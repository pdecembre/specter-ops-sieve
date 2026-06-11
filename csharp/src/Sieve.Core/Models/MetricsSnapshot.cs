namespace Sieve.Core.Models;

/// <summary>
/// Immutable snapshot of system-wide performance metrics.
/// Thread-safe: Yes (immutable record type).
/// Design: Immutable Object pattern for safe metric reporting.
/// </summary>
public sealed record MetricsSnapshot
{
    /// <summary>
    /// Gets the total number of NthPrime requests received.
    /// </summary>
    public long TotalRequests { get; init; }
    
    /// <summary>
    /// Gets the number of times primes were served from cache.
    /// </summary>
    public long CacheHits { get; init; }
    
    /// <summary>
    /// Gets the number of times prime generation was required.
    /// </summary>
    public long CacheMisses { get; init; }
    
    /// <summary>
    /// Gets the number of times a generator was invoked.
    /// </summary>
    public long GenerationCalls { get; init; }
    
    /// <summary>
    /// Gets the total number of primes generated across all operations.
    /// </summary>
    public long TotalPrimesGenerated { get; init; }
    
    /// <summary>
    /// Gets the cache hit ratio (0.0 to 1.0).
    /// Returns 0 if no requests have been made.
    /// </summary>
    public double CacheHitRatio => TotalRequests > 0 
        ? (double)CacheHits / TotalRequests 
        : 0.0;
    
    /// <summary>
    /// Gets the average number of primes generated per generation call.
    /// Returns 0 if no generation calls have been made.
    /// </summary>
    public double AveragePrimesPerGeneration => GenerationCalls > 0 
        ? (double)TotalPrimesGenerated / GenerationCalls 
        : 0.0;
}
