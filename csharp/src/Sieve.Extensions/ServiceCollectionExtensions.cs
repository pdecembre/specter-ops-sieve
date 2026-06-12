using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sieve.Core.Abstractions;
using Sieve.Implementation;
using Sieve.Implementation.Caching;
using Sieve.Implementation.Estimation;
using Sieve.Implementation.Generation;
using Sieve.Implementation.Metrics;

namespace Sieve.Extensions;

/// <summary>
/// Registers all required Sieve services into a dependency injection container.
///
/// Registration strategy:
/// 1) Stateless services are singletons (safe and allocation-friendly).
/// 2) Cache is singleton so all requests share warm state.
/// 3) Orchestrator is singleton facade over singleton dependencies.
/// 
/// Why this extension exists:
/// - centralizes wiring decisions so application code does not replicate
///   construction logic,
/// - keeps test and production composition consistent,
/// - exposes a single place to tune cache and segmentation policy.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Sieve services with default configuration.
    /// </summary>
    /// <param name="services">Service collection being configured.</param>
    /// <returns>The same collection for fluent chaining.</returns>
    public static IServiceCollection AddSieveServices(this IServiceCollection services)
    {
        return services.AddSieveServices(_ => { });
    }

    /// <summary>
    /// Registers Sieve services with custom configuration.
    /// </summary>
    /// <param name="services">Service collection being configured.</param>
    /// <param name="configure">Configuration callback for Sieve options.</param>
    /// <returns>The same collection for fluent chaining.</returns>
    public static IServiceCollection AddSieveServices(
        this IServiceCollection services,
        Action<SieveConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Build effective runtime options once, then register as singleton so all
        // components share the same tuned values.
        var configuration = new SieveConfiguration();
        configure(configuration);

        services.AddSingleton(configuration);

        // Core mathematical/stateless components.
        services.AddSingleton<IEstimator, RosserSchoenfeldEstimator>();
        services.AddSingleton<IMetricsCollector, AtomicMetricsCollector>();

        // Cache shared across requests to maximize hit ratio.
        services.AddSingleton<IPrimeCache>(_ =>
            new ConcurrentLruPrimeCache(configuration.MaxCacheMemoryBytes, configuration.CacheChunkSize));

        // Register concrete generators individually so the orchestrator can select explicitly.
        services.AddSingleton<ClassicSieveGenerator>();
        services.AddSingleton<SegmentedSieveGenerator>(sp =>
            new SegmentedSieveGenerator(sp.GetRequiredService<IEstimator>(), configuration.SegmentSize));

        // Optionally expose generators by strategy interface for diagnostics/extensibility.
        services.AddSingleton<IPrimeGenerator>(sp => sp.GetRequiredService<ClassicSieveGenerator>());
        services.AddSingleton<IPrimeGenerator>(sp => sp.GetRequiredService<SegmentedSieveGenerator>());

        // Facade exposed as the core contract.
        services.AddSingleton<ISieve, SieveOrchestrator>();

        EnsureLoggingConfigured(services);
        return services;
    }

    private static void EnsureLoggingConfigured(IServiceCollection services)
    {
        // Respect host-level logging if already configured.
        if (services.Any(sd => sd.ServiceType == typeof(ILoggerFactory)))
        {
            return;
        }

        // Fallback logging profile for standalone/console usage.
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }
}

/// <summary>
/// Runtime settings for Sieve services.
///
/// Memory interpretation for the default values:
/// - <see cref="MaxCacheMemoryBytes"/> = 100 MB = 102,400 KB total cache budget.
/// - <see cref="CacheChunkSize"/> = 10,000 prime slots per chunk, which is stored as
///   10,000 <c>long</c> values in the current cache implementation. That is 80,000 bytes
///   = 78.125 KB of raw prime-value storage per fully populated chunk, plus small object
///   and array overhead.
/// - <see cref="SegmentSize"/> = 1,048,576 integer values per segment. In the current
///   segmented sieve implementation this is backed by a <c>bool[]</c>, so the active segment
///   bitmap is roughly 1,048,576 bytes = 1,024 KB, plus array overhead.
/// </summary>
public sealed record SieveConfiguration
{
    /// <summary>
    /// Maximum cache memory budget in bytes.
    ///
    /// Default: 100 MB = 102,400 KB.
    ///
    /// This is the total memory ceiling used by <see cref="ConcurrentLruPrimeCache"/>
    /// for all cached prime chunks combined.
    /// </summary>
    public long MaxCacheMemoryBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>
    /// Number of prime indices grouped into a cache chunk.
    ///
    /// Default: 10,000 prime slots.
    ///
    /// In the current cache implementation each chunk stores 10,000 <c>long</c> values.
    /// Raw payload size = 10,000 * 8 bytes = 80,000 bytes = 78.125 KB, not counting
    /// object/array overhead. This value controls chunk granularity, not the total cache budget.
    /// </summary>
    public int CacheChunkSize { get; set; } = 10_000;

    /// <summary>
    /// Segment width (in integer values) for segmented sieve processing.
    ///
    /// Default: 1,048,576 values.
    ///
    /// In the current segmented sieve implementation each segment uses a <c>bool[]</c>
    /// with one flag per integer value. Because a <c>bool</c> occupies 1 byte in the
    /// array, the active segment bitmap is roughly 1,048,576 bytes = 1,024 KB, plus
    /// array overhead. This value controls the size of the working window used during
    /// segmentation.
    /// </summary>
    public int SegmentSize { get; set; } = 1024 * 1024;
}