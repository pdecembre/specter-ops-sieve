using FluentAssertions;
using Sieve.Implementation.Validation;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

public sealed class PrimeValidatorTests : TestBase
{
    public PrimeValidatorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Theory]
    [InlineData(-10, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(9, false)]
    [InlineData(97, true)]
    [InlineData(99, false)]
    public void IsPrime_ReturnsExpectedResult(long n, bool expected)
    {
        PrimeValidator.IsPrime(n).Should().Be(expected);
    }

    [Fact]
    public void IsPrime_ValidatesFirst100Primes()
    {
        var first100Primes = new[]
        {
            2L, 3, 5, 7, 11, 13, 17, 19, 23, 29,
            31, 37, 41, 43, 47, 53, 59, 61, 67, 71,
            73, 79, 83, 89, 97, 101, 103, 107, 109, 113,
            127, 131, 137, 139, 149, 151, 157, 163, 167, 173,
            179, 181, 191, 193, 197, 199, 211, 223, 227, 229,
            233, 239, 241, 251, 257, 263, 269, 271, 277, 281,
            283, 293, 307, 311, 313, 317, 331, 337, 347, 349,
            353, 359, 367, 373, 379, 383, 389, 397, 401, 409,
            419, 421, 431, 433, 439, 443, 449, 457, 461, 463,
            467, 479, 487, 491, 499, 503, 509, 521, 523, 541
        };

        foreach (var prime in first100Primes)
        {
            PrimeValidator.IsPrime(prime).Should().BeTrue($"{prime} should be prime");
        }
    }

    [Fact]
    public void AreConsecutivePrimes_WithEmptyInput_ReturnsTrue()
    {
        PrimeValidator.AreConsecutivePrimes(ReadOnlySpan<long>.Empty).Should().BeTrue();
    }

    [Fact]
    public void AreConsecutivePrimes_WithConsecutivePrimeSequence_ReturnsTrue()
    {
        long[] candidates = [11, 13, 17, 19, 23, 29, 31];

        PrimeValidator.AreConsecutivePrimes(candidates).Should().BeTrue();
    }

    [Fact]
    public void AreConsecutivePrimes_WithMissingPrime_ReturnsFalse()
    {
        long[] candidates = [11, 17];

        PrimeValidator.AreConsecutivePrimes(candidates).Should().BeFalse();
    }

    [Fact]
    public void AreConsecutivePrimes_WithCompositeValue_ReturnsFalse()
    {
        long[] candidates = [11, 13, 15, 17];

        PrimeValidator.AreConsecutivePrimes(candidates).Should().BeFalse();
    }

    [Fact]
    public void AreConsecutivePrimes_WithNonIncreasingSequence_ReturnsFalse()
    {
        long[] candidates = [11, 13, 13, 17];

        PrimeValidator.AreConsecutivePrimes(candidates).Should().BeFalse();
    }
}