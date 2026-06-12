using FluentAssertions;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

/// <summary>
/// Verifies backward-compatible facade behavior exposed by the public Sieve project.
/// </summary>
public sealed class SieveFacadeCompatibilityTests : TestBase
{
    public SieveFacadeCompatibilityTests(ITestOutputHelper output) : base(output)
    {
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(19, 71)]
    [InlineData(99, 541)]
    [InlineData(500, 3581)]
    public void SieveFactory_Create_ReturnsCompatibleFacadeWithExpectedResults(long index, long expectedPrime)
    {
        var sieve = SieveFactory.Create();

        var actual = sieve.NthPrime(index);

        actual.Should().Be(expectedPrime);
    }

    [Fact]
    public void SieveImplementation_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var sieve = SieveFactory.Create();

        var action = () => sieve.NthPrime(-1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
