namespace Sieve.Core.Models;

/// <summary>
/// Immutable snapshot of high-level operational counters captured at a single
/// point in time.
///
/// <para>
/// Usage intent:
/// 1) Surface low-overhead observability data to logs, health probes, and
///    dashboards without exposing mutable internal state.
/// 2) Provide lightweight derived indicators (cache hit ratio, average generation
///    batch size) that callers can log or display without further computation.
/// 3) Guarantee that derived values are consistent with the primitive counters
///    they are computed from, because all values are captured together.
/// </para>
///
/// <para>
/// Consistency note:
/// Under concurrent load the five primitive counters are read independently in
/// rapid succession by the metrics collector, not in a single atomic operation.
/// A concurrent modification between reads can cause derived ratios to be
/// slightly off (for example TotalRequests may be one ahead of CacheHits +
/// CacheMisses). This is the standard and accepted trade-off for lock-free
/// metrics; treat these values as observability-grade signals, not exact
/// accounting figures.
/// </para>
/// </summary>
public sealed record MetricsSnapshot
{
    /// <summary>
    /// Total number of calls to ISieve.NthPrime or NthPrimeAsync received since
    /// the metrics collector was created.
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// Number of requests that were served entirely from the cache, without
    /// invoking any prime generator.
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    /// Number of requests that could not be served from cache and triggered
    /// a prime generation operation.
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    /// Number of times a prime generator was invoked. Should match or be close
    /// to <see cref="CacheMisses"/> in a well-behaved system.
    /// </summary>
    public long GenerationCalls { get; init; }

    /// <summary>
    /// The cumulative count of prime values that have been produced across all
    /// generation calls. Divide by <see cref="GenerationCalls"/> to get the
    /// average batch size (see <see cref="AveragePrimesPerGeneration"/>).
    /// </summary>
    public long TotalPrimesGenerated { get; init; }

    /// <summary>
    /// The fraction of requests served from cache, ranging from 0.0 (all misses)
    /// to 1.0 (all hits).
    ///
    /// <para>
    /// Calculation: CacheHits / TotalRequests.
    /// Returns 0.0 when TotalRequests is zero to avoid a divide-by-zero.
    /// </para>
    ///
    /// <para>
    /// A ratio above 0.9 generally indicates the cache is well-sized for the
    /// workload. A ratio below 0.5 suggests the access pattern is too scattered
    /// to benefit from caching, or the cache budget is too small.
    /// </para>
    /// </summary>
    public double CacheHitRatio => TotalRequests > 0
        ? (double)CacheHits / TotalRequests
        : 0.0;

    /// <summary>
    /// The average number of prime values produced per generation call.
    ///
    /// <para>
    /// Calculation: TotalPrimesGenerated / GenerationCalls.
    /// Returns 0.0 when GenerationCalls is zero.
    /// </para>
    ///
    /// <para>
    /// A large average batch size means the orchestrator's prefetch buffer is
    /// working well: each generation call serves many future requests from cache.
    /// A small average (close to 1) suggests the prefetch buffer is too small
    /// or each request hits a previously uncached index.
    /// </para>
    /// </summary>
    public double AveragePrimesPerGeneration => GenerationCalls > 0
        ? (double)TotalPrimesGenerated / GenerationCalls
        : 0.0;
}
