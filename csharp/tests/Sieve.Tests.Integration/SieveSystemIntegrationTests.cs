using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sieve.Core.Abstractions;
using Sieve.Extensions;
using Sieve.Implementation.Validation;
using CoreISieve = Sieve.Core.Abstractions.ISieve;

namespace Sieve.Tests.Integration;

/// <summary>
/// End-to-end integration tests that validate the full DI-composed system.
///
/// These tests intentionally exercise real implementations together:
/// estimator + generator + cache + metrics + orchestrator.
/// </summary>
public sealed class SieveSystemIntegrationTests
{
    private static readonly (long Index, long Prime)[] KnownCases =
    [
        (0, 2),
        (19, 71),
        (99, 541),
        (500, 3581),
        (986, 7793),
        (2_000, 17_393)
    ];

    [Fact]
    public void AddSieveServices_ComposedSystem_ResolvesAndComputesKnownValues()
    {
        using var provider = BuildProvider();
        var sieve = provider.GetRequiredService<CoreISieve>();

        foreach (var (index, expectedPrime) in KnownCases)
        {
            sieve.NthPrime(index).Should().Be(expectedPrime);
        }
    }

    [Fact]
    public async Task AddSieveServices_WithCustomConfiguration_ComputesCorrectly()
    {
        using var provider = BuildProvider(options =>
        {
            // Small settings force more cache chunk interactions and segmented passes.
            options.CacheChunkSize = 256;
            options.MaxCacheMemoryBytes = 2 * 1024 * 1024;
            options.SegmentSize = 16 * 1024;
        });

        var sieve = provider.GetRequiredService<CoreISieve>();
        var prime = await sieve.NthPrimeAsync(5_000);

        prime.Should().Be(48_619);
    }

    [Fact]
    public async Task SameIndexRepeated_FirstCallWarmsCache_SecondCallHitsCache()
    {
        using var provider = BuildProvider();

        var sieve = provider.GetRequiredService<CoreISieve>();
        var metrics = provider.GetRequiredService<IMetricsCollector>();

        var first = await sieve.NthPrimeAsync(20_000);
        var second = await sieve.NthPrimeAsync(20_000);

        first.Should().Be(second);
        PrimeValidator.IsPrime(first).Should().BeTrue();

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalRequests.Should().Be(2);
        snapshot.CacheHits.Should().BeGreaterThan(0);
        snapshot.CacheMisses.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConcurrentRequests_AcrossDifferentIndices_ReturnExpectedResults()
    {
        using var provider = BuildProvider();
        var sieve = provider.GetRequiredService<CoreISieve>();

        var requests = new[]
        {
            (Index: 50L, Expected: 233L),
            (Index: 99L, Expected: 541L),
            (Index: 500L, Expected: 3_581L),
            (Index: 2_000L, Expected: 17_393L),
            (Index: 10_000L, Expected: 104_743L)
        };

        var tasks = requests
            .Select(async r => (r.Index, Actual: await sieve.NthPrimeAsync(r.Index), r.Expected));

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            result.Actual.Should().Be(result.Expected, $"index {result.Index} should match known prime");
        }
    }

    [Fact]
    public async Task NthPrimeAsync_WithPreCancelledToken_ThrowsOperationCanceledException()
    {
        using var provider = BuildProvider();
        var sieve = provider.GetRequiredService<CoreISieve>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var action = async () => await sieve.NthPrimeAsync(1_000_000, cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ServiceProvider BuildProvider(Action<SieveConfiguration>? configure = null)
    {
        var services = new ServiceCollection();

        if (configure is null)
        {
            services.AddSieveServices();
        }
        else
        {
            services.AddSieveServices(configure);
        }

        return services.BuildServiceProvider();
    }
}