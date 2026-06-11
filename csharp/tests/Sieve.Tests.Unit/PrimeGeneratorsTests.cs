using FluentAssertions;
using Sieve.Implementation.Estimation;
using Sieve.Implementation.Generation;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

public sealed class PrimeGeneratorsTests : TestBase
{
    public PrimeGeneratorsTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ClassicGenerator_GeneratesKnownPrimeSlice()
    {
        var estimator = new RosserSchoenfeldEstimator();
        var generator = new ClassicSieveGenerator(estimator);

        var primes = await generator.GeneratePrimesAsync(0, 9);

        primes.Should().Equal(2, 3, 5, 7, 11, 13, 17, 19, 23, 29);
    }

    [Fact]
    public async Task SegmentedGenerator_GeneratesKnownPrimeSlice()
    {
        var estimator = new RosserSchoenfeldEstimator();
        var generator = new SegmentedSieveGenerator(estimator, segmentSize: 512);

        var primes = await generator.GeneratePrimesAsync(0, 19);

        primes.Should().Equal(2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71);
    }

    [Fact]
    public async Task Generators_ReturnKnownPrimeAtIndex500()
    {
        var estimator = new RosserSchoenfeldEstimator();
        var classic = new ClassicSieveGenerator(estimator);
        var segmented = new SegmentedSieveGenerator(estimator, segmentSize: 2048);

        var classicPrime = (await classic.GeneratePrimesAsync(500, 500))[0];
        var segmentedPrime = (await segmented.GeneratePrimesAsync(500, 500))[0];

        classicPrime.Should().Be(3581);
        segmentedPrime.Should().Be(3581);
    }
}