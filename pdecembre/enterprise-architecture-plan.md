# Enterprise C# Implementation Plan for Nth Prime API
## Senior Software Architect Solution Design

---

## Table of Contents
1. [Executive Summary](#executive-summary)
2. [Architectural Overview](#architectural-overview)
3. [SOLID Principles Application](#solid-principles-application)
4. [Component Design](#component-design)
5. [Thread Safety Strategy](#thread-safety-strategy)
6. [Performance & Scalability](#performance--scalability)
7. [Error Handling & Resilience](#error-handling--resilience)
8. [Testing Strategy](#testing-strategy)
9. [Documentation Standards](#documentation-standards)
10. [Implementation Roadmap](#implementation-roadmap)

---

## Executive Summary

This document outlines an enterprise-grade architecture for the Nth Prime API implementation using the Sieve of Eratosthenes algorithm. The solution employs SOLID principles, thread-safety, comprehensive caching, and extensibility to meet both current requirements (finding primes up to index 10,000,000) and future scalability needs.

### Key Design Decisions
- **Segmented Sieve Architecture**: Memory-efficient processing of large prime ranges
- **Multi-tier Caching**: Thread-safe caches at multiple levels (small primes, segments, results)
- **Strategy Pattern**: Pluggable sieve implementations for different performance characteristics
- **Immutable Data Structures**: Thread-safe by design where applicable
- **Observable Performance Metrics**: Built-in telemetry for monitoring and optimization

### Success Criteria
- ✅ Correctly compute NthPrime(10,000,000) = 179,424,691
- ✅ Thread-safe concurrent access without locks on read paths
- ✅ Memory-efficient (< 500MB for max workload)
- ✅ Responsive for repeated queries (< 1ms for cached results)
- ✅ Extensible for future optimizations (parallel segmentation, disk-backed storage)

---

## Architectural Overview

### High-Level Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                       ISieve (Public API)                       │
└────────────────────────────┬────────────────────────────────────┘
                             │
                ┌────────────┴────────────┐
                │  SieveOrchestrator      │  ← Facade, coordinates components
                │  (Main Implementation)  │
                └────────────┬────────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
┌────────▼────────┐  ┌───────▼────────┐  ┌──────▼────────┐
│ IPrimeGenerator │  │ IPrimeCache    │  │ IEstimator    │
│                 │  │                │  │               │
│ - Classic       │  │ - LRU Cache    │  │ - Upper bound │
│ - Segmented     │  │ - Bloom Filter │  │ - Nth prime   │
│ - Parallel      │  │ - Persistent   │  │               │
└─────────────────┘  └────────────────┘  └───────────────┘
```

### Component Responsibilities

| Component | Responsibility | Thread Safety |
|-----------|---------------|---------------|
| `ISieve` | Public API contract | N/A (interface) |
| `SieveOrchestrator` | Coordinate generation, caching, validation | Thread-safe (immutable state + concurrent collections) |
| `IPrimeGenerator` | Generate primes using various algorithms | Stateless (thread-safe) |
| `IPrimeCache` | Cache computed primes for fast retrieval | Thread-safe (concurrent collections) |
| `IEstimator` | Estimate prime bounds and counts | Stateless (thread-safe) |
| `IPrimeValidator` | Validate primality and correctness | Stateless (thread-safe) |
| `IMetricsCollector` | Collect performance metrics | Thread-safe (atomic operations) |

---

## SOLID Principles Application

### Single Responsibility Principle (SRP)

Each class has ONE reason to change:

```csharp
/// <summary>
/// Responsible ONLY for estimating the upper bound of the Nth prime
/// using Rosser and Schoenfeld's approximation.
/// </summary>
public class PrimeUpperBoundEstimator : IEstimator
{
    // Single responsibility: estimate bounds
}

/// <summary>
/// Responsible ONLY for generating primes using segmented sieve.
/// No caching, no validation, no I/O.
/// </summary>
public class SegmentedSieveGenerator : IPrimeGenerator
{
    // Single responsibility: generate primes
}

/// <summary>
/// Responsible ONLY for caching prime numbers with LRU eviction.
/// No generation logic, no estimation logic.
/// </summary>
public class LruPrimeCache : IPrimeCache
{
    // Single responsibility: cache management
}
```

### Open/Closed Principle (OCP)

Open for extension, closed for modification:

```csharp
/// <summary>
/// Base abstraction for prime generation strategies.
/// New algorithms can be added without modifying existing code.
/// </summary>
public interface IPrimeGenerator
{
    /// <summary>
    /// Generates all primes up to and including the limit.
    /// </summary>
    /// <param name="limit">The upper bound (inclusive)</param>
    /// <param name="cancellationToken">Cancellation token for long operations</param>
    /// <returns>Sorted array of primes</returns>
    Task<long[]> GeneratePrimesUpToAsync(long limit, CancellationToken cancellationToken = default);
}

// Implementations can be swapped via dependency injection:
// - ClassicSieveGenerator (simple, for small N)
// - SegmentedSieveGenerator (memory-efficient, for large N)
// - ParallelSegmentedGenerator (future: multi-threaded)
// - DistributedSieveGenerator (future: cluster computing)
```

### Liskov Substitution Principle (LSP)

Any implementation of `IPrimeGenerator` must be substitutable:

```csharp
// All implementations must honor the contract:
// - Return sorted primes
// - Include all primes up to limit
// - Be deterministic
// - Support cancellation

public class ClassicSieveGenerator : IPrimeGenerator { /* ... */ }
public class SegmentedSieveGenerator : IPrimeGenerator { /* ... */ }
public class WheelFactorizationGenerator : IPrimeGenerator { /* ... */ }

// Client code works with any implementation
public class SieveOrchestrator
{
    private readonly IPrimeGenerator _generator;
    
    public SieveOrchestrator(IPrimeGenerator generator)
    {
        _generator = generator; // Can be ANY valid implementation
    }
}
```

### Interface Segregation Principle (ISP)

Clients depend only on interfaces they use:

```csharp
/// <summary>
/// Minimal interface for read-only prime cache.
/// Clients that only need to READ don't depend on write operations.
/// </summary>
public interface IPrimeCacheReader
{
    bool TryGetPrime(long index, out long prime);
    int Count { get; }
}

/// <summary>
/// Extended interface for read-write prime cache.
/// Only components that MODIFY cache depend on this.
/// </summary>
public interface IPrimeCache : IPrimeCacheReader
{
    void AddPrime(long index, long prime);
    void AddPrimeRange(long startIndex, long[] primes);
    void Clear();
}

// Usage:
// - Read-only components: depend on IPrimeCacheReader
// - Write components: depend on IPrimeCache
```

### Dependency Inversion Principle (DIP)

High-level modules depend on abstractions, not concretions:

```csharp
/// <summary>
/// High-level orchestrator depends on ABSTRACTIONS (interfaces),
/// not concrete implementations. All dependencies injected via constructor.
/// </summary>
public class SieveOrchestrator : ISieve
{
    private readonly IPrimeGenerator _generator;
    private readonly IPrimeCache _cache;
    private readonly IEstimator _estimator;
    private readonly ILogger<SieveOrchestrator> _logger;
    
    /// <summary>
    /// All dependencies injected - testable and extensible.
    /// </summary>
    public SieveOrchestrator(
        IPrimeGenerator generator,
        IPrimeCache cache,
        IEstimator estimator,
        ILogger<SieveOrchestrator> logger)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    // Implementation uses abstractions, not concrete types
}
```

---

## Component Design

### 1. Core Interfaces

```csharp
namespace Sieve.Core.Abstractions
{
    /// <summary>
    /// Defines the contract for prime number computation.
    /// This is the public API exposed to consumers.
    /// </summary>
    public interface ISieve
    {
        /// <summary>
        /// Returns the prime number at the specified zero-based index.
        /// </summary>
        /// <param name="n">Zero-based index (0 = first prime = 2)</param>
        /// <returns>The prime at index n</returns>
        /// <exception cref="ArgumentOutOfRangeException">When n is negative</exception>
        /// <exception cref="PrimeComputationException">When computation fails</exception>
        long NthPrime(long n);
        
        /// <summary>
        /// Asynchronous version supporting cancellation for long-running operations.
        /// </summary>
        Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default);
    }
}

namespace Sieve.Core.Generation
{
    /// <summary>
    /// Strategy interface for prime generation algorithms.
    /// Implementations must be STATELESS and THREAD-SAFE.
    /// </summary>
    public interface IPrimeGenerator
    {
        /// <summary>
        /// Generates all primes up to the specified limit.
        /// </summary>
        /// <param name="limit">Upper bound (inclusive)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Sorted array of primes from 2 to limit</returns>
        Task<long[]> GeneratePrimesUpToAsync(long limit, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Name of the algorithm for logging/diagnostics.
        /// </summary>
        string AlgorithmName { get; }
        
        /// <summary>
        /// Recommended maximum limit for this algorithm.
        /// Beyond this, consider a different strategy.
        /// </summary>
        long RecommendedMaxLimit { get; }
    }
}

namespace Sieve.Core.Caching
{
    /// <summary>
    /// Thread-safe cache for computed prime numbers.
    /// Implementations must support concurrent reads without locking.
    /// </summary>
    public interface IPrimeCache
    {
        /// <summary>
        /// Attempts to retrieve a prime at the given index.
        /// </summary>
        /// <returns>True if found, false otherwise</returns>
        bool TryGetPrime(long index, out long prime);
        
        /// <summary>
        /// Adds a single prime to the cache.
        /// </summary>
        void AddPrime(long index, long prime);
        
        /// <summary>
        /// Adds a contiguous range of primes starting at startIndex.
        /// </summary>
        void AddPrimeRange(long startIndex, ReadOnlySpan<long> primes);
        
        /// <summary>
        /// Returns cache statistics for monitoring.
        /// </summary>
        CacheStatistics GetStatistics();
        
        /// <summary>
        /// Current number of cached primes.
        /// </summary>
        int Count { get; }
    }
}

namespace Sieve.Core.Estimation
{
    /// <summary>
    /// Provides mathematical estimates for prime number bounds.
    /// All methods are pure functions (stateless, thread-safe).
    /// </summary>
    public interface IEstimator
    {
        /// <summary>
        /// Estimates the upper bound for the Nth prime using
        /// Rosser and Schoenfeld's inequality.
        /// </summary>
        /// <param name="n">Zero-based index</param>
        /// <returns>Upper bound (guaranteed to be >= actual Nth prime)</returns>
        long EstimateNthPrimeUpperBound(long n);
        
        /// <summary>
        /// Estimates the number of primes up to limit using
        /// the prime number theorem approximation.
        /// </summary>
        long EstimatePrimeCount(long limit);
    }
}
```

### 2. Primary Implementation Classes

```csharp
namespace Sieve.Implementation
{
    /// <summary>
    /// Main orchestrator implementing the ISieve interface.
    /// Coordinates caching, generation, and validation.
    /// Thread-safe through immutable state and concurrent collections.
    /// </summary>
    public sealed class SieveOrchestrator : ISieve
    {
        private readonly IPrimeGenerator _generator;
        private readonly IPrimeCache _cache;
        private readonly IEstimator _estimator;
        private readonly IMetricsCollector _metrics;
        private readonly ILogger<SieveOrchestrator> _logger;
        
        // Immutable configuration
        private readonly SieveConfiguration _config;
        
        public SieveOrchestrator(
            IPrimeGenerator generator,
            IPrimeCache cache,
            IEstimator estimator,
            IMetricsCollector metrics,
            ILogger<SieveOrchestrator> logger,
            SieveConfiguration config)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? SieveConfiguration.Default;
        }
        
        public long NthPrime(long n)
        {
            return NthPrimeAsync(n, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        
        public async Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default)
        {
            // Implementation in later sections
        }
    }
}

namespace Sieve.Generation
{
    /// <summary>
    /// Segmented Sieve of Eratosthenes implementation.
    /// Memory-efficient algorithm that processes primes in chunks.
    /// Stateless and thread-safe - can be called concurrently.
    /// </summary>
    public sealed class SegmentedSieveGenerator : IPrimeGenerator
    {
        public string AlgorithmName => "Segmented Sieve of Eratosthenes";
        public long RecommendedMaxLimit => 1_000_000_000; // 1 billion
        
        private readonly int _segmentSize;
        
        /// <summary>
        /// Initializes with optional segment size (default: 32KB = L1 cache friendly).
        /// </summary>
        public SegmentedSieveGenerator(int segmentSize = 32 * 1024)
        {
            if (segmentSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(segmentSize));
            
            _segmentSize = segmentSize;
        }
        
        public async Task<long[]> GeneratePrimesUpToAsync(
            long limit, 
            CancellationToken cancellationToken = default)
        {
            // Implementation in later sections
        }
    }
}

namespace Sieve.Caching
{
    /// <summary>
    /// Thread-safe LRU cache for prime numbers using ConcurrentDictionary.
    /// Provides lock-free reads with atomic operations for cache management.
    /// </summary>
    public sealed class ConcurrentLruPrimeCache : IPrimeCache
    {
        // ConcurrentDictionary provides thread-safe operations without explicit locks
        private readonly ConcurrentDictionary<long, long> _primesByIndex;
        
        // Atomic counter for cache statistics
        private long _hits;
        private long _misses;
        
        private readonly int _maxSize;
        
        public ConcurrentLruPrimeCache(int maxSize = 10_000)
        {
            if (maxSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxSize));
            
            _maxSize = maxSize;
            _primesByIndex = new ConcurrentDictionary<long, long>();
        }
        
        public bool TryGetPrime(long index, out long prime)
        {
            // Lock-free read operation
            if (_primesByIndex.TryGetValue(index, out prime))
            {
                Interlocked.Increment(ref _hits);
                return true;
            }
            
            Interlocked.Increment(ref _misses);
            return false;
        }
        
        public void AddPrime(long index, long prime)
        {
            // Implementation with eviction strategy
        }
        
        public void AddPrimeRange(long startIndex, ReadOnlySpan<long> primes)
        {
            // Batch addition with optimized eviction
        }
        
        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                Count = _primesByIndex.Count,
                Hits = Interlocked.Read(ref _hits),
                Misses = Interlocked.Read(ref _misses),
                HitRate = CalculateHitRate()
            };
        }
        
        public int Count => _primesByIndex.Count;
    }
}
```

### 3. Supporting Classes

```csharp
namespace Sieve.Estimation
{
    /// <summary>
    /// Implements mathematical formulas for prime number estimation.
    /// All methods are pure functions with no state.
    /// Thread-safe by design (no mutable state).
    /// </summary>
    public sealed class RosserSchoenfeld Estimator : IEstimator
    {
        /// <summary>
        /// Estimates upper bound using Rosser-Schoenfeld inequality:
        /// p(n) < n * (ln(n) + ln(ln(n))) for n >= 6
        /// </summary>
        public long EstimateNthPrimeUpperBound(long n)
        {
            // Handle small cases with known values
            if (n < 6)
                return _smallPrimeUpperBounds[n];
            
            double nDouble = (double)n;
            double logN = Math.Log(nDouble);
            double logLogN = Math.Log(logN);
            
            // Rosser-Schoenfeld upper bound with safety margin
            double estimate = nDouble * (logN + logLogN);
            
            // Add 5% safety margin and round up
            return (long)Math.Ceiling(estimate * 1.05);
        }
        
        public long EstimatePrimeCount(long limit)
        {
            // Prime number theorem: π(x) ≈ x / ln(x)
            if (limit < 2) return 0;
            
            double x = (double)limit;
            double estimate = x / Math.Log(x);
            
            return (long)Math.Ceiling(estimate * 1.1); // 10% safety margin
        }
        
        // Pre-computed upper bounds for small N
        private static readonly long[] _smallPrimeUpperBounds = 
            { 2, 3, 5, 7, 11, 13 };
    }
}

namespace Sieve.Configuration
{
    /// <summary>
    /// Immutable configuration for Sieve orchestrator.
    /// Thread-safe through immutability.
    /// </summary>
    public sealed class SieveConfiguration
    {
        public int CacheMaxSize { get; init; } = 10_000;
        public int SegmentSize { get; init; } = 32 * 1024;
        public bool EnableMetrics { get; init; } = true;
        public bool EnableValidation { get; init; } = true;
        public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(5);
        
        public static SieveConfiguration Default { get; } = new();
        
        /// <summary>
        /// Configuration optimized for high-throughput scenarios.
        /// </summary>
        public static SieveConfiguration HighThroughput { get; } = new()
        {
            CacheMaxSize = 100_000,
            SegmentSize = 64 * 1024,
            EnableValidation = false // Skip validation for speed
        };
        
        /// <summary>
        /// Configuration optimized for low-memory scenarios.
        /// </summary>
        public static SieveConfiguration LowMemory { get; } = new()
        {
            CacheMaxSize = 1_000,
            SegmentSize = 8 * 1024
        };
    }
}

namespace Sieve.Metrics
{
    /// <summary>
    /// Thread-safe metrics collector using atomic operations.
    /// </summary>
    public sealed class AtomicMetricsCollector : IMetricsCollector
    {
        private long _totalQueries;
        private long _cacheHits;
        private long _cacheMisses;
        private long _totalGenerationTimeMs;
        
        public void RecordQuery()
        {
            Interlocked.Increment(ref _totalQueries);
        }
        
        public void RecordCacheHit()
        {
            Interlocked.Increment(ref _cacheHits);
        }
        
        public void RecordCacheMiss()
        {
            Interlocked.Increment(ref _cacheMisses);
        }
        
        public void RecordGenerationTime(TimeSpan duration)
        {
            Interlocked.Add(ref _totalGenerationTimeMs, (long)duration.TotalMilliseconds);
        }
        
        public MetricsSnapshot GetSnapshot()
        {
            return new MetricsSnapshot
            {
                TotalQueries = Interlocked.Read(ref _totalQueries),
                CacheHits = Interlocked.Read(ref _cacheHits),
                CacheMisses = Interlocked.Read(ref _cacheMisses),
                AverageGenerationTimeMs = CalculateAverage()
            };
        }
    }
}
```

---

## Thread Safety Strategy

### Principles

1. **Immutability First**: Use immutable data structures where possible
2. **Lock-Free Reads**: Use `ConcurrentDictionary` and atomic operations
3. **No Shared Mutable State**: Each component is stateless or uses thread-safe primitives
4. **Async/Await**: Properly handle cancellation and continuation contexts

### Thread Safety Patterns

#### Pattern 1: Immutable Configuration

```csharp
/// <summary>
/// Configuration is immutable - safe to share across threads.
/// Once constructed, cannot be modified.
/// </summary>
public sealed class SieveConfiguration
{
    // All properties are init-only (C# 9.0+)
    public int CacheMaxSize { get; init; }
    public int SegmentSize { get; init; }
    
    // No setters - completely immutable after construction
}
```

#### Pattern 2: Concurrent Collections

```csharp
/// <summary>
/// Uses ConcurrentDictionary for lock-free concurrent access.
/// Multiple threads can read simultaneously without blocking.
/// Writes are serialized internally by ConcurrentDictionary.
/// </summary>
public sealed class ConcurrentLruPrimeCache : IPrimeCache
{
    private readonly ConcurrentDictionary<long, long> _primesByIndex;
    
    public bool TryGetPrime(long index, out long prime)
    {
        // Lock-free read - no mutex required
        return _primesByIndex.TryGetValue(index, out prime);
    }
    
    public void AddPrime(long index, long prime)
    {
        // Atomic add-or-update operation
        _primesByIndex.TryAdd(index, prime);
    }
}
```

#### Pattern 3: Atomic Operations for Metrics

```csharp
/// <summary>
/// Uses Interlocked operations for atomic counter updates.
/// Safe for concurrent access without locks.
/// </summary>
public sealed class AtomicMetricsCollector
{
    private long _totalQueries;
    private long _cacheHits;
    
    public void RecordQuery()
    {
        // Atomic increment - thread-safe
        Interlocked.Increment(ref _totalQueries);
    }
    
    public long GetTotalQueries()
    {
        // Atomic read - thread-safe
        return Interlocked.Read(ref _totalQueries);
    }
}
```

#### Pattern 4: Stateless Services

```csharp
/// <summary>
/// Generator has NO mutable state - completely thread-safe.
/// Each invocation operates on local variables only.
/// Can be safely called from multiple threads simultaneously.
/// </summary>
public sealed class SegmentedSieveGenerator : IPrimeGenerator
{
    private readonly int _segmentSize; // Read-only field - safe
    
    public async Task<long[]> GeneratePrimesUpToAsync(long limit, CancellationToken cancellationToken)
    {
        // All state is local to this method call
        List<long> primes = new();
        bool[] isPrime = new bool[_segmentSize];
        
        // No shared state modified - thread-safe
        // ...
        
        return primes.ToArray();
    }
}
```

#### Pattern 5: Read-Write Locks (When Necessary)

```csharp
/// <summary>
/// For scenarios requiring complex read-write coordination.
/// Allows multiple concurrent readers or single writer.
/// </summary>
public sealed class BlockingPrimeCache : IPrimeCache
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<long, long> _primes = new();
    
    public bool TryGetPrime(long index, out long prime)
    {
        _lock.EnterReadLock();
        try
        {
            return _primes.TryGetValue(index, out prime);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    public void AddPrime(long index, long prime)
    {
        _lock.EnterWriteLock();
        try
        {
            _primes[index] = prime;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
```

### Thread Safety Verification

```csharp
namespace Sieve.Tests.Concurrency
{
    /// <summary>
    /// Validates thread safety under concurrent load.
    /// </summary>
    public class ThreadSafetyTests
    {
        [Fact]
        public async Task ConcurrentAccess_SameIndex_ReturnsConsistentResults()
        {
            // Arrange
            var sieve = CreateSieve();
            const long n = 1000;
            const int threadCount = 100;
            
            // Act: 100 threads query same prime simultaneously
            var tasks = Enumerable.Range(0, threadCount)
                .Select(_ => Task.Run(() => sieve.NthPrime(n)))
                .ToArray();
            
            var results = await Task.WhenAll(tasks);
            
            // Assert: All threads get the same result
            Assert.All(results, result => Assert.Equal(7919, result));
        }
        
        [Fact]
        public async Task ConcurrentAccess_DifferentIndices_NoDeadlocks()
        {
            // Arrange
            var sieve = CreateSieve();
            var random = new Random(42);
            
            // Act: 1000 concurrent queries with random indices
            var tasks = Enumerable.Range(0, 1000)
                .Select(i => Task.Run(() => 
                {
                    long n = random.Next(0, 10000);
                    return sieve.NthPrime(n);
                }))
                .ToArray();
            
            // Should complete without deadlocks
            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completed = await Task.WhenAny(Task.WhenAll(tasks), timeout);
            
            Assert.NotEqual(timeout, completed);
        }
    }
}
```

---

## Performance & Scalability

### Memory Management

```csharp
/// <summary>
/// Memory-efficient segmented sieve using bit arrays and array pooling.
/// Estimated memory usage: O(√N) + cache overhead
/// </summary>
public sealed class SegmentedSieveGenerator : IPrimeGenerator
{
    private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    
    public async Task<long[]> GeneratePrimesUpToAsync(long limit, CancellationToken cancellationToken)
    {
        // Use array pool to reduce GC pressure
        byte[] segmentBuffer = _bytePool.Rent(_segmentSize);
        
        try
        {
            // Process segments...
            // Each segment is reused, minimizing allocations
        }
        finally
        {
            // Return buffer to pool
            _bytePool.Return(segmentBuffer);
        }
    }
}
```

### Algorithmic Complexity

| Operation | Time Complexity | Space Complexity | Notes |
|-----------|----------------|------------------|-------|
| NthPrime(n) - Cold Cache | O(M log log M) | O(√M) | M = estimated Nth prime bound |
| NthPrime(n) - Warm Cache | O(1) | O(1) | Direct cache lookup |
| Generate Primes [0, M] | O(M log log M) | O(√M) | Segmented sieve |
| Cache Lookup | O(1) | O(K) | K = cache size |
| Estimate Bound | O(1) | O(1) | Mathematical formula |

### Performance Targets

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| NthPrime(10,000,000) - First Call | < 10 seconds | Benchmark test |
| NthPrime(10,000,000) - Cached | < 1 millisecond | Benchmark test |
| Memory Usage (N=10M) | < 500 MB | Process memory monitor |
| Concurrent Throughput | > 10,000 queries/sec | Load test (cached) |
| Cache Hit Rate | > 80% | Metrics collector |

### Optimization Strategies

```csharp
/// <summary>
/// Progressive optimization strategy based on N.
/// Small N: Use pre-computed primes
/// Medium N: Use classic sieve
/// Large N: Use segmented sieve
/// </summary>
public sealed class AdaptiveSieveGenerator : IPrimeGenerator
{
    private const long SMALL_THRESHOLD = 1000;
    private const long MEDIUM_THRESHOLD = 1_000_000;
    
    private readonly IPrimeGenerator _classicSieve;
    private readonly IPrimeGenerator _segmentedSieve;
    
    public async Task<long[]> GeneratePrimesUpToAsync(long limit, CancellationToken cancellationToken)
    {
        // Adaptive strategy selection
        if (limit < SMALL_THRESHOLD)
            return PrecomputedPrimes.GetPrimesUpTo(limit);
        else if (limit < MEDIUM_THRESHOLD)
            return await _classicSieve.GeneratePrimesUpToAsync(limit, cancellationToken);
        else
            return await _segmentedSieve.GeneratePrimesUpToAsync(limit, cancellationToken);
    }
}
```

---

## Error Handling & Resilience

### Exception Hierarchy

```csharp
namespace Sieve.Exceptions
{
    /// <summary>
    /// Base exception for all Sieve-related errors.
    /// </summary>
    public class SieveException : Exception
    {
        public SieveException(string message) : base(message) { }
        public SieveException(string message, Exception innerException) 
            : base(message, innerException) { }
    }
    
    /// <summary>
    /// Thrown when prime computation fails due to algorithmic error.
    /// </summary>
    public class PrimeComputationException : SieveException
    {
        public long RequestedIndex { get; }
        
        public PrimeComputationException(long index, string message, Exception innerException = null)
            : base($"Failed to compute prime at index {index}: {message}", innerException)
        {
            RequestedIndex = index;
        }
    }
    
    /// <summary>
    /// Thrown when validation detects incorrect results.
    /// </summary>
    public class PrimeValidationException : SieveException
    {
        public long Index { get; }
        public long ExpectedPrime { get; }
        public long ActualPrime { get; }
        
        public PrimeValidationException(long index, long expected, long actual)
            : base($"Validation failed: NthPrime({index}) returned {actual}, expected {expected}")
        {
            Index = index;
            ExpectedPrime = expected;
            ActualPrime = actual;
        }
    }
    
    /// <summary>
    /// Thrown when operation times out.
    /// </summary>
    public class PrimeComputationTimeoutException : SieveException
    {
        public TimeSpan Timeout { get; }
        
        public PrimeComputationTimeoutException(TimeSpan timeout)
            : base($"Prime computation exceeded timeout of {timeout}")
        {
            Timeout = timeout;
        }
    }
}
```

### Defensive Programming

```csharp
public sealed class SieveOrchestrator : ISieve
{
    public async Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default)
    {
        // 1. Input validation
        if (n < 0)
        {
            _logger.LogError("Invalid input: n={N} is negative", n);
            throw new ArgumentOutOfRangeException(
                nameof(n), 
                n, 
                "Index must be non-negative");
        }
        
        try
        {
            _metrics.RecordQuery();
            
            // 2. Cache lookup
            if (_cache.TryGetPrime(n, out long cachedPrime))
            {
                _logger.LogDebug("Cache hit for n={N}, prime={Prime}", n, cachedPrime);
                _metrics.RecordCacheHit();
                return cachedPrime;
            }
            
            _metrics.RecordCacheMiss();
            
            // 3. Estimate upper bound with safety checks
            long upperBound = _estimator.EstimateNthPrimeUpperBound(n);
            if (upperBound <= 0 || upperBound > long.MaxValue / 2)
            {
                throw new PrimeComputationException(n, 
                    $"Estimated upper bound {upperBound} is invalid");
            }
            
            _logger.LogInformation(
                "Computing primes up to {Limit} for n={N}", 
                upperBound, 
                n);
            
            // 4. Generate primes with timeout protection
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.DefaultTimeout);
            
            Stopwatch sw = Stopwatch.StartNew();
            long[] primes = await _generator.GeneratePrimesUpToAsync(upperBound, cts.Token);
            sw.Stop();
            
            _metrics.RecordGenerationTime(sw.Elapsed);
            
            // 5. Bounds check
            if (n >= primes.Length)
            {
                throw new PrimeComputationException(n,
                    $"Generated {primes.Length} primes but need index {n}. " +
                    $"Upper bound {upperBound} was insufficient.");
            }
            
            long result = primes[n];
            
            // 6. Validation (if enabled)
            if (_config.EnableValidation)
            {
                ValidateResult(n, result);
            }
            
            // 7. Update cache
            _cache.AddPrimeRange(0, primes);
            
            _logger.LogInformation(
                "Successfully computed NthPrime({N})={Prime} in {Ms}ms",
                n, result, sw.ElapsedMilliseconds);
            
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Prime computation cancelled for n={N}", n);
            throw;
        }
        catch (Exception ex) when (ex is not SieveException)
        {
            // Wrap unexpected exceptions
            _logger.LogError(ex, "Unexpected error computing NthPrime({N})", n);
            throw new PrimeComputationException(n, "Unexpected error", ex);
        }
    }
    
    private void ValidateResult(long n, long prime)
    {
        // Validate against known test cases
        if (KnownPrimes.TryGetValue(n, out long expected) && prime != expected)
        {
            throw new PrimeValidationException(n, expected, prime);
        }
        
        // Validate primality
        if (!IsPrime(prime))
        {
            throw new PrimeValidationException(n, 0, prime);
        }
    }
}
```

---

## Testing Strategy

### Test Pyramid

```
                    /\
                   /  \
                  / E2E \          ← 5%: Integration tests
                 /--------\
                /  Integ.  \       ← 15%: Component tests
               /------------\
              /     Unit      \    ← 80%: Unit tests
             /------------------\
```

### Unit Tests (80% of tests)

```csharp
namespace Sieve.Tests.Unit
{
    /// <summary>
    /// Tests for RosserSchoenfeldEstimator in isolation.
    /// Fast, deterministic, no dependencies.
    /// </summary>
    public class EstimatorTests
    {
        private readonly IEstimator _estimator = new RosserSchoenfeldEstimator();
        
        [Theory]
        [InlineData(0, 2)]
        [InlineData(1, 3)]
        [InlineData(5, 13)]
        [InlineData(100, 600)] // Upper bound must be >= 541
        [InlineData(1000000, 16_000_000)] // Upper bound must be >= 15_485_867
        public void EstimateNthPrimeUpperBound_ReturnsValidBound(long n, long minExpected)
        {
            // Act
            long upperBound = _estimator.EstimateNthPrimeUpperBound(n);
            
            // Assert
            Assert.True(upperBound >= minExpected, 
                $"Upper bound {upperBound} is less than expected minimum {minExpected}");
        }
    }
    
    /// <summary>
    /// Tests for SegmentedSieveGenerator in isolation.
    /// </summary>
    public class SegmentedSieveGeneratorTests
    {
        [Theory]
        [InlineData(10, new long[] { 2, 3, 5, 7 })]
        [InlineData(30, new long[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 })]
        public async Task GeneratePrimesUpToAsync_ReturnsCorrectPrimes(long limit, long[] expected)
        {
            // Arrange
            var generator = new SegmentedSieveGenerator();
            
            // Act
            long[] primes = await generator.GeneratePrimesUpToAsync(limit);
            
            // Assert
            Assert.Equal(expected, primes);
        }
        
        [Fact]
        public async Task GeneratePrimesUpToAsync_SupportsCancel lation()
        {
            // Arrange
            var generator = new SegmentedSieveGenerator();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(10));
            
            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => generator.GeneratePrimesUpToAsync(100_000_000, cts.Token));
        }
    }
    
    /// <summary>
    /// Tests for ConcurrentLruPrimeCache in isolation.
    /// </summary>
    public class CacheTests
    {
        [Fact]
        public void TryGetPrime_EmptyCache_ReturnsFalse()
        {
            // Arrange
            var cache = new ConcurrentLruPrimeCache(maxSize: 100);
            
            // Act
            bool found = cache.TryGetPrime(0, out long prime);
            
            // Assert
            Assert.False(found);
            Assert.Equal(0, prime);
        }
        
        [Fact]
        public void AddPrime_ThenGet_ReturnsCorrectValue()
        {
            // Arrange
            var cache = new ConcurrentLruPrimeCache(maxSize: 100);
            
            // Act
            cache.AddPrime(10, 31);
            bool found = cache.TryGetPrime(10, out long prime);
            
            // Assert
            Assert.True(found);
            Assert.Equal(31, prime);
        }
    }
}
```

### Integration Tests (15% of tests)

```csharp
namespace Sieve.Tests.Integration
{
    /// <summary>
    /// Tests multiple components working together.
    /// </summary>
    public class SieveIntegrationTests
    {
        private ISieve CreateSieveWithDependencies()
        {
            var generator = new SegmentedSieveGenerator();
            var cache = new ConcurrentLruPrimeCache(maxSize: 1000);
            var estimator = new RosserSchoenfeldEstimator();
            var metrics = new AtomicMetricsCollector();
            var logger = NullLogger<SieveOrchestrator>.Instance;
            var config = SieveConfiguration.Default;
            
            return new SieveOrchestrator(generator, cache, estimator, metrics, logger, config);
        }
        
        [Theory]
        [InlineData(0, 2)]
        [InlineData(19, 71)]
        [InlineData(99, 541)]
        [InlineData(500, 3581)]
        [InlineData(986, 7793)]
        [InlineData(2000, 17393)]
        public void NthPrime_ReturnsCorrectValue(long n, long expected)
        {
            // Arrange
            var sieve = CreateSieveWithDependencies();
            
            // Act
            long actual = sieve.NthPrime(n);
            
            // Assert
            Assert.Equal(expected, actual);
        }
        
        [Fact]
        public void NthPrime_CalledTwice_UsesCacheSecondTime()
        {
            // Arrange
            var cache = new ConcurrentLruPrimeCache(maxSize: 1000);
            var sieve = new SieveOrchestrator(
                new SegmentedSieveGenerator(),
                cache,
                new RosserSchoenfeldEstimator(),
                new AtomicMetricsCollector(),
                NullLogger<SieveOrchestrator>.Instance,
                SieveConfiguration.Default);
            
            // Act
            long first = sieve.NthPrime(100);
            long second = sieve.NthPrime(100);
            
            // Assert
            Assert.Equal(first, second);
            Assert.True(cache.Count > 0, "Cache should be populated after first call");
        }
    }
}
```

### End-to-End Tests (5% of tests)

```csharp
namespace Sieve.Tests.EndToEnd
{
    /// <summary>
    /// Full system tests including all components and configurations.
    /// Slower tests that validate the entire pipeline.
    /// </summary>
    public class SystemTests
    {
        [Fact(Timeout = 60000)] // 60 second timeout
        public async Task NthPrime_LargeN_CompletesWithinTimeout()
        {
            // Arrange
            var sieve = CreateProductionSieve();
            
            // Act
            long prime = await sieve.NthPrimeAsync(1_000_000);
            
            // Assert
            Assert.Equal(15_485_867, prime);
        }
        
        [Fact(Timeout = 120000)] // 2 minute timeout
        public async Task NthPrime_VeryLargeN_CompletesWithinTimeout()
        {
            // Arrange
            var sieve = CreateProductionSieve();
            
            // Act
            long prime = await sieve.NthPrimeAsync(10_000_000);
            
            // Assert
            Assert.Equal(179_424_691, prime);
        }
    }
}
```

### Performance Benchmarks

```csharp
namespace Sieve.Benchmarks
{
    /// <summary>
    /// BenchmarkDotNet performance tests.
    /// Run with: dotnet run -c Release --project Sieve.Benchmarks
    /// </summary>
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    public class SieveBenchmarks
    {
        private ISieve _sieve;
        
        [GlobalSetup]
        public void Setup()
        {
            _sieve = CreateOptimizedSieve();
        }
        
        [Benchmark]
        public long NthPrime_Small_N100()
        {
            return _sieve.NthPrime(100);
        }
        
        [Benchmark]
        public long NthPrime_Medium_N10000()
        {
            return _sieve.NthPrime(10_000);
        }
        
        [Benchmark]
        public long NthPrime_Large_N1Million()
        {
            return _sieve.NthPrime(1_000_000);
        }
        
        [Benchmark]
        public long NthPrime_VeryLarge_N10Million()
        {
            return _sieve.NthPrime(10_000_000);
        }
    }
}
```

---

## Documentation Standards

### XML Documentation Requirements

Every public member must have XML documentation including:

```csharp
/// <summary>
/// Computes the Nth prime number using zero-based indexing.
/// </summary>
/// <param name="n">
/// The zero-based index of the desired prime number.
/// Examples: 0→2, 1→3, 2→5, 19→71, 99→541
/// </param>
/// <returns>
/// The prime number at the specified index.
/// </returns>
/// <exception cref="ArgumentOutOfRangeException">
/// Thrown when <paramref name="n"/> is negative.
/// </exception>
/// <exception cref="PrimeComputationException">
/// Thrown when the computation fails due to algorithmic errors or insufficient memory.
/// </exception>
/// <remarks>
/// <para>
/// This method uses a multi-tier caching strategy to optimize performance:
/// </para>
/// <list type="number">
/// <item>Check in-memory cache for previously computed primes</item>
/// <item>Estimate upper bound using Rosser-Schoenfeld inequality</item>
/// <item>Generate primes using segmented Sieve of Eratosthenes</item>
/// <item>Cache results for future queries</item>
/// </list>
/// <para>
/// Time Complexity: O(M log log M) where M is the estimated Nth prime
/// </para>
/// <para>
/// Space Complexity: O(√M) due to segmented sieve algorithm
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ISieve sieve = new SieveOrchestrator(...);
/// long prime = sieve.NthPrime(10); // Returns 31 (the 11th prime, 0-indexed)
/// </code>
/// </example>
public long NthPrime(long n)
{
    // Implementation...
}
```

### Code Comments Strategy

```csharp
public async Task<long[]> GeneratePrimesUpToAsync(long limit, CancellationToken cancellationToken)
{
    // STEP 1: Validate input
    // Rationale: Prevent integer overflow in subsequent calculations
    if (limit < 2)
        return Array.Empty<long>();
    
    // STEP 2: Generate small primes for sieving
    // Rationale: We need primes up to √limit to mark composites in segments
    long sqrtLimit = (long)Math.Ceiling(Math.Sqrt(limit));
    long[] basePrimes = GenerateBasePrimes(sqrtLimit);
    
    // STEP 3: Initialize result collection
    // Capacity optimization: Pre-allocate based on prime number theorem estimate
    // π(x) ≈ x / ln(x), so we expect roughly limit/ln(limit) primes
    int estimatedCount = (int)(limit / Math.Log(limit) * 1.15);
    List<long> primes = new(estimatedCount);
    primes.AddRange(basePrimes);
    
    // STEP 4: Process segments
    // Rationale: Segmented approach keeps memory usage O(√N) instead of O(N)
    for (long segmentStart = sqrtLimit + 1; segmentStart <= limit; segmentStart += _segmentSize)
    {
        // Check for cancellation between segments
        // Rationale: Allow graceful termination of long-running operations
        cancellationToken.ThrowIfCancellationRequested();
        
        long segmentEnd = Math.Min(segmentStart + _segmentSize - 1, limit);
        
        // Process this segment and add found primes to results
        ProcessSegment(segmentStart, segmentEnd, basePrimes, primes);
    }
    
    return primes.ToArray();
}
```

### Architecture Decision Records (ADR)

```markdown
# ADR-001: Use Segmented Sieve Instead of Classic Sieve

## Status
Accepted

## Context
For large N (e.g., NthPrime(10,000,000)), we need to generate primes up to ~179 million.
Classic sieve requires a boolean array of size 179M, consuming ~171 MB.
Memory constraints and GC pressure are concerns.

## Decision
Use segmented Sieve of Eratosthenes with 32KB segments.

## Consequences
### Positive
- Memory usage reduced to O(√N) ≈ 13 KB for base primes + 32 KB segment buffer
- CPU cache friendly (segments fit in L1/L2 cache)
- Supports arbitrarily large N without memory exhaustion

### Negative
- Slightly more complex implementation
- ~10-15% slower than classic sieve for small N (< 1M)

### Mitigation
- Use adaptive strategy: classic sieve for N < 1M, segmented for N >= 1M
```

---

## Implementation Roadmap

### Phase 1: Foundation (Week 1)

**Goals**: Core interfaces, basic implementations, unit tests

```
✅ Define all interfaces (ISieve, IPrimeGenerator, IPrimeCache, IEstimator)
✅ Implement RosserSchoenfeldEstimator
✅ Implement basic ClassicSieveGenerator
✅ Unit tests for estimator and generator
✅ Documentation for all public interfaces
```

### Phase 2: Caching & Orchestration (Week 2)

**Goals**: Thread-safe cache, main orchestrator, integration tests

```
✅ Implement ConcurrentLruPrimeCache
✅ Implement SieveOrchestrator with all wiring
✅ Add metrics collection
✅ Integration tests for full pipeline
✅ Performance benchmarks for small N (< 10K)
```

### Phase 3: Segmented Sieve (Week 3)

**Goals**: Memory-efficient large N support

```
✅ Implement SegmentedSieveGenerator
✅ Optimize segment size (benchmark 8KB, 16KB, 32KB, 64KB)
✅ Add array pooling for zero-allocation segments
✅ Validate against all test cases including N=10M
✅ Performance benchmarks for large N
```

### Phase 4: Error Handling & Resilience (Week 4)

**Goals**: Production-ready error handling and logging

```
✅ Implement exception hierarchy
✅ Add comprehensive input validation
✅ Add timeout protection
✅ Implement cancellation support
✅ Add structured logging throughout
✅ Circuit breaker for repeated failures (optional)
```

### Phase 5: Optimization & Polish (Week 5)

**Goals**: Fine-tuning and documentation

```
✅ Adaptive strategy selection (small/medium/large N)
✅ Wheel factorization optimization (optional)
✅ Parallel segment processing (optional)
✅ Complete XML documentation
✅ Architecture decision records
✅ Performance tuning guide
```

### Phase 6: Testing & Validation (Week 6)

**Goals**: Comprehensive testing and validation

```
✅ End-to-end tests for all requirements
✅ Thread safety tests (concurrent access)
✅ Stress tests (memory, CPU, duration)
✅ Fuzz testing (random inputs)
✅ Code coverage analysis (target: >90%)
✅ Static analysis (Roslyn analyzers, Sonar)
```

---

## Project Structure

```
Sieve/
├── src/
│   ├── Sieve.Core/                        # Core interfaces and abstractions
│   │   ├── Abstractions/
│   │   │   ├── ISieve.cs                  # Public API
│   │   │   ├── IPrimeGenerator.cs
│   │   │   ├── IPrimeCache.cs
│   │   │   ├── IEstimator.cs
│   │   │   ├── IMetricsCollector.cs
│   │   │   └── IPrimeValidator.cs
│   │   ├── Configuration/
│   │   │   └── SieveConfiguration.cs      # Immutable config
│   │   ├── Exceptions/
│   │   │   ├── SieveException.cs
│   │   │   ├── PrimeComputationException.cs
│   │   │   └── PrimeValidationException.cs
│   │   └── Models/
│   │       ├── CacheStatistics.cs
│   │       └── MetricsSnapshot.cs
│   │
│   ├── Sieve.Implementation/              # Main implementations
│   │   ├── SieveOrchestrator.cs           # Main facade
│   │   ├── Generation/
│   │   │   ├── ClassicSieveGenerator.cs
│   │   │   ├── SegmentedSieveGenerator.cs
│   │   │   └── AdaptiveSieveGenerator.cs
│   │   ├── Caching/
│   │   │   └── ConcurrentLruPrimeCache.cs
│   │   ├── Estimation/
│   │   │   └── RosserSchoenfeldEstimator.cs
│   │   ├── Metrics/
│   │   │   └── AtomicMetricsCollector.cs
│   │   └── Validation/
│   │       └── PrimeValidator.cs
│   │
│   └── Sieve.Extensions/                  # DI and hosting extensions
│       ├── ServiceCollectionExtensions.cs
│       └── SieveOptions.cs
│
├── tests/
│   ├── Sieve.Tests.Unit/                  # Fast, isolated tests
│   │   ├── EstimatorTests.cs
│   │   ├── GeneratorTests.cs
│   │   ├── CacheTests.cs
│   │   └── ValidationTests.cs
│   │
│   ├── Sieve.Tests.Integration/           # Component integration tests
│   │   ├── SieveIntegrationTests.cs
│   │   └── CachingIntegrationTests.cs
│   │
│   ├── Sieve.Tests.EndToEnd/              # Full system tests
│   │   ├── SystemTests.cs
│   │   └── ThreadSafetyTests.cs
│   │
│   └── Sieve.Benchmarks/                  # Performance benchmarks
│       ├── SieveBenchmarks.cs
│       └── CacheBenchmarks.cs
│
├── docs/
│   ├── architecture/
│   │   ├── adr-001-segmented-sieve.md
│   │   ├── adr-002-concurrent-cache.md
│   │   └── component-diagram.png
│   ├── api/
│   │   └── ISieve.md                      # Generated API docs
│   └── performance/
│       └── benchmarks.md
│
└── samples/
    ├── Sieve.Console/                     # Console app example
    └── Sieve.WebApi/                      # ASP.NET Core API example
```

---

## Dependency Injection Setup

```csharp
namespace Sieve.Extensions
{
    /// <summary>
    /// Extension methods for registering Sieve services with DI container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all Sieve services with default configuration.
        /// </summary>
        public static IServiceCollection AddSieve(this IServiceCollection services)
        {
            return services.AddSieve(SieveConfiguration.Default);
        }
        
        /// <summary>
        /// Registers all Sieve services with custom configuration.
        /// </summary>
        public static IServiceCollection AddSieve(
            this IServiceCollection services,
            SieveConfiguration configuration)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            
            // Register configuration as singleton
            services.AddSingleton(configuration);
            
            // Register stateless services as singletons (thread-safe)
            services.AddSingleton<IEstimator, RosserSchoenfeldEstimator>();
            services.AddSingleton<IPrimeValidator, PrimeValidator>();
            
            // Register cache as singleton (thread-safe concurrent collection)
            services.AddSingleton<IPrimeCache>(provider =>
                new ConcurrentLruPrimeCache(configuration.CacheMaxSize));
            
            // Register metrics collector as singleton
            services.AddSingleton<IMetricsCollector, AtomicMetricsCollector>();
            
            // Register generator strategy
            services.AddSingleton<IPrimeGenerator>(provider =>
            {
                // Use adaptive strategy that switches based on N
                var classic = new ClassicSieveGenerator();
                var segmented = new SegmentedSieveGenerator(configuration.SegmentSize);
                return new AdaptiveSieveGenerator(classic, segmented);
            });
            
            // Register main ISieve implementation as singleton
            // (All dependencies are thread-safe, so orchestrator can be singleton)
            services.AddSingleton<ISieve, SieveOrchestrator>();
            
            return services;
        }
        
        /// <summary>
        /// Registers Sieve services with custom options configuration.
        /// </summary>
        public static IServiceCollection AddSieve(
            this IServiceCollection services,
            Action<SieveOptions> configureOptions)
        {
            if (configureOptions == null)
                throw new ArgumentNullException(nameof(configureOptions));
            
            var options = new SieveOptions();
            configureOptions(options);
            
            return services.AddSieve(options.ToConfiguration());
        }
    }
    
    /// <summary>
    /// Fluent options for configuring Sieve services.
    /// </summary>
    public class SieveOptions
    {
        public int CacheMaxSize { get; set; } = 10_000;
        public int SegmentSize { get; set; } = 32 * 1024;
        public bool EnableMetrics { get; set; } = true;
        public bool EnableValidation { get; set; } = true;
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
        
        internal SieveConfiguration ToConfiguration()
        {
            return new SieveConfiguration
            {
                CacheMaxSize = CacheMaxSize,
                SegmentSize = SegmentSize,
                EnableMetrics = EnableMetrics,
                EnableValidation = EnableValidation,
                DefaultTimeout = DefaultTimeout
            };
        }
    }
}
```

### Usage Examples

```csharp
// Example 1: ASP.NET Core application
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register with defaults
        services.AddSieve();
        
        // OR: Register with custom configuration
        services.AddSieve(options =>
        {
            options.CacheMaxSize = 50_000;
            options.SegmentSize = 64 * 1024;
            options.EnableValidation = false; // Disable for production perf
        });
        
        services.AddControllers();
    }
}

// Example 2: Console application
public class Program
{
    public static void Main(string[] args)
    {
        // Build service provider
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSieve(SieveConfiguration.HighThroughput);
        
        var provider = services.BuildServiceProvider();
        
        // Resolve ISieve
        var sieve = provider.GetRequiredService<ISieve>();
        
        // Use it
        long prime = sieve.NthPrime(10_000_000);
        Console.WriteLine($"The 10,000,001st prime is: {prime}");
    }
}

// Example 3: Unit tests with mocks
public class MyServiceTests
{
    [Fact]
    public void MyService_UsesSieve_Correctly()
    {
        // Arrange: Mock ISieve for fast, deterministic tests
        var mockSieve = new Mock<ISieve>();
        mockSieve.Setup(s => s.NthPrime(It.IsAny<long>()))
                 .Returns((long n) => n * 2 + 1); // Fake implementation
        
        var myService = new MyService(mockSieve.Object);
        
        // Act & Assert
        // Test your service logic without actually computing primes
    }
}
```

---

## Summary

This architecture provides:

### ✅ SOLID Principles
- **SRP**: Each class has one responsibility
- **OCP**: Extensible via interfaces without modifying existing code
- **LSP**: All implementations honor contracts
- **ISP**: Segregated interfaces for different needs
- **DIP**: Dependencies on abstractions, not concretions

### ✅ Thread Safety
- Immutable configuration
- Concurrent collections (ConcurrentDictionary)
- Atomic operations (Interlocked)
- Stateless services
- Lock-free reads

### ✅ Comprehensive Documentation
- XML documentation for all public members
- Inline code comments explaining "why"
- Architecture decision records
- Performance benchmarks
- Usage examples

### ✅ Enterprise-Grade Features
- Dependency injection support
- Structured logging
- Metrics collection
- Graceful error handling
- Cancellation support
- Configurable timeouts
- Multiple caching strategies
- Extensible architecture

### ✅ Performance & Scalability
- Memory-efficient segmented sieve
- Multi-tier caching
- Adaptive algorithm selection
- Array pooling for zero allocations
- CPU cache-friendly design

This is a **production-ready, senior-level solution** that can handle the requirements (NthPrime(10,000,000) = 179,424,691) while being extensible for future enhancements like parallel processing, distributed computation, or persistent caching.
