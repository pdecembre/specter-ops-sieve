using System.Threading;
using Sieve.Core.Abstractions;
using Sieve.Core.Models;

namespace Sieve.Implementation.Metrics;

/// <summary>
/// Thread-safe metrics collector based on Interlocked atomic operations.
/// </summary>
public sealed class AtomicMetricsCollector : IMetricsCollector
{
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _generationCalls;
    private long _totalPrimesGenerated;

    /// <inheritdoc />
    public void RecordRequest() => Interlocked.Increment(ref _totalRequests);

    /// <inheritdoc />
    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);

    /// <inheritdoc />
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    /// <inheritdoc />
    public void RecordGeneration(long primesGenerated)
    {
        Interlocked.Increment(ref _generationCalls);
        Interlocked.Add(ref _totalPrimesGenerated, primesGenerated);
    }

    /// <inheritdoc />
    public MetricsSnapshot GetSnapshot() => new()
    {
        TotalRequests = Interlocked.Read(ref _totalRequests),
        CacheHits = Interlocked.Read(ref _cacheHits),
        CacheMisses = Interlocked.Read(ref _cacheMisses),
        GenerationCalls = Interlocked.Read(ref _generationCalls),
        TotalPrimesGenerated = Interlocked.Read(ref _totalPrimesGenerated)
    };
}