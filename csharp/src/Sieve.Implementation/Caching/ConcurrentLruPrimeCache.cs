using System.Collections.Concurrent;
using System.Threading;
using Sieve.Core.Abstractions;
using Sieve.Core.Models;

namespace Sieve.Implementation.Caching;

/// <summary>
/// A thread-safe, bounded-memory cache for storing and retrieving previously
/// computed prime numbers.
///
/// <para>
/// DESIGN GOALS — AND THEIR TENSIONS:
/// 1) Correctness under concurrent reads/writes: many requests arriving
///    simultaneously from different threads must see consistent results and
///    never corrupt the cache's internal data structures.
/// 2) Fast single-index reads: the dominant use case is "give me the prime at
///    index n," which should complete in sub-millisecond time on a warm cache
///    without locks or allocations.
/// 3) Bounded memory usage: the cache must not grow unboundedly. When total
///    usage exceeds a configured limit, entries should be evicted without
///    pausing the main request path.
///
/// Balancing these goals requires careful architecture: lock-free atomic
/// counters instead of locks for hot-path correctness, chunking to reduce
/// allocation churn, and background eviction to avoid blocking generators.
/// </para>
///
/// <para>
/// WHY CHUNKING:
/// Naive dictionary-per-prime (key = index, value = prime) would result in
/// millions of allocations and dictionary entries for large prime ranges. Instead,
/// prime indices are grouped into fixed-size chunks:
///
///   chunkKey = index / chunkSize
///   chunkStartIndex = chunkKey * chunkSize
///
/// Each chunk stores a fixed-length array of `chunkSize` prime values. The cache
/// holds at most (maxMemoryBytes / bytesPerChunk) chunks, keeping memory
/// proportional to the budget regardless of how many distinct indices have been
/// accessed.
///
/// Example with chunkSize = 10,000:
///   Chunk 0: indices 0..9,999
///   Chunk 1: indices 10,000..19,999
///   Chunk 2: indices 20,000..29,999
/// </para>
///
/// <para>
/// UNKNOWN SLOT REPRESENTATION — THE ZERO SENTINEL:
/// Within a chunk, not all slots may be populated (for example, if generation
/// skipped indices 5,000..7,999 and only filled 0..4,999 and 8,000..9,999).
/// These unknown slots are represented with the value 0. Since primes are always
/// > 1, the sentinel is unambiguous and requires no extra metadata.
/// </para>
///
/// <para>
/// CONCURRENCY MODEL:
/// - The ConcurrentDictionary ensures chunk-level atomicity: multiple threads
///   can read/update different chunks without contention.
/// - Per-chunk access timestamps are updated with Interlocked.Exchange so the
///   LRU rank can be read safely without locks.
/// - Global counters (_totalRequests, _cacheHits, etc.) are maintained with
///   Interlocked operations, avoiding lock overhead for frequently-updated
///   metrics.
/// - Eviction happens asynchronously behind a lightweight Monitor to prevent
///   competing eviction passes and allow the request path to return control
///   immediately after the budget is exceeded.
/// </para>
/// </summary>
public sealed class ConcurrentLruPrimeCache : IPrimeCache
{
    /// <summary>
    /// The dictionary backing the cache. Keys are chunk indices (chunkKey =
    /// primeIndex / chunkSize), values are <see cref="CacheEntry"/> instances
    /// containing the actual prime values and metadata.
    /// </summary>
    private readonly ConcurrentDictionary<long, CacheEntry> _cache = new();

    /// <summary>
    /// The memory usage ceiling in bytes. When _currentMemoryBytes exceeds this,
    /// eviction is triggered. The caller can tune this to balance cache
    /// effectiveness (larger = more hits) against memory footprint.
    /// </summary>
    private readonly long _maxMemoryBytes;

    /// <summary>
    /// The number of prime indices per chunk. A fixed chunk size allows for
    /// predictable memory calculations and reduces per-chunk overhead.
    /// Typical value is 10,000 (80 KB per fully populated chunk + overhead).
    /// </summary>
    private readonly int _chunkSize;

    /// <summary>
    /// Lightweight mutual-exclusion object for serializing concurrent eviction
    /// passes. Eviction is sufficiently expensive that we want to prevent two
    /// threads from evicting concurrently; using a simple object lock is more
    /// efficient than a ReaderWriterLockSlim for this use case.
    /// </summary>
    private readonly object _evictionLock = new();

    // Atomic counters maintained with Interlocked operations.
    // These track cache behavior for observability and diagnostics.
    /// <summary>
    /// Total number of lookup attempts ever made against this cache instance.
    /// </summary>
    private long _totalRequests;

    /// <summary>
    /// Number of lookup attempts that found all requested indices in the cache.
    /// </summary>
    private long _cacheHits;

    /// <summary>
    /// Number of lookup attempts that could not find all requested indices.
    /// </summary>
    private long _cacheMisses;

    /// <summary>
    /// Current estimated memory usage in bytes. Compared against _maxMemoryBytes
    /// to determine when eviction should be triggered.
    /// </summary>
    private long _currentMemoryBytes;

    /// <summary>
    /// Creates a new cache with the specified memory budget and chunking policy.
    ///
    /// <para>
    /// PARAMETER DEFAULTS — TUNING GUIDANCE:
    ///
    ///   maxMemoryBytes = 100 MB:
    ///     This allocates roughly 1,250 fully populated chunks (100 MB / 80 KB per chunk).
    ///     For typical access patterns where requests stay within the first few million
    ///     primes, this is usually sufficient. For applications that query widely
    ///     scattered indices (for example millions of random primes), increase this.
    ///
    ///   chunkSize = 10,000:
    ///     Each chunk holds 10,000 long values = 80,000 bytes of prime storage.
    ///     This balances chunk-replacement granularity against overhead. Smaller chunks
    ///     allow finer-grained eviction and reduce worst-case allocation fragmentation.
    ///     Larger chunks reduce the number of dictionary entries and improve cache hits
    ///     for sequential access patterns.
    /// </para>
    /// </summary>
    /// <param name="maxMemoryBytes">
    /// Maximum total cache memory in bytes. Must be positive. When cache usage
    /// exceeds this, background eviction removes least-recently-used chunks.
    /// </param>
    /// <param name="chunkSize">
    /// Number of prime values per chunk. Must be positive. Typical range is
    /// 1,000 to 100,000 depending on your request patterns and available memory.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when maxMemoryBytes &lt;= 0 or chunkSize &lt;= 0.
    /// </exception>
    public ConcurrentLruPrimeCache(long maxMemoryBytes = 100L * 1024 * 1024, int chunkSize = 10_000)
    {
        if (maxMemoryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemoryBytes), "Maximum memory must be positive.");
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        _maxMemoryBytes = maxMemoryBytes;
        _chunkSize = chunkSize;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// IMPLEMENTATION STRATEGY — ALL-OR-NOTHING SEMANTICS:
    /// This method returns true ONLY when every single prime in the requested
    /// range [startIndex..endIndex] is found in the cache. If any index is
    /// missing or unknown (represented by zero), the entire request is rejected
    /// and false is returned. This keeps callers simple: they never have to
    /// handle partial results or partially-filled ranges.
    /// </para>
    ///
    /// <para>
    /// WHY CHUNK-BY-CHUNK FETCHES INSTEAD OF BUFFER-COPY:
    /// For a request spanning multiple chunks (for example startIndex=5,000,
    /// endIndex=15,000 with chunkSize=10,000) the method fetches each chunk
    /// independently from the dictionary and validates every index before
    /// building the result array. This is slightly slower than a single
    /// copy if all chunks exist, but is correct for partially-populated
    /// chunks: if chunk 0 exists but chunk 1 doesn't, this loop detects the
    /// missing chunk immediately rather than returning an incomplete array.
    /// </para>
    ///
    /// <para>
    /// Least Recently Used.
    /// LRU TOUCH — UPDATING ACCESS TIME:
    /// When an entry is successfully read, its LastAccessTicks timestamp is
    /// updated via entry.Touch(). This ensures the LRU eviction pass will
    /// prefer to remove chunks that have not been accessed recently.
    /// 
    /// An LRU cache operates on the principle of temporal locality—the idea 
    /// that data you accessed recently is highly likely to be accessed again soon.
    /// When your cache fills up to its limit, it needs a rule to decide what to throw away to make room for new data. 
    /// The LRU rule is simple: Evict the item that hasn't been looked at or modified for the longest period of time.
    /// 
    /// </para>
    /// </remarks>
    public bool TryGetPrimeRange(long startIndex, long endIndex, out long[] primes)
    {
        // Count every lookup attempt, including invalid ranges and misses.
        Interlocked.Increment(ref _totalRequests);

        if (startIndex < 0 || endIndex < startIndex)
        {
            primes = Array.Empty<long>();
            Interlocked.Increment(ref _cacheMisses);
            return false;
        }

        var length = checked((int)(endIndex - startIndex + 1));
        var result = new long[length];

        // Fetch each absolute index independently. This approach prioritizes correctness
        // for partially populated chunks over raw throughput in large range reads.
        for (var i = 0; i < length; i++)
        {
            var absoluteIndex = startIndex + i;
            var chunkKey = absoluteIndex / _chunkSize;

            if (!_cache.TryGetValue(chunkKey, out var entry))
            {
                primes = Array.Empty<long>();
                Interlocked.Increment(ref _cacheMisses);
                return false;
            }

            entry.Touch();

            if (!entry.TryReadPrimeAt(absoluteIndex, out var prime))
            {
                primes = Array.Empty<long>();
                Interlocked.Increment(ref _cacheMisses);
                return false;
            }

            result[i] = prime;
        }

        // Full range was resolved from cache without gaps.
        primes = result;
        Interlocked.Increment(ref _cacheHits);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// SPARSE UPDATES — HANDLING PARTIAL CHUNKS:
    /// The incoming span may not align with chunk boundaries. For example,
    /// startIndex might be 5,000 within a 10,000-element chunk. This method
    /// walks the span and projects each element into the appropriate chunk at
    /// the correct offset, handling wraparound to the next chunk when needed.
    ///
    /// For each chunk touched, a new CacheEntry is created with the sparse
    /// update data. The HighestKnownIndex tracks which elements in that chunk
    /// are actually populated (primes > 1) vs unknown (zero).
    /// </para>
    ///
    /// <para>
    /// MERGE STRATEGY — PRESERVING EXISTING DATA:
    /// If a chunk already exists in the cache, the AddOrUpdate callback merges:
    ///   1) Copy the existing chunk's array.
    ///   2) Overlay the newly-provided values, but only where the new data
    ///      contains actual primes (> 1).
    ///   3) Update HighestKnownIndex to reflect both old and new populated slots.
    ///
    /// This ensures that repeated generation calls for overlapping ranges don't
    /// lose data. For example:
    ///   First call: AddPrimeRange(0, [2, 3, 5, ...])     chunk 0 populated 0..9
    ///   Second call: AddPrimeRange(8, [11, 13, ...])     merge with chunk 0,
    ///                                                     now 0..9 fully populated
    /// </para>
    ///
    /// <para>
    /// ASYNC EVICTION TRIGGER:
    /// After all chunks have been added/merged, if total memory exceeds the
    /// budget, EvictLruEntries is scheduled asynchronously. This avoids
    /// blocking the producer (generator or orchestrator) while old entries are
    /// removed. The eviction task runs in the background thread pool.
    /// </para>
    /// </remarks>
    public void AddPrimeRange(long startIndex, ReadOnlySpan<long> primes)
    {
        if (startIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be non-negative.");
        }

        if (primes.IsEmpty)
        {
            return;
        }

        // Walk the incoming span and project it chunk-by-chunk into cache entries.
        var sourceOffset = 0;
        while (sourceOffset < primes.Length)
        {
            var absoluteIndex = startIndex + sourceOffset;
            var chunkKey = absoluteIndex / _chunkSize;
            var chunkStartIndex = chunkKey * _chunkSize;
            var offsetInChunk = (int)(absoluteIndex - chunkStartIndex);
            var remainingInChunk = _chunkSize - offsetInChunk;
            var copyLength = Math.Min(remainingInChunk, primes.Length - sourceOffset);

            // Build sparse update payload for this single chunk.
            var update = new long[_chunkSize];
            var highestKnownIndex = chunkStartIndex - 1;

            for (var i = 0; i < copyLength; i++)
            {
                var value = primes[sourceOffset + i];
                update[offsetInChunk + i] = value;

                if (value > 1)
                {
                    highestKnownIndex = chunkStartIndex + offsetInChunk + i;
                }
            }

            var newEntry = new CacheEntry(chunkStartIndex, update, highestKnownIndex);

            _cache.AddOrUpdate(
                chunkKey,
                _ =>
                {
                    // New chunk insertion increases memory budget usage.
                    Interlocked.Add(ref _currentMemoryBytes, newEntry.SizeBytes);
                    return newEntry;
                },
                (_, existing) =>
                {
                    // Merge strategy keeps existing known values and overlays
                    // newly computed values for populated slots.
                    var merged = new long[_chunkSize];
                    Array.Copy(existing.Primes, merged, _chunkSize);

                    var mergedHighest = existing.HighestKnownIndex;
                    for (var i = 0; i < _chunkSize; i++)
                    {
                        if (update[i] > 1)
                        {
                            merged[i] = update[i];
                            mergedHighest = Math.Max(mergedHighest, chunkStartIndex + i);
                        }
                    }

                    var mergedEntry = new CacheEntry(chunkStartIndex, merged, mergedHighest);

                    // Same array length for both entries, so memory delta is neutral.
                    return mergedEntry;
                });

            sourceOffset += copyLength;
        }

        // Trigger trim asynchronously so producers are not blocked by eviction work.
        if (Interlocked.Read(ref _currentMemoryBytes) > _maxMemoryBytes)
        {
            _ = Task.Run(EvictLruEntries);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// PURPOSE — INCREMENTAL FILL:
    /// The orchestrator calls this method immediately after adding a new range
    /// to the cache to find the next generation window. Example flow:
    ///
    ///   Cache initially holds indices 0..4,999.
    ///   GenerateAndCache(5,000..6,000) populates 5,000..6,000.
    ///   Cache now holds 0..6,000.
    ///   GetHighestCachedIndex() returns 6,000.
    ///   Next request for index 7,000:
    ///     generationStart = 6,000 + 1 = 6,001
    ///     generationEnd = 7,000 + buffer
    ///   No primes are regenerated from 0..6,000.
    /// </para>
    ///
    /// <para>
    /// SPARSE CHUNKS:
    /// If a chunk exists but some of its indices are unknown (zero sentinel),
    /// this method still returns the highest-known-index within that chunk.
    /// Future requests for those unknown indices will trigger generation of just
    /// the missing slice, not the entire chunk.
    /// </para>
    /// </remarks>
    public long GetHighestCachedIndex()
    {
        if (_cache.IsEmpty)
        {
            return -1;
        }

        var max = -1L;
        foreach (var entry in _cache.Values)
        {
            if (entry.HighestKnownIndex > max)
            {
                max = entry.HighestKnownIndex;
            }
        }

        return max;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each call to GetStatistics performs five independent Interlocked.Read calls
    /// on the counters. Because these reads are independent and the counters are
    /// being updated concurrently by other threads, the returned snapshot may be
    /// slightly inconsistent (for example TotalRequests might be 1000 while
    /// CacheHits + CacheMisses is 999). This is the expected and acceptable
    /// behavior for lock-free observability metrics. The snapshot is immutable
    /// once returned.
    /// </remarks>
    public CacheStatistics GetStatistics() => new()
    {
        TotalRequests = Interlocked.Read(ref _totalRequests),
        CacheHits = Interlocked.Read(ref _cacheHits),
        CacheMisses = Interlocked.Read(ref _cacheMisses),
        EntriesCount = _cache.Count,
        MemoryUsageBytes = Interlocked.Read(ref _currentMemoryBytes)
    };

    /// <summary>
    /// Asynchronously evicts least-recently-used cache entries until memory
    /// usage falls below the target threshold.
    ///
    /// <para>
    /// WHY ASYNCHRONOUS EVICTION:
    /// When a producer (the orchestrator or generator) finishes adding a large
    /// generation result and detects that the cache has exceeded its memory
    /// budget, the producer is still holding a reference to the generated array
    /// and may be mid-serialization or mid-return to the caller. Blocking the
    /// producer thread for eviction could cause the entire request path to stall.
    /// Instead, eviction is fire-and-forget: scheduled on the thread pool so the
    /// producer returns to its work immediately.
    /// </para>
    ///
    /// <para>
    /// MONITOR GUARD — SERIALIZING EVICTION:
    /// Multiple concurrent AddPrimeRange calls might all detect the budget is
    /// exceeded and try to start eviction simultaneously. Using Monitor.TryEnter
    /// ensures only one eviction pass runs at a time:
    ///   - The first task to enter acquires _evictionLock and evicts.
    ///   - Subsequent tasks call TryEnter, get false (already held), and return
    ///     immediately without evicting.
    /// This prevents thrashing where multiple eviction passes compete to remove
    /// the same entries.
    /// </para>
    ///
    /// <para>
    /// THE 75% THRESHOLD — WHY NOT 100%:
    /// Eviction brings usage down from (100%+ of budget) to 75% of budget,
    /// creating a 25% headroom. Without this gap, the cache would continuously
    /// oscillate: reach 100%, evict to 100%, immediately exceed again, evict
    /// again. The gap amortizes eviction costs and allows brief periods of
    /// usage slightly above 100% without triggering expensive removal passes.
    ///
    /// Trade-off: you lose ~25% of cache capacity to the headroom, but gain
    /// smooth performance under steady load. For a 100 MB cache, the effective
    /// usable budget is ~75 MB.
    /// </para>
    ///
    /// <para>
    /// LRU SELECTION — REMOVING OLDEST ENTRIES FIRST:
    /// Eviction sorts all cache entries by LastAccessTicks (oldest first) and
    /// removes them one at a time until the target is reached. This preserves
    /// entries that have been accessed recently, which are statistically more
    /// likely to be accessed again soon.
    /// </para>
    /// </summary>
    private void EvictLruEntries()
    {
        // Try to acquire the eviction lock without blocking. If another thread
        // is already evicting, return immediately to avoid cascading eviction
        // passes and contention.
        if (!Monitor.TryEnter(_evictionLock))
        {
            return;
        }

        try
        {
            // Re-read the current memory usage and compute the target threshold.
            // We use a local copy of 'current' to track progress as we remove entries,
            // so we can stop as soon as we reach the target without re-reading the
            // atomic field repeatedly.
            var current = Interlocked.Read(ref _currentMemoryBytes);
            var target = (long)(_maxMemoryBytes * 0.75);

            // If we're already below target (possibly because another thread
            // removed entries while we were waiting for the lock), exit early.
            if (current <= target)
            {
                return;
            }

            // Snapshot the current dictionary entries and sort by age (oldest access
            // time first). We take a snapshot to avoid holding a long-lived reference
            // that would prevent entries from being removed if new entries arrive
            // mid-eviction.
            var candidates = _cache.OrderBy(kvp => kvp.Value.LastAccessTicks).ToList();

            // Remove entries in LRU order until we reach the target.
            foreach (var candidate in candidates)
            {
                if (current <= target)
                {
                    break;
                }

                // Attempt to remove this chunk from the cache. TryRemove is atomic:
                // either the chunk is removed and returned, or it's not present
                // (possibly already removed by another thread).
                if (_cache.TryRemove(candidate.Key, out var removed))
                {
                    // Update our local 'current' to reflect the removal, and
                    // atomically update the global counter.
                    current -= removed.SizeBytes;
                    Interlocked.Add(ref _currentMemoryBytes, -removed.SizeBytes);
                }
            }
        }
        finally
        {
            // Always release the lock so other waiting threads can acquire it.
            Monitor.Exit(_evictionLock);
        }
    }
}