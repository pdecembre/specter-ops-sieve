namespace Sieve.Core.Abstractions;

/// <summary>
/// Strategy interface for prime-generation algorithms.
///
/// <para>
/// THE STRATEGY PATTERN — WHY THIS INTERFACE EXISTS:
/// Different prime-generation algorithms have dramatically different performance
/// profiles depending on the size of the range requested:
///
///   - The classic Sieve of Eratosthenes is very fast for small ranges because
///     it uses a single contiguous boolean array. However it requires O(N) memory
///     where N is the numeric value of the largest prime in scope — for the
///     10,000,000th prime (~179 million), that bitmap would occupy roughly 171 MB.
///
///   - The segmented sieve uses O(√N + segmentSize) memory by processing the
///     number line in small fixed-width windows. It is slightly slower for small
///     ranges but scales to arbitrarily large primes without exhausting memory.
///
/// By expressing both as implementations of IPrimeGenerator, the orchestrator can
/// select the appropriate algorithm at runtime based on the requested index, without
/// any of the calling code needing to know which implementation is in use. New
/// algorithms (for example a parallel sieve or a bit-packed sieve) can be introduced
/// by adding a new implementation class without touching any existing code.
/// </para>
///
/// <para>
/// INDEX SPACE VS VALUE SPACE — A CRITICAL DISTINCTION:
/// This interface operates entirely in "index space": the parameters startIndex
/// and endIndex are prime indices (0-based positions in the sequence 2, 3, 5, 7, ...),
/// not numeric values.
///
///   index 0  → value 2
///   index 4  → value 11
///   index 99 → value 541
///
/// The generator's job is to convert from index space to value space internally,
/// using the estimator to determine how far into the number line it needs to sieve.
/// Callers only see prime indices, never raw numeric limits.
/// </para>
///
/// <para>
/// THREAD-SAFETY CONTRACT:
/// Implementations MUST be safe to call concurrently. The simplest and recommended
/// way to achieve this is to keep all state local to the method call (no shared
/// mutable fields). Each invocation should allocate its own working buffers.
/// </para>
/// </summary>
public interface IPrimeGenerator
{
    /// <summary>
    /// Generates and returns the contiguous slice of the prime sequence from
    /// <paramref name="startIndex"/> to <paramref name="endIndex"/> inclusive.
    ///
    /// <para>
    /// RETURN VALUE CONTRACT:
    /// The returned array has exactly (endIndex - startIndex + 1) elements.
    /// Elements are in strictly ascending order. Element at position i in the
    /// returned array is the prime at absolute index (startIndex + i).
    ///
    ///   startIndex=0, endIndex=4  →  [2, 3, 5, 7, 11]
    ///   startIndex=3, endIndex=5  →  [7, 11, 13]
    /// </para>
    ///
    /// <para>
    /// WHY ASYNC:
    /// Generation of very large ranges (e.g., the first 10 million primes) can
    /// take several seconds. Making the method async allows implementations to
    /// yield the thread at natural checkpoints (for example between segment passes
    /// in the segmented sieve) so the thread-pool can service other work while
    /// generation is in progress. It also enables cooperative cancellation — see
    /// the <paramref name="cancellationToken"/> notes below.
    /// </para>
    /// </summary>
    /// <param name="startIndex">
    /// First prime index to include in the result (0-based, inclusive).
    /// Must be &gt;= 0.
    /// </param>
    /// <param name="endIndex">
    /// Last prime index to include in the result (0-based, inclusive).
    /// Must be &gt;= <paramref name="startIndex"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Token that signals the caller has abandoned the request. Implementations
    /// should check this between expensive inner-loop iterations and throw
    /// <see cref="OperationCanceledException"/> promptly when signalled.
    /// </param>
    /// <returns>
    /// Array of prime values in ascending order for indices
    /// [<paramref name="startIndex"/>..<paramref name="endIndex"/>].
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="startIndex"/> is negative, or when
    /// <paramref name="endIndex"/> is less than <paramref name="startIndex"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before
    /// generation completes.
    /// </exception>
    Task<long[]> GeneratePrimesAsync(
        long startIndex,
        long endIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a heuristic estimate of how many bytes this generator will
    /// allocate when generating primes up to numeric value <paramref name="limit"/>.
    ///
    /// <para>
    /// PURPOSE — CAPACITY PLANNING:
    /// Before the orchestrator invokes a generator it may call this method to
    /// understand the memory cost of the upcoming operation. This information
    /// can be used to:
    ///   1) Select between algorithms (prefer segmented when classic sieve's
    ///      O(N) bitmap would exceed available memory),
    ///   2) Emit warnings or metrics when a single request will consume a
    ///      significant share of the process's memory budget.
    /// </para>
    ///
    /// <para>
    /// ACCURACY EXPECTATIONS:
    /// The return value is a rough estimate, not a guaranteed allocation ceiling.
    /// It reflects the dominant data structure(s) used by the algorithm and
    /// excludes minor overhead such as local variables, stack frames, and
    /// GC object headers. Implementations should err on the side of slight
    /// over-estimation to avoid surprising callers with higher-than-expected usage.
    /// </para>
    /// </summary>
    /// <param name="limit">
    /// The numeric upper bound (a prime value, not an index) up to which
    /// generation would be performed.
    /// </param>
    /// <returns>
    /// Estimated peak memory usage in bytes.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="limit"/> is negative.
    /// </exception>
    long EstimateMemoryUsage(long limit);
}
