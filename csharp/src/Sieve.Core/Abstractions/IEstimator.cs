namespace Sieve.Core.Abstractions;

/// <summary>
/// Interface for estimating upper bounds for the Nth prime number.
/// Used to size data structures appropriately before prime generation.
/// Thread-safe: Implementations should be stateless.
/// </summary>
public interface IEstimator
{
    /// <summary>
    /// Estimates an upper bound for the Nth prime number.
    /// Uses mathematical inequalities (e.g., Rosser-Schoenfeld) to provide a reliable upper bound.
    /// Formula: p(n) &lt; n(ln(n) + ln(ln(n))) for n ≥ 6
    /// </summary>
    /// <param name="n">Prime index (0-based, where 0 corresponds to the first prime: 2)</param>
    /// <returns>Upper bound estimate for the Nth prime</returns>
    /// <exception cref="ArgumentOutOfRangeException">If n &lt; 0</exception>
    long EstimateUpperBound(long n);
}
