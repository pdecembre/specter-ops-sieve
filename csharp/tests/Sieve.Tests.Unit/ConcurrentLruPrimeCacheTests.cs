using FluentAssertions;
using Sieve.Implementation.Caching;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

public sealed class ConcurrentLruPrimeCacheTests : TestBase
{
    public ConcurrentLruPrimeCacheTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void TryGetPrimeRange_EmptyCache_ReturnsFalse()
    {
        var cache = new ConcurrentLruPrimeCache();

        var found = cache.TryGetPrimeRange(0, 0, out _);

        found.Should().BeFalse();
    }

    [Fact]
    public void AddPrimeRange_ThenGet_ReturnsStoredValues()
    {
        var cache = new ConcurrentLruPrimeCache();
        long[] primes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29];

        cache.AddPrimeRange(0, primes);

        var found = cache.TryGetPrimeRange(0, 9, out var loaded);

        found.Should().BeTrue();
        loaded.Should().Equal(primes);
    }

    [Fact]
    public void AddPrimeRange_MultipleChunks_RangeSpanningChunksIsReturnedCorrectly()
    {
        var chunkSize = 4;
        var cache = new ConcurrentLruPrimeCache(maxMemoryBytes: 10_000_000, chunkSize: chunkSize);
        long[] primes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29];

        cache.AddPrimeRange(0, primes);

        var found = cache.TryGetPrimeRange(3, 7, out var loaded);

        found.Should().BeTrue();
        loaded.Should().Equal(7, 11, 13, 17, 19);
    }

    [Fact]
    public void GetHighestCachedIndex_ReturnsExpectedMaximum()
    {
        var cache = new ConcurrentLruPrimeCache();

        cache.GetHighestCachedIndex().Should().Be(-1);

        cache.AddPrimeRange(100, [541, 547, 557]);
        cache.GetHighestCachedIndex().Should().Be(102);
    }

    [Fact]
    public void GetStatistics_TracksHitsAndMisses()
    {
        var cache = new ConcurrentLruPrimeCache();
        cache.AddPrimeRange(0, [2, 3, 5]);

        cache.TryGetPrimeRange(0, 1, out _).Should().BeTrue();
        cache.TryGetPrimeRange(8, 8, out _).Should().BeFalse();

        var stats = cache.GetStatistics();
        stats.TotalRequests.Should().Be(2);
        stats.CacheHits.Should().Be(1);
        stats.CacheMisses.Should().Be(1);
    }
}