namespace Sieve.Core.Abstractions;

/// <summary>
/// Repository pattern interface for caching computed primes.
/// Thread-safe: Yes - implementations must handle concurrent access safely.
/// Design: LRU (Least Recently Used) eviction policy to manage memory limits.
/// </summary>
public interface IPrimeCache
{
    /// <summary>
    /// Attempts to retrieve primes from startIndex to endIndex (inclusive).
    /// </summary>
    /// <param name="startIndex">Starting index (0-based)</param>
    /// <param name="endIndex">Ending index (0-based, inclusive)</param>
    /// <param name="primes">Output array of primes if successful</param>
    /// <returns>True if all primes in range are cached, false otherwise</returns>
    bool TryGetPrimeRange(long startIndex, long endIndex, out long[] primes);
    
    /// <summary>
    /// Stores a contiguous range of primes starting at startIndex.
    /// If memory limit is exceeded, least recently used entries will be evicted.
    /// </summary>
    /// <param name="startIndex">Starting index for the prime range</param>
    /// <param name="primes">Primes to cache (must be contiguous)</param>
    void AddPrimeRange(long startIndex, ReadOnlySpan<long> primes);
    
    /// <summary>
    /// Gets the highest cached prime index.
    /// </summary>
    /// <returns>Highest index, or -1 if cache is empty</returns>
    long GetHighestCachedIndex();
    
    /// <summary>
    /// Retrieves cache performance statistics.
    /// </summary>
    /// <returns>Statistics snapshot including hits, misses, and memory usage</returns>
    Models.CacheStatistics GetStatistics();
}
