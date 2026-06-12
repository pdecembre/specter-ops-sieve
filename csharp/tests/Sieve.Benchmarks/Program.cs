using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace Sieve.Benchmarks;

/// <summary>
/// Entry point for benchmark execution.
///
/// Usage examples:
/// - dotnet run -c Release --project tests/Sieve.Benchmarks
/// - dotnet run -c Release --project tests/Sieve.Benchmarks -- --filter *WarmCache*
/// </summary>
public static class Program
{
	public static void Main(string[] args)
	{
		_ = BenchmarkRunner.Run<SievePerformanceBenchmarks>(
			ManualConfig
				.Create(DefaultConfig.Instance)
				.WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest))
				.AddJob(Job.Default.WithId("default")),
			args);
	}
}

/// <summary>
/// Benchmarks representative production scenarios:
/// 1) Cold lookup: first time for index (generator + cache fill).
/// 2) Warm lookup: repeated lookup after cache warm-up.
///
/// The benchmark uses a dedicated <see cref="SieveImplementation"/> per iteration
/// for cold paths, and a single pre-warmed instance for warm paths.
/// </summary>
[MemoryDiagnoser]
public class SievePerformanceBenchmarks
{
	private SieveImplementation _warmSieve = null!;

	[Params(2_000, 100_000, 1_000_000)]
	public long Index { get; set; }

	[GlobalSetup]
	public void GlobalSetup()
	{
		_warmSieve = new SieveImplementation();

		// Warm once so the warm benchmark measures fast-path cache retrieval.
		_ = _warmSieve.NthPrime(Index);
	}

	[Benchmark(Description = "Cold lookup")]
	public long ColdLookup()
	{
		// New instance isolates cold-path setup for each benchmark invocation.
		var sieve = new SieveImplementation();
		return sieve.NthPrime(Index);
	}

	[Benchmark(Description = "Warm cache lookup")]
	public long WarmCacheLookup()
	{
		return _warmSieve.NthPrime(Index);
	}
}
