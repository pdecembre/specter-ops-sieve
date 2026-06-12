namespace Sieve.Core.Abstractions;

/// <summary>
/// Produces a numeric upper bound on the value of the Nth prime number.
///
/// <para>
/// WHY ESTIMATION IS NECESSARY IN THIS ARCHITECTURE:
/// Both the classic and segmented sieves need to know how large a number-line
/// range to cover before they begin marking composites. If you want the prime at
/// index N you must sieve at least up to the value of that prime — but you do
/// not know that value until after you have sieved (it is what you are trying to
/// find). The estimator breaks this chicken-and-egg problem by supplying a
/// conservative upper bound: "the Nth prime is definitely no larger than this
/// value, so sieving up to here is sufficient."
///
/// Over-estimating is always safe (the generator does a little extra work).
/// Under-estimating is a correctness bug (the requested prime falls outside the
/// sieved range and the generator would not find it).
/// </para>
///
/// <para>
/// THE ROSSER-SCHOENFELD INEQUALITY:
/// The standard implementation uses a result from analytic number theory known
/// as the Rosser-Schoenfeld bound (1962):
///
///   p(n) &lt; n × (ln(n) + ln(ln(n)))   for n ≥ 6  (1-based n)
///
/// where:
///   p(n)  = the value of the nth prime
///   ln    = natural logarithm
///
/// This formula gives a tight upper bound that grows slightly faster than the
/// actual prime sequence, ensuring the sieve always covers enough of the number
/// line while not grossly over-allocating memory.
///
/// For very small n (1 through 6) the logarithmic formula is impractical and the
/// exact prime values are used directly via a lookup table.
/// </para>
///
/// <para>
/// INDEXING NOTE — 0-BASED VS 1-BASED:
/// The public API of this interface uses 0-based indexing (index 0 = first prime
/// = value 2) to match the rest of the system. The mathematical inequality is
/// defined for 1-based n, so implementations must convert by adding 1 before
/// applying the formula.
/// </para>
///
/// <para>
/// THREAD-SAFETY CONTRACT:
/// Implementations MUST be stateless and thread-safe. Because the estimator is
/// a singleton injected into multiple components that may run concurrently, any
/// instance-level mutable state would introduce race conditions.
/// </para>
/// </summary>
public interface IEstimator
{
    /// <summary>
    /// Returns a value V such that the prime at index <paramref name="n"/> is
    /// guaranteed to be less than or equal to V.
    ///
    /// <para>
    /// UPPER BOUND CONTRACT:
    /// The return value V must satisfy: actual_prime(n) &lt;= V.
    /// It is acceptable — and expected — for V to be somewhat larger than the
    /// actual prime. A safety margin of a few percent is typical.
    /// </para>
    ///
    /// <para>
    /// HOW GENERATORS USE THIS VALUE:
    /// A generator calls EstimateUpperBound(endIndex) to obtain the numeric
    /// ceiling it must sieve up to in order to guarantee it will encounter at
    /// least (endIndex + 1) prime values. It then allocates its working bitmap
    /// or configures its segment loop based on that ceiling.
    /// </para>
    /// </summary>
    /// <param name="n">
    /// Zero-based prime index. Must be &gt;= 0.
    /// Index 0 corresponds to the first prime (value 2).
    /// </param>
    /// <returns>
    /// A numeric value V such that the prime at position <paramref name="n"/>
    /// is guaranteed to be &lt;= V.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="n"/> is negative.
    /// </exception>
    long EstimateUpperBound(long n);
}
