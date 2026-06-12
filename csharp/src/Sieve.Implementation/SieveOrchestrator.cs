using Microsoft.Extensions.Logging;
using Sieve.Core.Abstractions;
using Sieve.Core.Exceptions;
using Sieve.Implementation.Generation;

namespace Sieve.Implementation;

/// <summary>
/// The central coordination point that wires cache, generation, estimation,
/// metrics, and logging together to fulfill the <see cref="ISieve"/> contract.
///
/// <para>
/// THE FACADE PATTERN — WHY THIS CLASS EXISTS:
/// Each subsystem (cache, generator, estimator, metrics) is independently designed
/// and testable. Without an orchestrator, every caller would have to know about all
/// of them and reproduce the same coordination logic: check cache, miss, estimate
/// upper bound, pick a generator, generate, cache the result, return the prime.
/// That logic would be duplicated and inconsistently implemented across call sites.
///
/// SieveOrchestrator is the single place that owns that coordination. Callers see
/// only the one-method <see cref="ISieve"/> surface and are completely unaware of
/// the internal pipeline.
/// </para>
///
/// <para>
/// EXECUTION PIPELINE — STEP BY STEP:
/// Every call to <see cref="NthPrimeAsync"/> follows this exact sequence:
///
///   Step 1 — Validate input.
///     Reject negative indices immediately with <see cref="ArgumentOutOfRangeException"/>.
///     This is a deterministic, caller-fixable error and should never be swallowed.
///
///   Step 2 — Record the request in metrics.
///     Metrics are updated before any cache or generation logic so the total
///     request count always reflects every call, even ones that fail validation.
///     (Validation failure throws before this line, so truly invalid calls are
///     intentionally excluded from the request counter.)
///
///   Step 3 — Attempt a cache lookup.
///     Call <c>IPrimeCache.TryGetPrimeRange(n, n, ...)</c> for the single
///     requested index. If the prime is already cached, record a cache hit and
///     return immediately. This is the happy path for warm caches and completes
///     in sub-millisecond time.
///
///   Step 4 — Cache miss: determine the generation window.
///     Ask the cache for the highest index it currently holds. The new generation
///     starts at (highestCachedIndex + 1) to avoid recomputing primes that are
///     already stored. The end of the window is (n + buffer), where the buffer is
///     a look-ahead chosen by <see cref="CalculateBufferSize"/> so that nearby
///     future requests are also served from cache.
///
///   Step 5 — Select the appropriate generator strategy.
///     For small indices the classic sieve is used (fast, single allocation).
///     For large indices the segmented sieve is used (bounded memory, slightly
///     more overhead). The threshold is controlled by
///     <see cref="SegmentedThreshold"/>.
///
///   Step 6 — Generate primes and populate the cache.
///     Invoke the generator asynchronously with cancellation support. Store the
///     full generated range in the cache so subsequent requests in that window
///     are served instantly.
///
///   Step 7 — Extract the requested index from the result and return it.
///     Translate the absolute index n into an offset within the generated array.
///     If the offset is out of bounds, an internal contract was violated and a
///     <see cref="PrimeComputationException"/> is thrown.
/// </para>
///
/// <para>
/// THREAD-SAFETY:
/// This class holds no mutable state of its own — all counters, cached values,
/// and configuration live in the injected dependencies. Because every dependency
/// is thread-safe (ConcurrentLruPrimeCache, AtomicMetricsCollector, etc.), the
/// orchestrator is safe to call concurrently from any number of threads without
/// additional locking.
/// </para>
///
/// <para>
/// ERROR-HANDLING POLICY:
/// - <see cref="ArgumentOutOfRangeException"/> for negative n: propagates unchanged.
///   This is a caller error; do not wrap it.
/// - <see cref="OperationCanceledException"/>: re-thrown unchanged after logging a
///   warning. Callers that supply a <see cref="CancellationToken"/> expect to
///   receive this exception when they cancel; wrapping it would break their
///   cancellation handling.
/// - Any other unexpected exception (for example <see cref="OutOfMemoryException"/>
///   thrown inside a generator): caught, logged, and re-thrown as
///   <see cref="PrimeComputationException"/> so callers always see a single,
///   predictable domain exception type for computation failures, regardless of
///   which internal component failed.
/// </para>
/// </summary>
public sealed class SieveOrchestrator : ISieve
{
    /// <summary>
    /// The prime index above which the segmented sieve is preferred over the
    /// classic sieve. For indices at or above this value, the classic sieve's
    /// O(N) boolean bitmap would require hundreds of megabytes, so the segmented
    /// sieve's O(√N + segmentSize) memory profile is used instead.
    /// </summary>
    private const long SegmentedThreshold = 1_000_000;

    // Injected dependencies — all readonly to prevent accidental reassignment.
    // All concrete types are thread-safe; see their own class documentation.
    private readonly IPrimeCache _cache;
    private readonly ClassicSieveGenerator _classicGenerator;
    private readonly SegmentedSieveGenerator _segmentedGenerator;
    private readonly IEstimator _estimator;
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<SieveOrchestrator> _logger;

    /// <summary>
    /// Constructs a fully wired orchestrator.
    ///
    /// <para>
    /// Every parameter is required. Passing null for any of them would leave
    /// the orchestrator in a broken state that manifests as a NullReferenceException
    /// at an unpredictable later point. The null-guards here make the contract
    /// explicit and surface the misconfiguration immediately at construction time,
    /// which is much easier to diagnose than a late null dereference.
    /// </para>
    ///
    /// <para>
    /// In production, this constructor is called by the DI container after
    /// <c>AddSieveServices</c> registers all dependencies as singletons. In tests,
    /// it is called directly, often supplying mock or stub implementations.
    /// </para>
    /// </summary>
    /// <param name="cache">The prime cache. Must be thread-safe.</param>
    /// <param name="classicGenerator">
    /// Generator used for small index ranges where a single contiguous bitmap
    /// is affordable.
    /// </param>
    /// <param name="segmentedGenerator">
    /// Generator used for large index ranges where the classic bitmap would
    /// consume too much memory.
    /// </param>
    /// <param name="estimator">
    /// Produces numeric upper bounds so generators know how far to sieve.
    /// </param>
    /// <param name="metricsCollector">
    /// Receives telemetry events from this pipeline. Must be thread-safe.
    /// </param>
    /// <param name="logger">
    /// Structured logger for debug, info, warning, and error events.
    /// </param>
    public SieveOrchestrator(
        IPrimeCache cache,
        ClassicSieveGenerator classicGenerator,
        SegmentedSieveGenerator segmentedGenerator,
        IEstimator estimator,
        IMetricsCollector metricsCollector,
        ILogger<SieveOrchestrator> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _classicGenerator = classicGenerator ?? throw new ArgumentNullException(nameof(classicGenerator));
        _segmentedGenerator = segmentedGenerator ?? throw new ArgumentNullException(nameof(segmentedGenerator));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// SYNC-OVER-ASYNC — WHY THIS IS SAFE HERE:
    /// This method calls the async pipeline synchronously via GetAwaiter().GetResult().
    /// In general, sync-over-async can deadlock in environments with a
    /// single-threaded synchronization context (for example classic ASP.NET or
    /// WinForms). In this application the async path uses Task.Yield() and
    /// awaits purely computational work — there is no I/O, no ConfigureAwait(false)
    /// omission on ambient context captures, and the host is expected to be either
    /// a console application or ASP.NET Core (which has no synchronization context
    /// on thread-pool threads). Under these conditions the sync wrapper is safe.
    /// </para>
    ///
    /// <para>
    /// The synchronous overload exists solely to preserve the original API shape
    /// (the exercise requirement specifies a simple <c>long NthPrime(long n)</c>
    /// signature). All real computation lives in <see cref="NthPrimeAsync"/>.
    /// </para>
    /// </remarks>
    public long NthPrime(long n)
    {
        return NthPrimeAsync(n, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default)
    {
        // Step 1 — Input validation.
        // The prime sequence is defined only for non-negative indices. Negative
        // values have no mathematical meaning and indicate a caller bug.
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Index must be non-negative.");
        }

        // Step 2 — Record the incoming request.
        // This fires before any cache or generation logic so TotalRequests always
        // counts every valid call, regardless of cache hit/miss outcome.
        _metricsCollector.RecordRequest();

        try
        {
            // Step 3 — Cache lookup (happy path).
            // Ask the cache for the single prime at index n. If it is present,
            // record the hit and return immediately — no generation needed.
            // TryGetPrimeRange(n, n, ...) requests a range of exactly one element;
            // this keeps the call site consistent with range-based lookups.
            if (_cache.TryGetPrimeRange(n, n, out var cachedPrime))
            {
                _metricsCollector.RecordCacheHit();
                _logger.LogDebug("Cache hit for index {Index}: {Prime}", n, cachedPrime[0]);
                return cachedPrime[0];
            }

            // Step 4 — Cache miss: compute the incremental generation window.
            // Record the miss before any further computation so the metric is
            // always updated even if generation subsequently throws.
            _metricsCollector.RecordCacheMiss();

            // Find the highest prime index the cache already holds.
            // Generation starts at the very next index to avoid recomputing
            // primes that are already cached.
            // Example: cache holds 0..4999. highestCachedIndex = 4999.
            //          generationStart = 5000. We only compute new primes.
            var highestCachedIndex = _cache.GetHighestCachedIndex();
            var generationStart = Math.Max(0, highestCachedIndex + 1);

            // Extend the generation window beyond n by a look-ahead buffer.
            // This prefetches nearby primes so adjacent future requests are
            // served from cache rather than triggering another generation call.
            // 'checked' ensures an OverflowException is thrown rather than
            // silently wrapping to a negative value for extremely large n.
            var generationEnd = checked(n + CalculateBufferSize(n));

            // Log the estimated numeric upper bound for observability.
            // The estimator's output is passed to the generator internally;
            // we call it here a second time only to surface the value in logs.
            var estimatedUpperBound = _estimator.EstimateUpperBound(generationEnd);

            _logger.LogInformation(
                "Generating prime indices [{Start}..{End}] for requested index {RequestedIndex}. Estimated value upper bound={UpperBound}.",
                generationStart,
                generationEnd,
                n,
                estimatedUpperBound);

            // Step 5 — Select generation algorithm.
            // Small indices: classic sieve (fast single-pass, larger memory).
            // Large indices: segmented sieve (bounded memory, slightly slower).
            var generator = SelectGenerator(n);

            // Step 6 — Generate and cache.
            // GeneratePrimesAsync returns exactly (generationEnd - generationStart + 1)
            // primes in ascending order. The cancellation token is forwarded so the
            // caller can abort a long-running generation pass.
            var generated = await generator.GeneratePrimesAsync(generationStart, generationEnd, cancellationToken);

            // Record how many primes this call produced before caching, so the
            // metric is accurate even if AddPrimeRange throws.
            _metricsCollector.RecordGeneration(generated.LongLength);
            _cache.AddPrimeRange(generationStart, generated);

            // Step 7 — Extract the requested prime from the result.
            // The generated array covers [generationStart..generationEnd].
            // The requested index n maps to position (n - generationStart) within it.
            var offset = n - generationStart;
            if (offset < 0 || offset >= generated.LongLength)
            {
                // This branch indicates an internal contract violation: the generator
                // promised to cover [generationStart..generationEnd] but the resulting
                // array does not contain the position for index n. This should never
                // happen under correct operation; if it does, surface it as a
                // computation exception rather than an IndexOutOfRangeException so
                // the caller gets a meaningful domain error.
                throw new PrimeComputationException(n, "Generated prime range did not contain the requested index.");
            }

            return generated[offset];
        }
        catch (OperationCanceledException)
        {
            // Log the cancellation at Warning level (it is not an error — the caller
            // intentionally withdrew its request) then re-throw unchanged so the
            // caller's cancellation handling works as expected.
            _logger.LogWarning("Prime computation was cancelled for index {Index}.", n);
            throw;
        }
        catch (Exception ex) when (ex is not PrimeComputationException)
        {
            // Wrap any unexpected exception in PrimeComputationException so callers
            // receive a consistent domain exception type regardless of which internal
            // component failed. The original exception is preserved as InnerException
            // so the root cause remains visible in logs and debuggers.
            // The 'when' filter excludes PrimeComputationException itself to avoid
            // double-wrapping when the inner contract-violation check above fires.
            _logger.LogError(ex, "Prime computation failed for index {Index}.", n);
            throw new PrimeComputationException(n, "Prime computation failed.", ex);
        }
    }

    /// <summary>
    /// Returns the generator that is most appropriate for the requested prime index.
    ///
    /// <para>
    /// SELECTION LOGIC:
    /// For indices below <see cref="SegmentedThreshold"/> (1,000,000) the classic
    /// sieve is used. Its single contiguous boolean array is CPU-cache friendly and
    /// very fast for small-to-medium ranges. The estimated array size for index
    /// ~1,000,000 is around 15 MB — still manageable.
    ///
    /// For indices at or above the threshold the segmented sieve is used. At index
    /// 10,000,000 the classic sieve's bitmap would need ~179 MB; the segmented sieve
    /// keeps peak allocation to O(√N + segmentSize), typically a few megabytes.
    /// </para>
    /// </summary>
    /// <param name="index">The 0-based prime index being requested.</param>
    /// <returns>
    /// <see cref="ClassicSieveGenerator"/> for small indices,
    /// <see cref="SegmentedSieveGenerator"/> for large indices.
    /// </returns>
    private IPrimeGenerator SelectGenerator(long index)
    {
        return index < SegmentedThreshold
            ? _classicGenerator
            : _segmentedGenerator;
    }

    /// <summary>
    /// Returns the number of extra prime indices to generate beyond the requested
    /// index <paramref name="n"/>, so that nearby future requests are already cached.
    ///
    /// <para>
    /// WHY PREFETCH AT ALL:
    /// If the orchestrator generated exactly one prime per request, every cache miss
    /// would trigger a new sieve pass — even for requests for index n+1 arriving
    /// immediately after index n. Because sieve algorithms work by marking a range
    /// of integers, generating a slightly larger batch than requested costs very
    /// little additional work but eliminates many subsequent generation calls.
    /// </para>
    ///
    /// <para>
    /// ADAPTIVE TIERS — RATIONALE FOR EACH THRESHOLD:
    ///
    ///   n &lt; 1,000 → fixed buffer of 1,000:
    ///     For very small indices the prime values are tiny numbers. Generating
    ///     the first 2,000 primes is nearly instantaneous and costs well under 1 MB.
    ///     Using a fixed warm-up window avoids repeated tiny sieve passes during
    ///     sequential or exploratory usage patterns.
    ///
    ///   1,000 ≤ n &lt; 100,000 → proportional buffer of n / 10:
    ///     In the medium range, proportional look-ahead scales the buffer with the
    ///     request size. At n = 10,000 the buffer is 1,000; at n = 99,999 it is
    ///     ~10,000. This keeps generation time and memory proportional to what the
    ///     caller has already demonstrated they need.
    ///
    ///   n ≥ 100,000 → capped buffer of 100,000:
    ///     For very large requests the proportional heuristic would produce
    ///     excessively large buffers (at n = 10,000,000 it would be 1,000,000 extra
    ///     primes). The cap prevents a single request from monopolising memory and
    ///     generation time for speculative prefetching.
    /// </para>
    /// </summary>
    /// <param name="n">The 0-based prime index that was requested.</param>
    /// <returns>
    /// The number of additional prime indices to include in the generation window
    /// beyond <paramref name="n"/>.
    /// </returns>
    private static long CalculateBufferSize(long n)
    {
        if (n < 1_000)
        {
            return 1_000;
        }

        if (n < 100_000)
        {
            return n / 10;
        }

        return 100_000;
    }
}