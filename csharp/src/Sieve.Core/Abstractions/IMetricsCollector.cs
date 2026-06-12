namespace Sieve.Core.Abstractions;

/// <summary>
/// Accepts event notifications from the computation pipeline and accumulates
/// them into observable performance counters.
///
/// <para>
/// THE OBSERVER PATTERN — WHY THIS INTERFACE EXISTS:
/// The core computation components (orchestrator, cache, generators) should not
/// contain dashboarding or logging logic. Mixing those concerns would make the
/// components harder to test, harder to read, and harder to evolve independently.
///
/// Instead, each component fires discrete, named events ("a request arrived",
/// "a cache hit occurred", "N primes were generated") by calling the corresponding
/// method on this interface. The metrics collector is the sole observer of those
/// events and decides how to record them — whether as in-process atomic counters,
/// as writes to a time-series database, or as no-ops in a test double.
///
/// This means you can swap out the entire metrics backend without touching any
/// computation code, and you can unit-test computation logic with a mock or
/// no-op collector.
/// </para>
///
/// <para>
/// CALL SEQUENCE IN THE ORCHESTRATOR PIPELINE:
/// Each call to ISieve.NthPrime produces the following sequence of metric events:
///
///   1. RecordRequest()       — always, at the start of every call.
///   2a. RecordCacheHit()     — when the prime was found in the cache.
///      OR
///   2b. RecordCacheMiss()    — when the prime was NOT in the cache.
///   3. RecordGeneration(N)   — only after a miss, once generation completes,
///                              where N is the count of primes that were computed.
///
/// A well-tuned cache produces mostly cache hits: step 3 will not fire for most
/// calls, and TotalPrimesGenerated will grow much more slowly than TotalRequests.
/// </para>
///
/// <para>
/// WHY SEPARATE METHODS INSTEAD OF ONE RecordEvent(EventType, ...) METHOD:
/// Strongly-typed, named methods provide better discoverability, compile-time
/// checking, and performance. Each recording is a single atomic counter increment
/// with no string allocation, switch dispatch, or boxing. They also make it
/// obvious at each call site exactly which counter is being updated.
/// </para>
///
/// <para>
/// THREAD-SAFETY CONTRACT:
/// All methods MUST be safe to call concurrently from multiple threads without
/// external locking. The implementation is expected to use lock-free atomic
/// operations (for example Interlocked) so that recording a metric never blocks
/// the computation thread.
/// </para>
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Signals that one call to ISieve.NthPrime or NthPrimeAsync was received.
    /// This is always the first event fired in the pipeline for any request.
    /// </summary>
    void RecordRequest();

    /// <summary>
    /// Signals that the requested prime was found in the cache and no generation
    /// was required.
    ///
    /// <para>
    /// This is a positive indicator: every cache hit is a generation operation
    /// that was avoided. A high ratio of cache hits to total requests means the
    /// cache is sized well for the workload.
    /// </para>
    /// </summary>
    void RecordCacheHit();

    /// <summary>
    /// Signals that the requested prime was NOT found in the cache and generation
    /// will be (or has just been) triggered.
    ///
    /// <para>
    /// A sustained high miss rate may indicate the cache is too small for the
    /// access pattern, or that requests are spread too broadly across the index
    /// space to benefit from caching.
    /// </para>
    /// </summary>
    void RecordCacheMiss();

    /// <summary>
    /// Signals that a generation operation completed and produced
    /// <paramref name="primesGenerated"/> new prime values.
    ///
    /// <para>
    /// This event fires after a cache miss, once the generator has returned
    /// its result array. <paramref name="primesGenerated"/> is the count of
    /// primes in that array, not the numeric value of those primes. Over time
    /// TotalPrimesGenerated divided by GenerationCalls gives the average batch
    /// size, which reflects how aggressively the orchestrator prefetches ahead
    /// of the requested index.
    /// </para>
    /// </summary>
    /// <param name="primesGenerated">
    /// The number of prime values produced by this single generation call.
    /// </param>
    void RecordGeneration(long primesGenerated);

    /// <summary>
    /// Returns an immutable point-in-time copy of all accumulated counters.
    ///
    /// <para>
    /// SNAPSHOT SEMANTICS:
    /// Because counters are updated concurrently from multiple threads, the
    /// five values in the returned <see cref="Models.MetricsSnapshot"/> are read
    /// independently and may not all reflect the exact same instant in time.
    /// They should be treated as observability-grade approximations, not as
    /// transactionally exact accounting figures.
    ///
    /// The snapshot is immutable once returned: it will not change even as new
    /// events are recorded after this call returns. This makes it safe to pass
    /// across threads or serialize without additional locking.
    /// </para>
    /// </summary>
    /// <returns>
    /// An immutable <see cref="Models.MetricsSnapshot"/> reflecting current
    /// counter values.
    /// </returns>
    Models.MetricsSnapshot GetSnapshot();
}
