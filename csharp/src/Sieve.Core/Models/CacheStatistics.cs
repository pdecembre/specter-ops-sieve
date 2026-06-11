namespace Sieve.Core.Models;

/// <summary>
/// Immutable snapshot of cache performance metrics.
/// Thread-safe: Yes (immutable record type).
/// Design: Immutable Object pattern for safe sharing across threads.
/// </summary>
public sealed record CacheStatistics
{
    /// <summary>
    /// Gets the total number of cache requests (hits + misses).
    /// </summary>
    public long TotalRequests { get; init; }
    
    /// <summary>
    /// Gets the number of cache hits (primes found in cache).
    /// </summary>
    public long CacheHits { get; init; }
    
    /// <summary>
    /// Gets the number of cache misses (primes not found, generation required).
    /// </summary>
    public long CacheMisses { get; init; }
    
    /// <summary>
    /// Gets the cache hit ratio (0.0 to 1.0).
    /// Returns 0 if no requests have been made.
    /// </summary>
    public double HitRatio => TotalRequests > 0 
        ? (double)CacheHits / TotalRequests 
        : 0.0;
    
    /// <summary>
    /// Gets the number of cache entries currently stored.
    /// </summary>
    public long EntriesCount { get; init; }
    
    /// <summary>
    /// Gets the current memory usage of the cache in bytes.
    /// </summary>
    public long MemoryUsageBytes { get; init; }
}
