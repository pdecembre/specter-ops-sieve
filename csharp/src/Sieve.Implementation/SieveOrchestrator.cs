using Microsoft.Extensions.Logging;
using Sieve.Core.Abstractions;
using Sieve.Core.Exceptions;
using Sieve.Implementation.Generation;

namespace Sieve.Implementation;

/// <summary>
/// Coordinates cache, generation engines, and metrics to satisfy the primary ISieve contract.
///
/// Execution pipeline:
/// 1) Validate input.
/// 2) Attempt exact cache read.
/// 3) Compute generation range and select strategy (classic vs segmented).
/// 4) Generate and cache contiguous results.
/// 5) Return requested prime and record telemetry.
///
/// Thread-safety:
/// - Delegates mutable behavior to thread-safe dependencies.
/// - Contains no mutable shared state except constants.
/// 
/// Error-handling policy:
/// - Validation errors propagate directly (for example negative index input).
/// - Cancellation propagates unchanged for cooperative callers.
/// - Other unexpected failures are wrapped in <see cref="PrimeComputationException"/>
///   so callers can reliably distinguish computation failures from input issues.
/// </summary>
public sealed class SieveOrchestrator : ISieve
{
    private const long SegmentedThreshold = 1_000_000;

    private readonly IPrimeCache _cache;
    private readonly ClassicSieveGenerator _classicGenerator;
    private readonly SegmentedSieveGenerator _segmentedGenerator;
    private readonly IEstimator _estimator;
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<SieveOrchestrator> _logger;

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
    public long NthPrime(long n)
    {
        // Sync-over-async facade for compatibility with original API shape.
        // The underlying async path remains the single implementation source.
        return NthPrimeAsync(n, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default)
    {
        // Input contract check: public nth-prime API uses 0-based non-negative indices.
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Index must be non-negative.");
        }

        _metricsCollector.RecordRequest();

        try
        {
            if (_cache.TryGetPrimeRange(n, n, out var cachedPrime))
            {
                _metricsCollector.RecordCacheHit();
                _logger.LogDebug("Cache hit for index {Index}: {Prime}", n, cachedPrime[0]);
                return cachedPrime[0];
            }

            _metricsCollector.RecordCacheMiss();

            // Determine incremental generation window.
            // We only request missing range beyond the highest cached index.
            var highestCachedIndex = _cache.GetHighestCachedIndex();
            var generationStart = Math.Max(0, highestCachedIndex + 1);
            var generationEnd = checked(n + CalculateBufferSize(n));

            // Optional call kept for diagnostics/value checking and to aid traceability.
            // The generators themselves still own actual sieve upper-bound estimation.
            var estimatedUpperBound = _estimator.EstimateUpperBound(generationEnd);

            _logger.LogInformation(
                "Generating prime indices [{Start}..{End}] for requested index {RequestedIndex}. Estimated value upper bound={UpperBound}.",
                generationStart,
                generationEnd,
                n,
                estimatedUpperBound);

            // Strategy selection is based on request magnitude.
            var generator = SelectGenerator(n);
            var generated = await generator.GeneratePrimesAsync(generationStart, generationEnd, cancellationToken);

            _metricsCollector.RecordGeneration(generated.LongLength);
            _cache.AddPrimeRange(generationStart, generated);

            // Translate absolute requested index into generated window offset.
            var offset = n - generationStart;
            if (offset < 0 || offset >= generated.LongLength)
            {
                throw new PrimeComputationException(n, "Generated prime range did not contain the requested index.");
            }

            return generated[offset];
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Prime computation was cancelled for index {Index}.", n);
            throw;
        }
        catch (Exception ex) when (ex is not PrimeComputationException)
        {
            _logger.LogError(ex, "Prime computation failed for index {Index}.", n);
            throw new PrimeComputationException(n, "Prime computation failed.", ex);
        }
    }

    private IPrimeGenerator SelectGenerator(long index)
    {
        return index < SegmentedThreshold
            ? _classicGenerator
            : _segmentedGenerator;
    }

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