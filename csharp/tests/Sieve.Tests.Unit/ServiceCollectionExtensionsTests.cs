using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sieve.Core.Abstractions;
using Sieve.Extensions;
using Sieve.Implementation.Caching;
using Sieve.Implementation.Estimation;
using Sieve.Implementation.Generation;
using Sieve.Implementation.Metrics;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;
using CoreISieve = Sieve.Core.Abstractions.ISieve;

namespace Sieve.Tests.Unit;

/// <summary>
/// Unit-level coverage for DI registration behavior.
///
/// Goal: ensure phase wiring stays deterministic and backward compatible when
/// service registrations evolve.
/// </summary>
public sealed class ServiceCollectionExtensionsTests : TestBase
{
    public ServiceCollectionExtensionsTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void AddSieveServices_RegistersCoreContracts()
    {
        var services = new ServiceCollection();
        services.AddSieveServices();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<CoreISieve>().Should().NotBeNull();
        provider.GetRequiredService<IEstimator>().Should().BeOfType<RosserSchoenfeldEstimator>();
        provider.GetRequiredService<IPrimeCache>().Should().BeOfType<ConcurrentLruPrimeCache>();
        provider.GetRequiredService<IMetricsCollector>().Should().BeOfType<AtomicMetricsCollector>();

        // We expose both concrete generators and IPrimeGenerator strategy registrations.
        provider.GetRequiredService<ClassicSieveGenerator>().Should().NotBeNull();
        provider.GetRequiredService<SegmentedSieveGenerator>().Should().NotBeNull();
        provider.GetServices<IPrimeGenerator>().Should().HaveCount(2);
    }

    [Fact]
    public void AddSieveServices_WithCustomConfiguration_StoresConfiguredValues()
    {
        var services = new ServiceCollection();

        services.AddSieveServices(options =>
        {
            options.CacheChunkSize = 777;
            options.MaxCacheMemoryBytes = 8 * 1024 * 1024;
            options.SegmentSize = 32 * 1024;
        });

        using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredService<SieveConfiguration>();

        configuration.CacheChunkSize.Should().Be(777);
        configuration.MaxCacheMemoryBytes.Should().Be(8 * 1024 * 1024);
        configuration.SegmentSize.Should().Be(32 * 1024);
    }

    [Fact]
    public void AddSieveServices_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var action = () => services.AddSieveServices(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}
