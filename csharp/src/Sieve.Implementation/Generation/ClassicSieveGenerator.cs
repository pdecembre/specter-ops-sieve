using Sieve.Core.Abstractions;

namespace Sieve.Implementation.Generation;

/// <summary>
/// Classic Sieve of Eratosthenes implementation.
///
/// Best-fit use case:
/// - Smaller upper bounds where allocating a single contiguous composite map is efficient.
///
/// Complexity:
/// - Time: O(N log log N)
///   Explanation:
///   1) Let N be the estimated numeric upper bound value (the largest integer we sieve up to),
///      not the requested prime index.
///   2) Initialization and final scan are both linear in N.
///   3) Composite marking is performed for each prime p up to sqrt(N), and marks roughly N/p values.
///   4) Summing N/p over primes p yields N * (sum(1/p over primes)), which grows like N log log N.
///   5) Therefore total runtime is dominated by O(N log log N).
///
/// - Space: O(N)
///   Explanation:
///   1) The algorithm keeps one boolean composite flag per integer in [0..N].
///   2) This array is the dominant memory structure, so memory scales linearly with N.
///   3) Additional local variables and loop counters are O(1).
///
/// Thread-safety:
/// - Fully thread-safe because all state is local to method execution.
/// </summary>
public sealed class ClassicSieveGenerator : IPrimeGenerator
{
    private readonly IEstimator _estimator;

    public ClassicSieveGenerator(IEstimator estimator)
    {
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
    }

    /// <inheritdoc />
    public Task<long[]> GeneratePrimesAsync(
        long startIndex,
        long endIndex,
        CancellationToken cancellationToken = default)
    {
        if (startIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        if (endIndex < startIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex));
        }

        var limit = _estimator.EstimateUpperBound(endIndex);
        if (limit > int.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex), "Estimated limit exceeds current classic generator capacity.");
        }

        // Composite bitmap where false means "potentially prime" and true means "composite".
        // bool[] keeps logic straightforward; segmented generator handles very large ranges.
        var composite = new bool[limit + 1];

        // Base-case normalization for sieve semantics:
        // - Prime numbers are defined only for integers >= 2.
        // - Therefore 0 and 1 must be explicitly marked as composite (non-prime).
        //
        // Why keep defensive boundary checks?
        // - The array length is derived from 'limit + 1', so index 0 exists when limit >= 0,
        //   and index 1 exists when limit >= 1.
        // - Today, estimator outputs typically make both conditions true, but explicit guards
        //   prevent future regressions if estimation behavior changes or this method is reused
        //   with extremely small limits.
        //
        // Without this initialization, the default 'false' values for 0/1 would incorrectly
        // classify them as "potentially prime" during later scans.
        if (limit >= 0)
        {
            composite[0] = true;
        }

        if (limit >= 1)
        {
            composite[1] = true;
        }

        var sqrtLimit = (long)Math.Sqrt(limit);

        // Core sieve marking:
        // For each prime p, mark multiples from p^2 onward. Starting from p^2 is valid
        // because smaller multiples of p already have a smaller prime factor.
        // Complexity intuition for this loop:
        // - For a fixed p, inner loop work is about N/p markings.
        // - Summed over relevant prime p values, this produces the N log log N behavior.
        for (var p = 2L; p <= sqrtLimit; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (composite[p])
            {
                continue;
            }

            for (var multiple = p * p; multiple <= limit; multiple += p)
            {
                composite[multiple] = true;
            }
        }

        // Calculate the number of elements in the [startIndex, endIndex] range (inclusive).
        // 'endIndex' and 'startIndex' are long, so the result is computed in long arithmetic
        // to avoid overflow during subtraction. The 'checked' cast to int will throw an
        // OverflowException if the range is larger than int.MaxValue (~2.1 billion elements),
        // preventing a silent overflow that would otherwise produce an incorrect (and possibly
        // negative) array length.
        var resultLength = checked((int)(endIndex - startIndex + 1));
        var result = new long[resultLength];

        // Convert "prime value stream" into "prime index stream" and capture requested slice.
        // This pass is linear in N because each value is inspected at most once.
        var currentPrimeIndex = 0L;
        var outputIndex = 0;
        for (var value = 2L; value <= limit && outputIndex < result.Length; value++)
        {
            if (composite[value])
            {
                continue;
            }

            if (currentPrimeIndex >= startIndex)
            {
                result[outputIndex++] = value;
            }

            currentPrimeIndex++;
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public long EstimateMemoryUsage(long limit)
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        // bool[] is one byte per number in practical CLR layout plus array overhead.
        return limit + 64;
    }
}