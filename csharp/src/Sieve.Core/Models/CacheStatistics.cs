namespace Sieve.Core.Models;

/// <summary>
/// Immutable snapshot describing the cache's behavior at a single observation point.
///
/// <para>
/// Why immutable:
/// 1) A producer (the cache implementation) can hand this object to any consumer
///    without needing to copy it, lock it, or worry about the consumer modifying it.
/// 2) Derived values such as <see cref="HitRatio"/> are computed from the snapshot's
///    own primitive fields and therefore remain perfectly consistent with each other
///    for the lifetime of the snapshot, even if the live cache counters continue
///    to change.
/// 3) The C# <c>record</c> type gives structural equality and <c>with</c>-expression
///    support for free, making it easy to produce modified copies in tests.
/// </para>
///
/// <para>
/// Interpretation guidance:
/// - <see cref="HitRatio"/> near 1.0 means the cache is serving nearly all requests
///   without triggering generation. This is the ideal steady-state.
/// - <see cref="HitRatio"/> near 0.0 indicates the access pattern is too broad
///   for the current cache size, or the cache was recently cold-started.
/// - <see cref="MemoryUsageBytes"/> approaching the configured ceiling means
///   eviction is occurring frequently; consider increasing MaxCacheMemoryBytes.
/// </para>
/// </summary>
public sealed record CacheStatistics
{
    /// <summary>
    /// The total number of lookup attempts made against the cache since it was
    /// created (or since counters were last reset). Includes both hits and misses.
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// The number of lookups that found all requested primes already stored in
    /// the cache. Each hit avoided a generation call.
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    /// The number of lookups that did not find all requested primes in the cache,
    /// requiring the orchestrator to invoke a generator.
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    /// The ratio of cache hits to total lookups, ranging from 0.0 (every request
    /// missed) to 1.0 (every request hit).
    ///
    /// <para>
    /// Calculation: CacheHits / TotalRequests.
    /// Returns 0.0 when TotalRequests is zero to avoid a divide-by-zero.
    /// </para>
    ///
    /// <para>
    /// This is intentionally a computed property rather than a stored field so it
    /// always derives from the snapshot's own CacheHits and TotalRequests values
    /// and can never be out of sync with them.
    /// </para>
    /// </summary>
    public double HitRatio => TotalRequests > 0
        ? (double)CacheHits / TotalRequests
        : 0.0;

    /// <summary>
    /// The number of cache entries (chunks) currently held in memory.
    /// Each chunk stores a fixed number of prime values as configured by
    /// CacheChunkSize in <c>SieveConfiguration</c>.
    /// </summary>
    public long EntriesCount { get; init; }

    /// <summary>
    /// An estimate of how many bytes the cache is currently consuming.
    /// Based on the sum of each chunk's payload size plus overhead.
    /// Compare against the configured MaxCacheMemoryBytes ceiling to
    /// determine how close the cache is to triggering eviction.
    /// </summary>
    public long MemoryUsageBytes { get; init; }
}
