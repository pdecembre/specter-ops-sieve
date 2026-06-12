using System.Threading;
using Sieve.Core.Abstractions;
using Sieve.Core.Models;

namespace Sieve.Implementation.Metrics;

/// <summary>
/// Thread-safe metrics collector based on Interlocked atomic operations.
///
/// <para>
/// PURPOSE:
/// This class accumulates simple numeric counters (request counts, cache hit/miss
/// counts, generation call counts, and total primes generated) that may be updated
/// concurrently from many threads at once -- for example, multiple web request
/// handlers calling RecordRequest() simultaneously, or a background cache-warming
/// task calling RecordGeneration() while requests are being served.
/// </para>
///
/// <para>
/// WHY "INTERLOCKED" INSTEAD OF A LOCK:
/// A simpler way to make these counters thread-safe would be to wrap every read
/// and write in a `lock (someObject) { ... }` block. That works, but locks are
/// relatively heavyweight: even an uncontended lock involves a kernel-aware
/// synchronization primitive, and under contention threads can be suspended and
/// rescheduled by the OS. For something as small and frequent as "add 1 to a
/// counter," that overhead is wasteful.
///
/// The Interlocked class instead uses CPU-level atomic instructions (on x86/x64,
/// things like `lock xadd`) that the processor itself guarantees cannot be
/// interrupted or interleaved with another core's access to the same memory
/// location. No thread is ever suspended, no kernel call happens, and there is
/// no possibility of two threads "stepping on" the same increment. This makes
/// Interlocked operations dramatically cheaper than locks for simple counter
/// updates, while still being completely safe under concurrency.
/// </para>
///
/// <para>
/// THREAD-SAFETY GUARANTEE:
/// Every individual counter (_totalRequests, _cacheHits, etc.) is independently
/// safe to increment from any number of threads at any time. However, this class
/// does NOT guarantee that a snapshot returned by GetSnapshot() represents a
/// perfectly consistent "moment in time" across all five counters together --
/// see the remarks on GetSnapshot() below for why, and why that's acceptable for
/// metrics.
/// </para>
/// </summary>
public sealed class AtomicMetricsCollector : IMetricsCollector
{
    // Backing fields for each counter.
    //
    // IMPORTANT: every field here is a 'long' (Int64), not an 'int'. This is
    // intentional and required:
    //   1. Interlocked.Read(ref long) only has an overload for 'long' -- there is
    //      no Interlocked.Read for 'int', because on 64-bit platforms a 32-bit
    //      read is already guaranteed atomic by the hardware, but a 64-bit read
    //      is NOT guaranteed atomic without Interlocked.Read (a torn read could
    //      observe half-old/half-new bytes on some platforms).
    //   2. Using 'long' from the start avoids ever needing to widen the type
    //      later if counts grow large (a busy service can rack up billions of
    //      requests over its lifetime; 'int' would overflow at ~2.1 billion).
    //
    // None of these fields are marked 'readonly' because Interlocked operations
    // mutate them in place via 'ref' -- they must remain mutable instance fields.
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _generationCalls;
    private long _totalPrimesGenerated;

    /// <summary>
    /// Records that one request was received/processed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HOW Interlocked.Increment WORKS:
    /// `Interlocked.Increment(ref _totalRequests)` performs the equivalent of
    /// `_totalRequests = _totalRequests + 1`, but as a single, indivisible
    /// hardware operation. Under the hood on x86/x64 this typically compiles to
    /// a `lock xadd` (or `lock inc`) CPU instruction -- the `lock` prefix here is
    /// a CPU-level bus/cache lock, completely unrelated to C#'s `lock` keyword.
    /// It tells the processor: "no other core may read or write this memory
    /// address while this instruction is executing."
    /// </para>
    ///
    /// <para>
    /// WHY THIS IS NEEDED -- THE RACE CONDITION IT PREVENTS:
    /// Without Interlocked, `_totalRequests++` (or `_totalRequests = _totalRequests + 1`)
    /// is actually THREE separate steps at the machine level:
    ///   1. Read the current value of _totalRequests from memory into a register.
    ///   2. Add 1 to the value in the register.
    ///   3. Write the register's new value back to _totalRequests in memory.
    ///
    /// If two threads run these three steps concurrently, they can interleave like this:
    ///   Thread A: reads _totalRequests = 100
    ///   Thread B: reads _totalRequests = 100   (B reads BEFORE A writes back)
    ///   Thread A: computes 100 + 1 = 101, writes 101
    ///   Thread B: computes 100 + 1 = 101, writes 101
    ///
    /// Two increments happened, but the counter only went from 100 to 101 -- one
    /// increment was silently lost. This is called a "lost update" race condition,
    /// and it's notoriously hard to debug because it only manifests under real
    /// concurrent load, not in single-threaded testing.
    ///
    /// Interlocked.Increment collapses all three steps (read, add, write-back)
    /// into one atomic unit, so this interleaving is physically impossible -- if
    /// Thread A's increment is "in flight," Thread B's increment must wait (at
    /// the hardware level) until A's is fully complete, and vice versa. No
    /// updates are ever lost, no matter how many threads call this concurrently.
    /// </para>
    ///
    /// <para>
    /// RETURN VALUE: Interlocked.Increment returns the new (post-increment) value
    /// of the field. This implementation discards that return value -- it's only
    /// used for its side effect here -- but it's available if a caller ever
    /// needed "what is the count right after my increment" (e.g., to detect
    /// "I was the request that pushed the total over some threshold").
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public void RecordRequest() => Interlocked.Increment(ref _totalRequests);

    /// <summary>
    /// Records one cache hit (a lookup that found existing data and avoided
    /// regenerating it).
    /// </summary>
    /// <remarks>
    /// Same atomic increment mechanism as <see cref="RecordRequest"/> -- see that
    /// method's remarks for the full explanation of why Interlocked.Increment is
    /// required here.
    /// </remarks>
    /// <inheritdoc />
    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);

    /// <summary>
    /// Records one cache miss (a lookup that did NOT find existing data and
    /// triggered a fresh generation).
    /// </summary>
    /// <remarks>
    /// Same atomic increment mechanism as <see cref="RecordRequest"/>.
    ///
    /// Note that RecordCacheMiss and RecordGeneration are typically called
    /// together (a miss usually triggers a generation), but they are tracked as
    /// separate counters and updated independently. This is fine: each
    /// individual counter is independently consistent, even if the *combination*
    /// of "misses so far" and "generations so far" might momentarily differ by
    /// one if read mid-flight by GetSnapshot() (see GetSnapshot remarks).
    /// </remarks>
    /// <inheritdoc />
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    /// <summary>
    /// Records that a prime-generation operation occurred, and how many primes
    /// it produced.
    /// </summary>
    /// <param name="primesGenerated">
    /// The number of primes produced by this generation call. Added to the
    /// running total of all primes ever generated.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method performs TWO separate atomic operations:
    ///   1. <c>Interlocked.Increment(ref _generationCalls)</c> -- bumps the count
    ///      of "how many times has generation been invoked" by exactly 1.
    ///   2. <c>Interlocked.Add(ref _totalPrimesGenerated, primesGenerated)</c> --
    ///      adds an arbitrary (caller-supplied) amount to the running total of
    ///      primes produced across all generations.
    /// </para>
    ///
    /// <para>
    /// Interlocked.Add works exactly like Interlocked.Increment, except it adds a
    /// caller-specified value instead of a hardcoded 1. It's the same atomic
    /// read-modify-write guarantee, generalized: "add X to this field, with no
    /// possibility of a lost update from another thread doing the same."
    /// </para>
    ///
    /// <para>
    /// IMPORTANT CAVEAT -- THESE TWO OPERATIONS ARE NOT A SINGLE ATOMIC UNIT:
    /// Each Interlocked call is atomic *individually*, but the pair of them
    /// together is not. It is possible for another thread, calling GetSnapshot()
    /// at exactly the wrong moment, to observe the updated _generationCalls but
    /// the OLD (pre-update) _totalPrimesGenerated, or vice versa. In other words:
    /// there's a tiny window where the two counters are momentarily "out of sync"
    /// relative to each other.
    ///
    /// For this metrics use case, that's an acceptable trade-off: these are
    /// approximate, eventually-consistent counters for observability/dashboards,
    /// not values used for correctness-critical business logic (e.g., billing or
    /// inventory). If you needed the two values to always be read together as a
    /// perfectly consistent pair, you would need a lock around both fields (or a
    /// single struct updated via a compare-and-swap loop), which would reintroduce
    /// the heavier synchronization cost that Interlocked was chosen to avoid.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public void RecordGeneration(long primesGenerated)
    {
        // Atomically bump the "how many generation calls happened" counter by 1.
        Interlocked.Increment(ref _generationCalls);

        // Atomically add this call's prime count to the running grand total.
        // Unlike Increment (which always adds exactly 1), Add lets us add an
        // arbitrary value -- here, however many primes this specific call produced.
        Interlocked.Add(ref _totalPrimesGenerated, primesGenerated);
    }

    /// <summary>
    /// Returns a point-in-time copy of all current metric values.
    /// </summary>
    /// <returns>
    /// A <see cref="MetricsSnapshot"/> containing the current value of every
    /// counter at (approximately) the moment this method was called.
    /// </returns>
    /// <remarks>
    /// <para>
    /// HOW Interlocked.Read WORKS, AND WHY IT'S NEEDED FOR A 'long':
    /// On a 32-bit process (or certain architectures), reading or writing a
    /// 64-bit value (`long`) is not guaranteed to happen as a single CPU
    /// operation -- it can be split into two separate 32-bit reads/writes under
    /// the hood. If a writer thread is in the middle of updating a `long` field
    /// via Interlocked.Increment/Add (which IS atomic) at the exact moment a
    /// reader thread does a plain, non-atomic read of that field, the reader
    /// could observe a "torn" value: the high 32 bits from the new value and the
    /// low 32 bits from the old value (or vice versa) -- a number that was never
    /// actually written by anyone, and is meaningless.
    ///
    /// Interlocked.Read(ref long) guarantees the read itself happens as a single
    /// atomic operation, so it always returns a value that some writer actually
    /// produced -- never a torn mix of two different writes.
    ///
    /// (On modern 64-bit/x64 processes, aligned 64-bit reads/writes are in
    /// practice already atomic at the hardware level, so Interlocked.Read often
    /// compiles to essentially a plain read with a memory barrier. But using it
    /// explicitly keeps the code correct and portable regardless of platform, and
    /// makes the intent -- "this is part of a concurrently-mutated counter" --
    /// clear to anyone reading the code.)
    /// </para>
    ///
    /// <para>
    /// WHY THE SNAPSHOT AS A WHOLE IS NOT PERFECTLY CONSISTENT:
    /// Each of the five Interlocked.Read calls below is individually atomic and
    /// always returns a value that was genuinely written at some point. However,
    /// the five reads happen one after another, not simultaneously. Other
    /// threads can keep calling RecordRequest/RecordCacheHit/etc. *between* these
    /// reads. So it's entirely possible for, say, TotalRequests to reflect 1,000
    /// recorded requests while CacheHits + CacheMisses (read a few nanoseconds
    /// later) reflects only 999 of them -- the 1,000th request's cache
    /// hit/miss hadn't been recorded yet at the moment CacheHits/CacheMisses were
    /// read, even though it WAS recorded by the time TotalRequests was read.
    ///
    /// This is sometimes called "eventual consistency" or a "torn snapshot" at
    /// the aggregate level (note: this is a different, higher-level concept than
    /// the "torn read" of a single value that Interlocked.Read itself prevents).
    /// For dashboards, logging, and trend monitoring -- the typical consumers of
    /// a MetricsSnapshot -- being off by a handful of counts in a high-throughput
    /// system is completely acceptable, and is the standard trade-off made by
    /// virtually all lightweight metrics libraries (e.g., .NET's own
    /// EventCounters, Prometheus client libraries, etc.).
    /// </para>
    ///
    /// <para>
    /// WHY USE A COLLECTION-INITIALIZER ('new() { ... }') HERE:
    /// Using the target-typed `new()` with object-initializer syntax constructs
    /// the MetricsSnapshot and populates all five properties in one expression.
    /// Each property's value is computed by its own Interlocked.Read call at the
    /// moment that property is initialized -- the reads happen in the order the
    /// properties are listed, top to bottom, left to right.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public MetricsSnapshot GetSnapshot() => new()
    {
        // Each of these is an independent atomic 64-bit read -- guaranteed to
        // return a value that was genuinely written by some Increment/Add call,
        // never a torn/partial value. But because these five reads happen
        // sequentially rather than simultaneously, the combination of values in
        // this snapshot is only an approximate "moment in time" -- see remarks above.
        TotalRequests = Interlocked.Read(ref _totalRequests),
        CacheHits = Interlocked.Read(ref _cacheHits),
        CacheMisses = Interlocked.Read(ref _cacheMisses),
        GenerationCalls = Interlocked.Read(ref _generationCalls),
        TotalPrimesGenerated = Interlocked.Read(ref _totalPrimesGenerated)
    };
}