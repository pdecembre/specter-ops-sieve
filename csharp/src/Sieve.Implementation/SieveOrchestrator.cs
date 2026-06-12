using Microsoft.Extensions.Logging;
using Sieve.Core.Abstractions;
using Sieve.Core.Exceptions;
using Sieve.Implementation.Generation;

namespace Sieve.Implementation;

/// <summary>
/// Coordinates cache, generation engines, and metrics to satisfy the primary ISieve contract.
///
/// <para>
/// HIGH-LEVEL ROLE:
/// This class is the "traffic controller" of the whole prime-lookup system. It doesn't
/// itself know HOW to compute primes (that's the job of <see cref="ClassicSieveGenerator"/>
/// and <see cref="SegmentedSieveGenerator"/>), and it doesn't know WHERE cached results
/// live (that's <see cref="IPrimeCache"/>). Instead, it answers the question "given a
/// request for the n-th prime, what is the cheapest sequence of steps to produce a
/// correct answer?" -- check the cache first, generate only what's missing, pick the
/// right generation strategy for the size of the job, and record what happened along
/// the way for observability.
/// </para>
///
/// <para>
/// Execution pipeline:
/// 1) Validate input.
/// 2) Attempt exact cache read.
/// 3) Compute generation range and select strategy (classic vs segmented).
/// 4) Generate and cache contiguous results.
/// 5) Return requested prime and record telemetry.
/// </para>
///
/// <para>
/// Thread-safety:
/// - Delegates mutable behavior to thread-safe dependencies.
/// - Contains no mutable shared state except constants.
///
/// In other words: this class itself never changes after construction -- every field is
/// either a `readonly` reference to an injected dependency, or a `const`. All the actual
/// "state that changes over time" (the cache contents, the metrics counters) lives
/// inside those injected dependencies, which are individually responsible for their own
/// thread-safety (e.g. <see cref="AtomicMetricsCollector"/> uses Interlocked operations).
/// This means many threads can safely call NthPrimeAsync concurrently on the SAME
/// SieveOrchestrator instance -- there's no shared mutable orchestrator state that could
/// be corrupted by interleaving.
/// </para>
///
/// <para>
/// Error-handling policy:
/// - Validation errors propagate directly (for example negative index input).
/// - Cancellation propagates unchanged for cooperative callers.
/// - Other unexpected failures are wrapped in <see cref="PrimeComputationException"/>
///   so callers can reliably distinguish computation failures from input issues.
///
/// This three-way split exists so that callers can write meaningful catch blocks:
/// catching <see cref="ArgumentOutOfRangeException"/> means "you asked for something
/// invalid"; catching <see cref="OperationCanceledException"/> means "the operation was
/// deliberately cancelled, not a failure"; and catching <see cref="PrimeComputationException"/>
/// means "something genuinely went wrong while computing the answer, and the original
/// exception is attached as InnerException for diagnostics."
/// </para>
/// </summary>
public sealed class SieveOrchestrator : ISieve
{
    /// <summary>
    /// The 0-based prime-index threshold at which we switch from the classic
    /// (in-memory, whole-range) sieve to the segmented sieve.
    ///
    /// <para>
    /// WHY THIS EXISTS:
    /// The classic Sieve of Eratosthenes allocates a single boolean array covering the
    /// ENTIRE range from 0 up to the estimated upper bound. For small/medium ranges this
    /// is simple and fast. But for very large indices (millions+), the upper bound for
    /// "the n-th prime" grows large enough that a single contiguous array would consume
    /// an impractical amount of memory (and take a long time to allocate/zero).
    ///
    /// The segmented sieve instead processes the range in fixed-size chunks ("segments"),
    /// reusing a small, bounded amount of memory regardless of how large the overall
    /// range is -- at the cost of somewhat more bookkeeping per segment.
    /// </para>
    ///
    /// <para>
    /// 1,000,000 is chosen as a practical crossover point: below this, the classic
    /// sieve's simplicity and lower per-segment overhead wins; at or above it, the
    /// segmented sieve's bounded memory footprint becomes the more important property.
    /// </para>
    /// </summary>
    private const long SegmentedThreshold = 1_000_000;

    // ---- Injected dependencies (all readonly -- set once at construction, never reassigned) ----

    /// <summary>
    /// Stores previously-generated primes so repeated/overlapping requests don't require
    /// re-running the sieve from scratch. Acts as the "memory" of the system across calls.
    /// </summary>
    private readonly IPrimeCache _cache;

    /// <summary>
    /// Generation strategy for smaller ranges (below <see cref="SegmentedThreshold"/>).
    /// Computes primes using a single in-memory sieve over the whole requested range.
    /// </summary>
    private readonly ClassicSieveGenerator _classicGenerator;

    /// <summary>
    /// Generation strategy for larger ranges (at or above <see cref="SegmentedThreshold"/>).
    /// Computes primes in bounded-memory chunks ("segments") to avoid allocating one huge array.
    /// </summary>
    private readonly SegmentedSieveGenerator _segmentedGenerator;

    /// <summary>
    /// Provides an analytical (formula-based) upper-bound estimate for where the n-th
    /// prime is likely to fall (e.g., the Rosser-Schoenfeld-based estimator). Used here
    /// purely for logging/diagnostics -- the actual generators perform their own
    /// authoritative bound calculations as part of generation.
    /// </summary>
    private readonly IEstimator _estimator;

    /// <summary>
    /// Records counts of requests, cache hits/misses, and generation activity for
    /// observability (e.g., an <see cref="AtomicMetricsCollector"/>).
    /// </summary>
    private readonly IMetricsCollector _metricsCollector;

    /// <summary>
    /// Structured logger for this orchestrator, used to record diagnostic information
    /// about each request's path through the pipeline (cache hit/miss, generation
    /// ranges, cancellations, failures).
    /// </summary>
    private readonly ILogger<SieveOrchestrator> _logger;

    /// <summary>
    /// Constructs the orchestrator with all of its collaborating services.
    /// </summary>
    /// <param name="cache">Backing store for previously-computed prime ranges.</param>
    /// <param name="classicGenerator">Strategy used for requests below <see cref="SegmentedThreshold"/>.</param>
    /// <param name="segmentedGenerator">Strategy used for requests at/above <see cref="SegmentedThreshold"/>.</param>
    /// <param name="estimator">Analytical upper-bound estimator, used for diagnostic logging.</param>
    /// <param name="metricsCollector">Sink for request/cache/generation telemetry.</param>
    /// <param name="logger">Structured logger for this orchestrator's activity.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown immediately if ANY dependency is null. This is a deliberate "fail fast"
    /// design: it's far better to discover a misconfigured dependency-injection
    /// container at application startup (when this constructor first runs) than to
    /// discover it later as a confusing NullReferenceException buried deep inside
    /// NthPrimeAsync, possibly only under specific request patterns.
    /// </exception>
    public SieveOrchestrator(
        IPrimeCache cache,
        ClassicSieveGenerator classicGenerator,
        SegmentedSieveGenerator segmentedGenerator,
        IEstimator estimator,
        IMetricsCollector metricsCollector,
        ILogger<SieveOrchestrator> logger)
    {
        // Each dependency is checked individually (rather than, say, checking a single
        // aggregate object) so that if construction DOES fail, the exception message
        // names the SPECIFIC parameter that was null -- making misconfigured DI
        // registrations easy to diagnose from the exception message alone.
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _classicGenerator = classicGenerator ?? throw new ArgumentNullException(nameof(classicGenerator));
        _segmentedGenerator = segmentedGenerator ?? throw new ArgumentNullException(nameof(segmentedGenerator));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Synchronous entry point: returns the n-th prime (0-based index).
    /// </summary>
    /// <param name="n">The 0-based index of the desired prime (0 -> 2, 1 -> 3, ...).</param>
    /// <returns>The n-th prime number.</returns>
    /// <remarks>
    /// <para>
    /// WHAT "SYNC-OVER-ASYNC" MEANS AND WHY THIS EXISTS:
    /// All the real work happens in <see cref="NthPrimeAsync"/>, which is `async` because
    /// generation may involve genuinely long-running work (and supports cancellation).
    /// This method exists purely so that callers using an older or synchronous-only API
    /// surface (the "original API shape" mentioned in the summary) can still call a
    /// plain, blocking method without needing to become `async` themselves.
    /// </para>
    ///
    /// <para>
    /// HOW <c>.GetAwaiter().GetResult()</c> WORKS:
    /// Calling an `async` method returns a <see cref="Task{TResult}"/> representing the
    /// in-progress (or already-completed) operation. `.GetAwaiter()` retrieves an
    /// "awaiter" object for that task, and `.GetResult()` BLOCKS THE CALLING THREAD until
    /// the task completes, then either returns its result or -- if the task faulted --
    /// re-throws the original exception directly (unlike `Task.Result`, which would wrap
    /// it in an <see cref="AggregateException"/>). This is why `.GetAwaiter().GetResult()`
    /// is generally preferred over `.Result` when bridging async code to a sync caller:
    /// it preserves the original exception type and stack trace for the
    /// catch-blocks in NthPrimeAsync to work as documented.
    /// </para>
    ///
    /// <para>
    /// RISK -- DEADLOCK POTENTIAL (and why it's acceptable here):
    /// Blocking on an async Task can deadlock in environments with a captured
    /// "synchronization context" (classic ASP.NET, WPF/WinForms UI threads) -- if the
    /// async method's continuations try to resume on that same captured context, and
    /// the context's single thread is the one sitting blocked on GetResult(), the
    /// continuation can never run, and GetResult() waits forever.
    ///
    /// This implementation does not show explicit `ConfigureAwait(false)` calls inside
    /// NthPrimeAsync's own awaits to the dependencies it calls (`generator.GeneratePrimesAsync`).
    /// If those downstream calls also avoid capturing a synchronization context (directly
    /// or via their own ConfigureAwait(false) usage), this is safe in modern ASP.NET Core
    /// (which has no synchronization context by default). If this orchestrator is ever
    /// consumed from a classic ASP.NET or desktop UI context, calling this synchronous
    /// `NthPrime` wrapper from a UI/request thread could deadlock -- callers in those
    /// environments should prefer <see cref="NthPrimeAsync"/> directly.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public long NthPrime(long n)
    {
        // Sync-over-async facade for compatibility with original API shape.
        // The underlying async path remains the single implementation source.
        return NthPrimeAsync(n, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously returns the n-th prime (0-based index), using cached results where
    /// possible and generating + caching any missing range as needed.
    /// </summary>
    /// <param name="n">The 0-based index of the desired prime (0 -> 2, 1 -> 3, ...).</param>
    /// <param name="cancellationToken">
    /// Token used to cooperatively cancel an in-progress generation. Has no effect on a
    /// cache hit, since no awaitable work occurs in that path.
    /// </param>
    /// <returns>A task that resolves to the n-th prime number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown synchronously (before any async work begins) if <paramref name="n"/> is negative.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Propagated unchanged if <paramref name="cancellationToken"/> is signalled during generation.
    /// </exception>
    /// <exception cref="PrimeComputationException">
    /// Thrown if any unexpected error occurs during cache access, range calculation, or
    /// generation. The original exception is preserved as <c>InnerException</c>.
    /// </exception>
    /// <inheritdoc />
    public async Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default)
    {
        // -----------------------------------------------------------------
        // STEP 1: VALIDATE INPUT
        // -----------------------------------------------------------------
        // Input contract check: public nth-prime API uses 0-based non-negative indices.
        //
        // This check happens BEFORE _metricsCollector.RecordRequest(), so a malformed
        // request (negative index) is NOT counted as a "request" in the metrics --
        // it never gets far enough to be considered a real attempt at the operation.
        // It also happens before the try/catch below, so this specific exception type
        // is never caught/wrapped -- it propagates immediately and unmodified, per the
        // class's documented error-handling policy ("Validation errors propagate directly").
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Index must be non-negative.");
        }

        // From this point on, the request is considered "real" and is counted.
        _metricsCollector.RecordRequest();

        try
        {
            // -----------------------------------------------------------------
            // STEP 2: ATTEMPT EXACT CACHE READ
            // -----------------------------------------------------------------
            // TryGetPrimeRange(n, n, ...) asks the cache for the inclusive range
            // [n, n] -- i.e., a "range" containing exactly one index. If the cache
            // already has the n-th prime stored, this returns true and populates
            // `cachedPrime` with a single-element array containing that prime.
            //
            // This is the fast path: if it succeeds, we can return immediately
            // without touching the generators at all.
            if (_cache.TryGetPrimeRange(n, n, out var cachedPrime))
            {
                _metricsCollector.RecordCacheHit();
                _logger.LogDebug("Cache hit for index {Index}: {Prime}", n, cachedPrime[0]);
                return cachedPrime[0];
            }

            // -----------------------------------------------------------------
            // STEP 3: COMPUTE GENERATION RANGE AND SELECT STRATEGY
            // -----------------------------------------------------------------
            // We reach here only if the requested index was NOT already cached.
            _metricsCollector.RecordCacheMiss();

            // Determine incremental generation window.
            // We only request missing range beyond the highest cached index.
            //
            // GetHighestCachedIndex() tells us the highest 0-based prime index the
            // cache currently holds (e.g., if the cache has primes for indices 0..499,
            // this returns 499). We start generation at the NEXT index after that, so
            // we never regenerate primes we already have -- the cache grows by
            // contiguous extension, never by overlapping/duplicating ranges.
            //
            // Math.Max(0, ...) guards the "cache is empty" case: if the cache has
            // never been populated, a typical "empty" sentinel for
            // GetHighestCachedIndex() would be -1 (meaning "nothing cached, even
            // index 0"). -1 + 1 = 0, which Math.Max(0, 0) leaves as 0 anyway -- but
            // the Math.Max guards against any implementation that might return a
            // value lower than -1, ensuring generationStart can never be negative.
            var highestCachedIndex = _cache.GetHighestCachedIndex();
            var generationStart = Math.Max(0, highestCachedIndex + 1);

            // generationEnd is the FURTHEST index we'll generate in this pass --
            // it's the requested index `n` PLUS a "prefetch buffer" (see
            // CalculateBufferSize below). Generating extra primes beyond exactly
            // what was asked for means future nearby requests (e.g., the user asks
            // for index 1000, then shortly after asks for index 1001) are likely to
            // be cache hits instead of triggering another generation pass.
            //
            // `checked(...)` wraps the addition so that if `n` is extremely close to
            // long.MaxValue (an unrealistic but theoretically possible input) and
            // adding the buffer would overflow, we get an immediate, clear
            // OverflowException rather than a silently wrapped-around (and
            // nonsensical, likely negative) generationEnd value that could cause
            // very confusing downstream behavior in the generators.
            var generationEnd = checked(n + CalculateBufferSize(n));

            // Optional call kept for diagnostics/value checking and to aid traceability.
            // The generators themselves still own actual sieve upper-bound estimation.
            //
            // NOTE: this estimatedUpperBound value is NOT passed to the generators and
            // does NOT influence what gets generated -- it exists purely so the log
            // line below can record "here's roughly what numeric value we expect the
            // primes in this range to reach," which is useful when reading logs to
            // sanity-check that generation ranges and resulting values look reasonable
            // (e.g., spotting if a generator's actual output wildly disagrees with this
            // estimate, which could indicate a bug).
            var estimatedUpperBound = _estimator.EstimateUpperBound(generationEnd);

            _logger.LogInformation(
                "Generating prime indices [{Start}..{End}] for requested index {RequestedIndex}. Estimated value upper bound={UpperBound}.",
                generationStart,
                generationEnd,
                n,
                estimatedUpperBound);

            // Strategy selection is based on request magnitude.
            //
            // Note this decision is based on `n` (the originally REQUESTED index),
            // not on `generationEnd` (which includes the prefetch buffer). This means
            // a request just below SegmentedThreshold will use the classic generator
            // even though its generation range (with buffer) might creep slightly
            // past the threshold -- a deliberate simplification, since the buffer
            // sizes (capped at 100,000 -- see CalculateBufferSize) are small relative
            // to the 1,000,000 threshold and won't meaningfully change the
            // memory/performance characteristics that motivate the threshold.
            var generator = SelectGenerator(n);

            // -----------------------------------------------------------------
            // STEP 4: GENERATE AND CACHE CONTIGUOUS RESULTS
            // -----------------------------------------------------------------
            // Delegate the actual sieve computation to whichever generator was
            // selected. `await` here suspends this method (without blocking the
            // calling thread) until generation completes, and respects
            // cancellationToken -- if the token is signalled mid-generation, the
            // generator is expected to throw OperationCanceledException, which is
            // caught and handled by the catch block below.
            var generated = await generator.GeneratePrimesAsync(generationStart, generationEnd, cancellationToken);

            // Record how many primes this generation pass produced (for the
            // "TotalPrimesGenerated" running total) and increment the
            // "GenerationCalls" counter by one (see AtomicMetricsCollector).
            // LongLength (rather than Length) is used because `generated` could in
            // principle be a very large array for high indices -- LongLength avoids
            // any truncation issues, returning the array's element count as a long.
            _metricsCollector.RecordGeneration(generated.LongLength);

            // Store the newly generated, contiguous block of primes in the cache,
            // tagged with the absolute index (generationStart) where it begins.
            // This extends the cache's "highest cached index" forward so future
            // requests (including the prefetch buffer's worth of indices) can be
            // served as cache hits in Step 2.
            _cache.AddPrimeRange(generationStart, generated);

            // -----------------------------------------------------------------
            // STEP 5: RETURN REQUESTED PRIME AND RECORD TELEMETRY
            // -----------------------------------------------------------------
            // Translate absolute requested index into generated window offset.
            //
            // `generated` is a 0-based array representing the contiguous range
            // [generationStart .. generationEnd]. The caller asked for absolute
            // index `n`, which corresponds to position (n - generationStart) within
            // this array. For example, if generationStart = 500 and n = 503, then
            // offset = 3 -- the 4th element of `generated`.
            var offset = n - generationStart;

            // This bounds check is a defensive invariant/sanity check, not an
            // expected-input-error path. If generationStart and generationEnd were
            // computed correctly (Step 3) and the generator honored its contract
            // (returning exactly the primes for indices [generationStart..generationEnd]),
            // `offset` should ALWAYS be within [0, generated.LongLength). If this
            // check ever fails, it indicates an internal bug -- e.g. a generator
            // returning fewer primes than the requested range implies, or a
            // mismatch between how the orchestrator and a generator interpret
            // index boundaries -- rather than anything the CALLER did wrong. That's
            // why it throws PrimeComputationException (an internal-failure type)
            // rather than ArgumentOutOfRangeException (an input-validation type).
            if (offset < 0 || offset >= generated.LongLength)
            {
                throw new PrimeComputationException(n, "Generated prime range did not contain the requested index.");
            }

            return generated[offset];
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a NORMAL, expected outcome for a cooperatively
            // cancellable operation -- it represents the caller changing its mind,
            // not a failure of the computation itself. We log it at Warning (so
            // it's visible in logs without being treated as an error/alert), and
            // then re-throw the ORIGINAL exception unchanged (a bare `throw;`
            // preserves the original stack trace) so callers awaiting this task
            // see a standard OperationCanceledException/TaskCanceledException,
            // exactly as they would from any other cancellable async API -- not
            // wrapped in a PrimeComputationException, which would force callers to
            // unwrap InnerException just to detect "this was a cancellation."
            _logger.LogWarning("Prime computation was cancelled for index {Index}.", n);
            throw;
        }
        catch (Exception ex) when (ex is not PrimeComputationException)
        {
            // Catch-all for any OTHER unexpected failure (cache errors, generator
            // bugs, arithmetic overflow from the `checked` block above, etc.).
            //
            // The `when (ex is not PrimeComputationException)` exception filter is
            // important: it prevents this catch block from re-wrapping a
            // PrimeComputationException that was ALREADY thrown and is already
            // propagating (e.g., the "did not contain the requested index" check
            // above). Without this filter, a PrimeComputationException thrown
            // inside the try block would be caught here and wrapped in ANOTHER
            // PrimeComputationException -- a confusing "exception wrapped in
            // itself" structure where InnerException is itself a
            // PrimeComputationException, and the original underlying cause (if any)
            // would be one level deeper than callers might expect.
            //
            // By excluding PrimeComputationException from this catch, such
            // exceptions pass through this clause untouched (there's no other catch
            // block for them, so they propagate to the caller exactly as thrown) --
            // ensuring a caller only ever needs to unwrap ONE level of
            // PrimeComputationException to find the true root cause, no matter
            // which internal step produced it.
            _logger.LogError(ex, "Prime computation failed for index {Index}.", n);
            throw new PrimeComputationException(n, "Prime computation failed.", ex);
        }
    }

    /// <summary>
    /// Chooses which generation strategy to use based on the magnitude of the
    /// requested prime index.
    /// </summary>
    /// <param name="index">The 0-based requested prime index (NOT the generation range end).</param>
    /// <returns>
    /// <see cref="ClassicSieveGenerator"/> for indices below <see cref="SegmentedThreshold"/>;
    /// otherwise <see cref="SegmentedSieveGenerator"/>.
    /// </returns>
    /// <remarks>
    /// Both generators implement <see cref="IPrimeGenerator"/>, so this method's return
    /// type is the shared interface -- the caller (NthPrimeAsync) doesn't need to know
    /// or care which concrete generator it received; it simply calls
    /// <c>GeneratePrimesAsync</c> on whichever one is returned. This is a simple
    /// Strategy-pattern selector: the "decision" of which algorithm to use is isolated
    /// to this one method, making it easy to find and adjust if the threshold or
    /// selection logic ever needs to change.
    /// </remarks>
    private IPrimeGenerator SelectGenerator(long index)
    {
        return index < SegmentedThreshold
            ? _classicGenerator
            : _segmentedGenerator;
    }

    /// <summary>
    /// Computes how many EXTRA prime indices beyond the requested index <paramref name="n"/>
    /// should be generated and cached in the same pass, as a "prefetch" optimization.
    /// </summary>
    /// <param name="n">The 0-based requested prime index.</param>
    /// <returns>The number of additional indices to generate beyond <paramref name="n"/>.</returns>
    /// <remarks>
    /// <para>
    /// WHY PREFETCH AT ALL:
    /// Generating primes has a fixed "setup cost" (allocating the sieve array,
    /// initializing it, etc.) that's largely independent of how many primes you actually
    /// need. If a caller asks for index 1000 and we generate EXACTLY up to index 1000,
    /// then a moment later asks for index 1001, we'd pay that setup cost again for just
    /// one more prime. By generating a bit extra each time, many "next index" or
    /// "nearby index" follow-up requests become cache hits (Step 2) instead of
    /// triggering a whole new generation pass.
    /// </para>
    ///
    /// <para>
    /// THE THREE TIERS, AND WHY EACH ONE IS SHAPED THE WAY IT IS:
    /// </para>
    ///
    /// <para>
    /// 1) <c>n &lt; 1,000</c> -> fixed buffer of 1,000.
    ///    For small requests, generation is essentially free regardless of exact size
    ///    (a sieve over a few thousand numbers is microseconds of work). A flat,
    ///    generous fixed buffer means even a request for index 0 immediately warms the
    ///    cache with the first ~1,000 primes, covering a huge proportion of "small
    ///    number" use cases (tests, examples, casual exploration) with a single
    ///    generation pass.
    /// </para>
    ///
    /// <para>
    /// 2) <c>1,000 &lt;= n &lt; 100,000</c> -> proportional buffer of <c>n / 10</c>.
    ///    Here, a flat buffer would either be too small (wasteful re-generation for
    ///    requests near the top of this range) or too large (wasted work for requests
    ///    near the bottom). Scaling the buffer as a percentage (10%) of the requested
    ///    index keeps the "extra work" roughly proportional to the "main work" --
    ///    generation cost in this range grows roughly with the size of the range, so a
    ///    10% buffer adds a roughly constant ~10% overhead regardless of where in this
    ///    tier the request falls.
    /// </para>
    ///
    /// <para>
    /// 3) <c>n &gt;= 100,000</c> -> fixed cap of 100,000.
    ///    Without a cap, the proportional approach from tier 2 would mean a request for
    ///    the 10-millionth prime triggers an extra 1-million-prime prefetch -- a
    ///    massive, possibly unwanted amount of extra memory and computation for a
    ///    "just in case" optimization. Capping the buffer at a fixed 100,000 bounds the
    ///    worst-case "extra work" to a known, constant amount no matter how large `n`
    ///    gets -- trading away some of the proportional benefit at extreme scales in
    ///    exchange for predictable, bounded resource usage (which matters especially
    ///    for the segmented generator's memory-conscious design).
    /// </para>
    ///
    /// <para>
    /// Note the three tiers form a continuous, non-decreasing function of `n` at the
    /// boundaries: just below n=1,000 the buffer is 1,000; at n=1,000 the proportional
    /// formula gives 1,000/10=100 -- so there IS a downward step at that boundary (from
    /// 1,000 to 100). Similarly at n=100,000, the proportional formula would give
    /// 10,000, but jumps to the 100,000 cap -- an upward step. These discontinuities are
    /// a known characteristic of this simple heuristic; they don't affect CORRECTNESS
    /// (the buffer is purely an optimization, never required for a correct answer), only
    /// the relative efficiency of cache warm-up right around those two boundary values.
    /// </para>
    /// </remarks>
    private static long CalculateBufferSize(long n)
    {
        // Simple adaptive prefetch heuristic:
        // - small requests use fixed warm-up window,
        // - medium requests use proportional look-ahead,
        // - large requests cap look-ahead for bounded memory/time overhead.
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