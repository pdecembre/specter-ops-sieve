namespace Sieve.Core.Abstractions;

/// <summary>
/// Interface for collecting runtime metrics and performance statistics.
/// Thread-safe: Yes - implementations must support concurrent metric recording.
/// Design: Observer pattern for monitoring system behavior.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Records a prime number request.
    /// </summary>
    void RecordRequest();
    
    /// <summary>
    /// Records a cache hit (prime found in cache).
    /// </summary>
    void RecordCacheHit();
    
    /// <summary>
    /// Records a cache miss (prime not found, generation required).
    /// </summary>
    void RecordCacheMiss();
    
    /// <summary>
    /// Records a prime generation operation.
    /// </summary>
    /// <param name="primesGenerated">Number of primes generated in this operation</param>
    void RecordGeneration(long primesGenerated);
    
    /// <summary>
    /// Gets a snapshot of current metrics.
    /// </summary>
    /// <returns>Immutable metrics snapshot</returns>
    Models.MetricsSnapshot GetSnapshot();
}
