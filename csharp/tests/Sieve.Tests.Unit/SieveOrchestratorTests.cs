using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sieve.Core.Abstractions;
using Sieve.Implementation;
using Sieve.Implementation.Caching;
using Sieve.Implementation.Estimation;
using Sieve.Implementation.Generation;
using Sieve.Implementation.Metrics;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;
using CoreISieve = Sieve.Core.Abstractions.ISieve;

namespace Sieve.Tests.Unit;

public sealed class SieveOrchestratorTests : TestBase
{
    public SieveOrchestratorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void NthPrime_ReturnsKnownValues()
    {
        var sieve = CreateSut();

        sieve.NthPrime(0).Should().Be(2);
        sieve.NthPrime(19).Should().Be(71);
        sieve.NthPrime(99).Should().Be(541);
    }

    [Fact]
    public async Task NthPrimeAsync_RepeatedCall_UsesCache()
    {
        var cache = new ConcurrentLruPrimeCache();
        var estimator = new RosserSchoenfeldEstimator();
        var metrics = new AtomicMetricsCollector();
        var classic = new ClassicSieveGenerator(estimator);
        var segmented = new SegmentedSieveGenerator(estimator, 2048);

        var sut = new SieveOrchestrator(
            cache,
            classic,
            segmented,
            estimator,
            metrics,
            NullLogger<SieveOrchestrator>.Instance);

        var first = await sut.NthPrimeAsync(2000);
        var second = await sut.NthPrimeAsync(2000);

        first.Should().Be(17393);
        second.Should().Be(17393);

        var stats = cache.GetStatistics();
        stats.CacheHits.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NthPrime_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var sieve = CreateSut();

        var action = () => sieve.NthPrime(-1);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CoreISieve CreateSut()
    {
        var estimator = new RosserSchoenfeldEstimator();
        return new SieveOrchestrator(
            new ConcurrentLruPrimeCache(),
            new ClassicSieveGenerator(estimator),
            new SegmentedSieveGenerator(estimator, 4096),
            estimator,
            new AtomicMetricsCollector(),
            NullLogger<SieveOrchestrator>.Instance);
    }
}