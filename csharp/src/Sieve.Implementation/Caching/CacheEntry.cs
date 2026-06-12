using System.Threading;

namespace Sieve.Implementation.Caching;

/// <summary>
/// Represents one cache chunk keyed by chunk index.
///
/// Storage model:
/// - <see cref="Primes"/> is indexed by offset inside the chunk (0..chunkSize-1).
/// - Value > 1 means "known prime value present".
/// - Value == 0 means "unknown/not populated in this chunk slot yet".
///
/// Thread-safety:
/// - Entry instances are immutable except <see cref="LastAccessTicks"/>.
/// - Access timestamp is updated with atomic operations only.
/// 
/// Why chunked entries instead of one value per dictionary key:
/// 1) Prime lookups are frequently adjacent in index space, so chunking improves
///    spatial locality and reduces dictionary overhead.
/// 2) Chunk-level replacement supports efficient LRU-style eviction.
/// 3) Chunk arrays allow sparse merge updates when new ranges are computed.
/// </summary>
internal sealed class CacheEntry
{
    private long _lastAccessTicks;

    public CacheEntry(long startIndex, long[] primes, long highestKnownIndex)
    {
        StartIndex = startIndex;
        Primes = primes;
        HighestKnownIndex = highestKnownIndex;
        _lastAccessTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Start index (inclusive) represented by offset 0 in <see cref="Primes"/>.
    /// </summary>
    public long StartIndex { get; }

    /// <summary>
    /// Chunk slots (prime values or zero for "unknown slot").
    /// </summary>
    public long[] Primes { get; }

    /// <summary>
    /// Highest index in this chunk currently known to be populated with a prime value.
    /// </summary>
    public long HighestKnownIndex { get; }

    /// <summary>
    /// Last-access timestamp used for LRU ranking.
    /// </summary>
    public long LastAccessTicks => Interlocked.Read(ref _lastAccessTicks);

    /// <summary>
    /// Approximate entry size in bytes used for memory budgeting.
    ///
    /// Includes:
    /// - prime array payload,
    /// - coarse object/header overhead estimate.
    ///
    /// This is intentionally approximate; the cache manager uses it for budget
    /// heuristics, not exact GC accounting.
    /// </summary>
    public long SizeBytes => (Primes.LongLength * sizeof(long)) + 64;

    /// <summary>
    /// Marks this entry as recently used.
    /// </summary>
    public void Touch()
    {
        Interlocked.Exchange(ref _lastAccessTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Attempts to read the prime value for an absolute index.
    /// 
    /// Returns false when:
    /// 1) the absolute index does not map into this chunk, or
    /// 2) the slot exists but is still unknown (sentinel 0).
    /// </summary>
    public bool TryReadPrimeAt(long absoluteIndex, out long prime)
    {
        var relative = absoluteIndex - StartIndex;
        if (relative < 0 || relative >= Primes.LongLength)
        {
            prime = 0;
            return false;
        }

        prime = Primes[relative];
        return prime > 1;
    }
}