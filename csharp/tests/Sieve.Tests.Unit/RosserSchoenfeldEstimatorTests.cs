using FluentAssertions;
using Sieve.Core.Abstractions;
using Sieve.Implementation.Estimation;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

public sealed class RosserSchoenfeldEstimatorTests : TestBase
{
    private readonly IEstimator _estimator = new RosserSchoenfeldEstimator();

    public RosserSchoenfeldEstimatorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void EstimateUpperBound_WithNegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var action = () => _estimator.EstimateUpperBound(-1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    [InlineData(4, 11)]
    [InlineData(5, 13)]
    public void EstimateUpperBound_ForSmallIndices_ReturnsKnownBounds(long n, long expected)
    {
        var bound = _estimator.EstimateUpperBound(n);

        bound.Should().Be(expected);
    }

    [Theory]
    [InlineData(6, 17)]
    [InlineData(100, 547)]
    [InlineData(1_000, 7_927)]
    [InlineData(10_000, 104_743)]
    public void EstimateUpperBound_ForKnownPrimeIndices_IsAtLeastActualPrime(long n, long knownPrime)
    {
        var bound = _estimator.EstimateUpperBound(n);

        bound.Should().BeGreaterThanOrEqualTo(knownPrime);
    }

    [Fact]
    public void EstimateUpperBound_IsMonotonicallyNonDecreasing()
    {
        long? previous = null;
        for (var n = 0; n <= 2_000; n++)
        {
            var current = _estimator.EstimateUpperBound(n);
            if (previous.HasValue)
            {
                current.Should().BeGreaterThanOrEqualTo(previous.Value);
            }

            previous = current;
        }
    }
}