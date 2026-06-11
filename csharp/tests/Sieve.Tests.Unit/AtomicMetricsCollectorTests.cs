using FluentAssertions;
using Sieve.Core.Abstractions;
using Sieve.Implementation.Metrics;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

public sealed class AtomicMetricsCollectorTests : TestBase
{
    private readonly IMetricsCollector _collector = new AtomicMetricsCollector();

    public AtomicMetricsCollectorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void GetSnapshot_OnNewCollector_ReturnsZeroedMetrics()
    {
        var snapshot = _collector.GetSnapshot();

        snapshot.TotalRequests.Should().Be(0);
        snapshot.CacheHits.Should().Be(0);
        snapshot.CacheMisses.Should().Be(0);
        snapshot.GenerationCalls.Should().Be(0);
        snapshot.TotalPrimesGenerated.Should().Be(0);
    }

    [Fact]
    public void RecordMethods_UpdateCountersCorrectly()
    {
        _collector.RecordRequest();
        _collector.RecordRequest();
        _collector.RecordCacheHit();
        _collector.RecordCacheMiss();
        _collector.RecordGeneration(100);
        _collector.RecordGeneration(50);

        var snapshot = _collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(2);
        snapshot.CacheHits.Should().Be(1);
        snapshot.CacheMisses.Should().Be(1);
        snapshot.GenerationCalls.Should().Be(2);
        snapshot.TotalPrimesGenerated.Should().Be(150);
    }

    [Fact]
    public void RecordGeneration_AllowsZeroAndNegativeValuesForAccounting()
    {
        _collector.RecordGeneration(0);
        _collector.RecordGeneration(-5);

        var snapshot = _collector.GetSnapshot();
        snapshot.GenerationCalls.Should().Be(2);
        snapshot.TotalPrimesGenerated.Should().Be(-5);
    }

    [Fact]
    public async Task RecordMethods_AreThreadSafeUnderConcurrency()
    {
        const int workers = 8;
        const int iterations = 1_000;

        var tasks = Enumerable.Range(0, workers)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    _collector.RecordRequest();
                    _collector.RecordCacheHit();
                    _collector.RecordCacheMiss();
                    _collector.RecordGeneration(2);
                }
            }));

        await Task.WhenAll(tasks);

        var expected = workers * iterations;
        var snapshot = _collector.GetSnapshot();

        snapshot.TotalRequests.Should().Be(expected);
        snapshot.CacheHits.Should().Be(expected);
        snapshot.CacheMisses.Should().Be(expected);
        snapshot.GenerationCalls.Should().Be(expected);
        snapshot.TotalPrimesGenerated.Should().Be(expected * 2L);
    }
}