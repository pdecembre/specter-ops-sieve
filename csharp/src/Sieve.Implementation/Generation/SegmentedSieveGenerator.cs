using Sieve.Core.Abstractions;

namespace Sieve.Implementation.Generation;

/// <summary>
/// Segmented Sieve of Eratosthenes for large ranges.
///
/// Key idea:
/// - Precompute base primes up to sqrt(limit).
/// - Process [2..limit] in fixed-size segments, marking composites per segment.
///
/// Complexity:
/// - Time: O(N log log N)
///   Explanation:
///   1) Let N be the estimated numeric upper bound (largest integer tested), not prime index.
///   2) Base-prime generation up to sqrt(N) costs about O(sqrt(N) log log sqrt(N)).
///   3) Segment processing marks multiples using those base primes across the full range [2..N].
///   4) Aggregate marking work over all relevant primes follows the same harmonic-on-primes
///      behavior as classic sieve, yielding O(N log log N).
///   5) Final collection scan across all segments is linear in N and does not change the bound.
/// - Space: O(sqrt(N) + segmentSize)
///   Explanation:
///   1) Base primes require a sieve buffer up to sqrt(N), plus the resulting prime list.
///   2) At runtime only one segment bitmap of size segmentSize is held at a time.
///   3) Therefore memory is sub-linear in N and avoids the full O(N) bitmap required
///      by classic sieve for very large bounds.
///
/// Thread-safety:
/// - All mutable state is local to the request; safe for concurrent use.
/// </summary>
public sealed class SegmentedSieveGenerator : IPrimeGenerator
{
    private readonly IEstimator _estimator;
    private readonly int _segmentSize;

    public SegmentedSieveGenerator(IEstimator estimator, int segmentSize = 1_048_576)
    {
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));

        if (segmentSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentSize));
        }

        _segmentSize = segmentSize;
    }

    /// <inheritdoc />
    public async Task<long[]> GeneratePrimesAsync(
        long startIndex,
        long endIndex,
        CancellationToken cancellationToken = default)
    {
        // Input contract: prime indices are 0-based and must form a valid inclusive range.
        if (startIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        if (endIndex < startIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex));
        }

        // Convert from requested prime index space into value space via estimator.
        // 'limit' is the largest integer value we may need to inspect.
        var limit = _estimator.EstimateUpperBound(endIndex);
        var sqrtLimit = (long)Math.Sqrt(limit);

        // Base primes are all primes <= sqrt(limit). They are sufficient to mark
        // every composite number in every subsequent segment.
        var basePrimes = GenerateBasePrimes(sqrtLimit, cancellationToken);

        // Result array size equals the requested inclusive index count.
        var requestedCount = checked((int)(endIndex - startIndex + 1));
        var result = new long[requestedCount];
        var resultOffset = 0;

        // Tracks global prime index as we stream prime values in ascending order.
        // Example: value=2 -> index 0, value=3 -> index 1, etc.
        var currentPrimeIndex = 0L;

        // Segment loop walks value space in fixed windows [low..high].
        // This is the core memory optimization: reuse a small bitmap per segment.
        for (var low = 2L; low <= limit && resultOffset < requestedCount; low += _segmentSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var high = Math.Min(limit, low + _segmentSize - 1);
            var segmentLength = checked((int)(high - low + 1));

            // Per-segment composite map:
            // - false = currently unmarked (potentially prime)
            // - true  = known composite
            var isComposite = new bool[segmentLength];

            // Mark this segment using precomputed base primes.
            foreach (var prime in basePrimes)
            {
                // firstMultiple selection rules:
                // 1) CeilingDiv(low, prime) * prime = first multiple of 'prime' within/after low.
                // 2) prime^2 lower-bound is safe because smaller multiples were already handled
                //    when processing smaller prime factors.
                // Taking max of the two gives the first relevant marking point in this segment.
                var firstMultiple = Math.Max(prime * prime, CeilingDiv(low, prime) * prime);

                // Mark every k*prime in this segment as composite.
                // Index translation: absolute value -> segment-local offset via (multiple - low).
                for (var multiple = firstMultiple; multiple <= high; multiple += prime)
                {
                    isComposite[multiple - low] = true;
                }
            }

            // Collect primes from the current segment while tracking global prime index.
            for (var value = low; value <= high && resultOffset < requestedCount; value++)
            {
                // Non-prime conditions:
                // - value < 2 (definitional exclusion)
                // - marked composite by any base prime
                if (value < 2 || isComposite[value - low])
                {
                    continue;
                }

                // Prime discovered in ascending value order.
                // Emit only when its global index is inside requested [startIndex..endIndex].
                if (currentPrimeIndex >= startIndex)
                {
                    result[resultOffset++] = value;
                }

                currentPrimeIndex++;
            }

            // Cooperative yield for very large requests to avoid monopolizing the thread.
            await Task.Yield();
        }

        // Result is guaranteed ordered because segments and in-segment scans are increasing.
        return result;
    }

    /// <inheritdoc />
    public long EstimateMemoryUsage(long limit)
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        // Heuristic estimate:
        // - sqrtLimit approximates base-sieve/storage contribution.
        // - segmentSize approximates active segment bitmap contribution.
        // - constant offset accounts for object/container overhead.
        var sqrtLimit = (long)Math.Sqrt(limit);
        return (sqrtLimit + _segmentSize) + 128;
    }

    private static long[] GenerateBasePrimes(long limit, CancellationToken cancellationToken)
    {
        // No primes exist below 2.
        if (limit < 2)
        {
            return Array.Empty<long>();
        }

        // This implementation currently uses bool[] indexed by int range.
        if (limit > int.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Base sieve limit exceeds current capacity.");
        }

        // Classic sieve over [0..limit] to obtain all base primes <= sqrt(mainLimit).
        var composite = new bool[limit + 1];
        composite[0] = true;
        composite[1] = true;

        var sqrt = (long)Math.Sqrt(limit);
        for (var p = 2L; p <= sqrt; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // If already composite, p is not a prime base and contributes no new marking pattern.
            if (composite[p])
            {
                continue;
            }

            // Mark multiples of prime p from p^2 upward.
            for (var multiple = p * p; multiple <= limit; multiple += p)
            {
                composite[multiple] = true;
            }
        }

        // Emit ascending base prime list.
        var primes = new List<long>();
        for (var n = 2L; n <= limit; n++)
        {
            if (!composite[n])
            {
                primes.Add(n);
            }
        }

        return primes.ToArray();
    }

    private static long CeilingDiv(long numerator, long denominator)
    {
        // Small helper for positive integer ranges used in segment alignment.
        // Returns the smallest integer q where q * denominator >= numerator.
        return (numerator + denominator - 1) / denominator;
    }
}