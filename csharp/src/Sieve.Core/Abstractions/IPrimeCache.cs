namespace Sieve.Core.Abstractions;

/// <summary>
/// Repository abstraction for storing and retrieving previously computed primes.
///
/// <para>
/// WHY A CACHE EXISTS IN THIS ARCHITECTURE:
/// Computing the Nth prime — especially for large N — is expensive: the
/// 10,000,000th prime requires sieving through roughly 179 million integers.
/// Repeating that work on every request would make the API unusable for high
/// traffic. The cache allows the orchestrator to pay the generation cost exactly
/// once and serve all subsequent requests for the same (or nearby) indices in
/// sub-millisecond time.
/// </para>
///
/// <para>
/// THE REPOSITORY PATTERN — WHY THIS INTERFACE SHAPE:
/// The Repository pattern treats a data store as a collection-like abstraction.
/// Callers ask "do you have this item?" (TryGetPrimeRange) and "please store this
/// item" (AddPrimeRange) without needing to know whether the backing store is a
/// ConcurrentDictionary, a distributed Redis cluster, or an on-disk file.
/// This keeps the orchestrator decoupled from storage mechanics, making both
/// sides independently testable: you can unit-test the orchestrator with a mock
/// cache, and you can test the cache implementation in isolation.
/// </para>
///
/// <para>
/// LRU EVICTION — WHY IT IS REQUIRED:
/// Without a memory limit the cache would grow unboundedly during a session that
/// queries many different prime ranges. LRU (Least Recently Used) eviction
/// automatically discards entries that have not been accessed recently, keeping
/// memory usage within a configured budget while retaining the entries most
/// likely to be requested again soon.
/// </para>
///
/// <para>
/// THREAD-SAFETY CONTRACT:
/// All methods on this interface MUST be safe to call concurrently. The cache is
/// a singleton shared across all request-handling threads; without thread safety
/// concurrent reads and writes would corrupt internal data structures.
/// </para>
/// </summary>
public interface IPrimeCache
{
    /// <summary>
    /// Attempts to read the contiguous prime slice [<paramref name="startIndex"/>..
    /// <paramref name="endIndex"/>] from the cache in a single atomic-style operation.
    ///
    /// <para>
    /// WHY TryGet INSTEAD OF GET-OR-THROW:
    /// Using a Try/out pattern (returning a bool and writing the result into an
    /// out parameter) is the idiomatic .NET pattern for "this operation may not
    /// succeed". Compared to throwing a CacheMissException it avoids the overhead
    /// of constructing and unwinding an exception on every cache miss — which on a
    /// busy server would happen millions of times a second for any index that hasn't
    /// been computed yet. The caller simply checks the bool and falls back to
    /// generation when the result is false.
    /// </para>
    ///
    /// <para>
    /// PARTIAL-RANGE SEMANTICS:
    /// This method returns true ONLY when ALL requested indices are present in the
    /// cache. If even a single index in [startIndex..endIndex] is missing, the
    /// method returns false and the out parameter is set to an empty array. This
    /// strict all-or-nothing contract keeps callers simple: they never need to
    /// handle partially-populated results.
    /// </para>
    /// </summary>
    /// <param name="startIndex">
    /// First prime index to retrieve (0-based, inclusive).
    /// </param>
    /// <param name="endIndex">
    /// Last prime index to retrieve (0-based, inclusive).
    /// Must be &gt;= <paramref name="startIndex"/>.
    /// </param>
    /// <param name="primes">
    /// When this method returns true, contains an array of prime values in
    /// ascending order for indices [startIndex..endIndex].
    /// When this method returns false, contains an empty array.
    /// </param>
    /// <returns>
    /// True if every prime in the requested range was found in the cache;
    /// false if any index was missing or the range was invalid.
    /// </returns>
    bool TryGetPrimeRange(long startIndex, long endIndex, out long[] primes);

    /// <summary>
    /// Stores a contiguous block of prime values starting at the given index.
    /// Evicts least-recently-used entries if the memory budget would be exceeded.
    ///
    /// <para>
    /// WHY ReadOnlySpan&lt;long&gt; INSTEAD OF long[]:
    /// <see cref="ReadOnlySpan{T}"/> allows callers to pass a slice of an
    /// existing array — or a stack-allocated buffer — without copying it into a
    /// new heap-allocated array first. This reduces allocations on the hot path
    /// where generators produce large arrays that are immediately passed into the
    /// cache. The "ReadOnly" qualifier signals that the cache must not modify the
    /// original buffer; it must copy values it wants to retain.
    /// </para>
    ///
    /// <para>
    /// CONTIGUOUS RANGE CONTRACT:
    /// The primes span must represent a contiguous slice of the prime sequence
    /// starting at <paramref name="startIndex"/>. Element 0 of the span is the
    /// prime at index startIndex, element 1 at index startIndex+1, and so on.
    /// Storing a non-contiguous or out-of-order span produces undefined behavior.
    /// </para>
    /// </summary>
    /// <param name="startIndex">
    /// The 0-based prime index of the first element in <paramref name="primes"/>.
    /// Must be &gt;= 0.
    /// </param>
    /// <param name="primes">
    /// Contiguous prime values to store, starting at <paramref name="startIndex"/>.
    /// A empty span is a no-op.
    /// </param>
    void AddPrimeRange(long startIndex, ReadOnlySpan<long> primes);

    /// <summary>
    /// Returns the highest prime index currently held in the cache, or -1 if
    /// the cache is empty.
    ///
    /// <para>
    /// HOW THE ORCHESTRATOR USES THIS:
    /// Before requesting generation the orchestrator calls this method to find
    /// out where the cache currently ends. It then starts generation from the
    /// very next index (highestCachedIndex + 1) instead of regenerating
    /// already-cached primes. This "incremental fill" pattern ensures each prime
    /// is computed at most once across the lifetime of the cache instance.
    ///
    ///   Example: cache holds indices 0..4999.
    ///   GetHighestCachedIndex() returns 4999.
    ///   Orchestrator generates indices 5000..N and stores them.
    ///   Next request for any index in 0..N is served entirely from cache.
    /// </para>
    /// </summary>
    /// <returns>
    /// The largest 0-based prime index present in the cache,
    /// or -1 when the cache contains no entries.
    /// </returns>
    long GetHighestCachedIndex();

    /// <summary>
    /// Returns an immutable snapshot of cache performance counters at this moment.
    ///
    /// <para>
    /// INTENDED CONSUMERS:
    /// Diagnostics endpoints, health checks, and logging pipelines use this
    /// snapshot to observe whether the cache is effective (high hit ratio) or
    /// under-provisioned (low hit ratio, high memory usage near budget ceiling).
    /// Because the snapshot is immutable it is safe to pass across threads or
    /// serialize without additional locking.
    /// </para>
    /// </summary>
    /// <returns>
    /// A <see cref="Models.CacheStatistics"/> instance capturing the current
    /// hit count, miss count, entry count, and memory usage.
    /// </returns>
    Models.CacheStatistics GetStatistics();
}
