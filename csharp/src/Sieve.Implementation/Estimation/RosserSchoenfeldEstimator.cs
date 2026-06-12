using Sieve.Core.Abstractions;

namespace Sieve.Implementation.Estimation;

/// <summary>
/// Estimates upper bounds for the Nth prime using the Rosser-Schoenfeld inequality.
/// 
/// Mathematical context:
/// - Prime density decreases as values grow, but not arbitrarily fast.
/// - Rosser-Schoenfeld provides an explicit, practical upper bound:
///   p(n) &lt; n * (ln(n) + ln(ln(n))) for n &gt;= 6 (with 1-based n).
/// - This bound is suitable for sizing sieve arrays before generation starts.
/// 
/// Why this estimator exists in architecture:
/// 1) generation strategies need a value-space limit before they can mark composites,
/// 2) over-estimation is acceptable (more work), under-estimation is unsafe
///    (requested prime might not be included),
/// 3) isolating estimation behind <see cref="IEstimator"/> keeps algorithms
///    pluggable and testable.
/// 
/// Thread-safe: Yes (stateless, immutable behavior).
/// Formula: p(n) < n * (ln(n) + ln(ln(n))) for n >= 6 (1-based n).
///
/// Indexing note:
/// Public API uses 0-based prime indexing (0 -> 2, 1 -> 3, ...),
/// while the inequality is defined for 1-based n. The implementation converts with n + 1.
///
/// Practical design note:
/// The raw inequality is a strict upper bound asymptotically, but numerical implementations
/// can be sensitive around lower ranges. A safety multiplier is applied to reduce risk of
/// underestimation in downstream allocation/planning scenarios.
/// </summary>
public sealed class RosserSchoenfeldEstimator : IEstimator
{
    // Exact values for the earliest indices where direct lookup is simpler and fully precise.
    // These values -- known primes up to 6 -- also bypass log/log-log domain edge handling for small n.
    private static readonly long[] SmallPrimeUpperBounds = {2, 3, 5, 7, 11, 13};

    // Conservative buffer on top of formula output to avoid tight-bound edge failures.
    // 1.05 means +5% headroom.
    private const double SafetyMargin = 1.05;

    /// <inheritdoc />
    public long EstimateUpperBound(long n)
    {
        // Guard: 0-based prime index must be non-negative.
        // Negative index has no defined nth-prime meaning.
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Index cannot be negative.");
        }

        // Small-n fast path:
        // For the first 6 prime indices, return exact known values.
        // This keeps behavior deterministic and removes dependency on inequality fit in this range.
        if (n < SmallPrimeUpperBounds.Length)
        {
            return SmallPrimeUpperBounds[n];
        }

        // Convert the 0-based prime index to the 1-based "n" the formula expects (index 0 -> 1st prime, n=1).
        var oneBasedIndex = n + 1.0;

        // Compute ln(n) and ln(ln(n)) terms from Rosser-Schoenfeld.
        // For this branch oneBasedIndex >= 7, so both logs are well-defined.
        var logN = Math.Log(oneBasedIndex);
        var logLogN = Math.Log(logN);

        // Raw inequality-based estimate before safety expansion.
        var estimate = oneBasedIndex * (logN + logLogN);

        // Expand by safety margin and round upward to preserve upper-bound semantics.
        // Ceiling avoids truncation producing a value below the computed estimate.
        return (long)Math.Ceiling(estimate * SafetyMargin);
    }
}