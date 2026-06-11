using System.Collections.Concurrent;
using System.Threading;
using Sieve.Core.Abstractions;
using Sieve.Core.Models;

namespace Sieve.Implementation.Caching;

/// <summary>
/// Thread-safe LRU cache for prime ranges.
///
/// Design goals:
/// 1) Correctness under concurrent reads/writes.
/// 2) Fast single-index reads (dominant path for NthPrime lookups).
/// 3) Bounded memory with best-effort LRU eviction.
///
/// Chunking model:
/// - Prime indices are grouped by <c>chunkKey = index / chunkSize</c>.
/// - Each chunk stores slot values for offsets 0..chunkSize-1.
/// - Unknown slots are represented with zero sentinel values.
/// </summary>
public sealed class ConcurrentLruPrimeCache : IPrimeCache
{
    private readonly ConcurrentDictionary<long, CacheEntry> _cache = new();
    private readonly long _maxMemoryBytes;
    private readonly int _chunkSize;
    private readonly object _evictionLock = new();

    // Atomic metrics.
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _currentMemoryBytes;

    /// <summary>
    /// Creates a new cache instance.
    /// </summary>
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
    public bool TryGetPrimeRange(long startIndex, long endIndex, out long[] primes)
    {
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

        primes = result;
        Interlocked.Increment(ref _cacheHits);
        return true;
    }

    /// <inheritdoc />
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
                    Interlocked.Add(ref _currentMemoryBytes, newEntry.SizeBytes);
                    return newEntry;
                },
                (_, existing) =>
                {
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

        if (Interlocked.Read(ref _currentMemoryBytes) > _maxMemoryBytes)
        {
            _ = Task.Run(EvictLruEntries);
        }
    }

    /// <inheritdoc />
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
    public CacheStatistics GetStatistics() => new()
    {
        TotalRequests = Interlocked.Read(ref _totalRequests),
        CacheHits = Interlocked.Read(ref _cacheHits),
        CacheMisses = Interlocked.Read(ref _cacheMisses),
        EntriesCount = _cache.Count,
        MemoryUsageBytes = Interlocked.Read(ref _currentMemoryBytes)
    };

    /// <summary>
    /// Evicts oldest-accessed entries until cache falls below 75% of max budget.
    /// </summary>
    private void EvictLruEntries()
    {
        if (!Monitor.TryEnter(_evictionLock))
        {
            return;
        }

        try
        {
            var current = Interlocked.Read(ref _currentMemoryBytes);
            var target = (long)(_maxMemoryBytes * 0.75);
            if (current <= target)
            {
                return;
            }

            // Snapshot, order by age (oldest first), then remove progressively.
            var candidates = _cache.OrderBy(kvp => kvp.Value.LastAccessTicks).ToList();
            foreach (var candidate in candidates)
            {
                if (current <= target)
                {
                    break;
                }

                if (_cache.TryRemove(candidate.Key, out var removed))
                {
                    current -= removed.SizeBytes;
                    Interlocked.Add(ref _currentMemoryBytes, -removed.SizeBytes);
                }
            }
        }
        finally
        {
            Monitor.Exit(_evictionLock);
        }
    }
}