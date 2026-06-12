namespace Sieve.Core.Abstractions;

/// <summary>
/// The primary public contract for retrieving the Nth prime number.
///
/// <para>
/// INDEXING CONVENTION — 0-BASED:
/// This interface uses zero-based indexing, meaning index 0 refers to the
/// mathematically first prime number (2), index 1 to the second (3), and so on:
///
///   n=0  → 2
///   n=1  → 3
///   n=2  → 5
///   n=19 → 71
///   n=99 → 541
///
/// This matches typical array/list conventions in modern languages and avoids
/// confusion about whether "the 1st prime" means index 0 or index 1.
/// </para>
///
/// <para>
/// WHY TWO METHODS — SYNC AND ASYNC:
/// <see cref="NthPrime"/> is the simple, blocking overload for callers that do
/// not care about cancellation and are running in a synchronous context (for
/// example, console applications, unit tests, or legacy call sites).
///
/// <see cref="NthPrimeAsync"/> is the cooperative, cancellable overload suited
/// for server workloads: it allows the calling thread to be returned to the
/// thread pool while computation is in progress, and it allows long-running
/// generation work to be cancelled mid-flight via a <see cref="CancellationToken"/>.
///
/// Implementations are expected to implement the async method as the real
/// computation path and have the synchronous overload delegate to it via
/// GetAwaiter().GetResult().
/// </para>
///
/// <para>
/// THREAD-SAFETY CONTRACT:
/// All implementations of this interface MUST be safe to call concurrently from
/// multiple threads. This is a hard requirement because in a typical hosting
/// scenario many web requests will call NthPrime or NthPrimeAsync simultaneously.
/// An implementation that is not thread-safe would cause silent data corruption
/// or exceptions under load that may not reproduce in single-threaded testing.
/// </para>
///
/// <para>
/// EXCEPTION CONTRACT:
/// - <see cref="ArgumentOutOfRangeException"/> is thrown for negative n. This is
///   a deterministic, caller-fixable input error and should never be swallowed.
/// - <see cref="Exceptions.PrimeComputationException"/> is thrown when input was
///   valid but the computation pipeline failed. Callers may choose to retry,
///   degrade gracefully, or surface the error.
/// - <see cref="OperationCanceledException"/> propagates from the async path when
///   the provided <see cref="CancellationToken"/> is signalled.
/// </para>
/// </summary>
public interface ISieve
{
    /// <summary>
    /// Returns the Nth prime number using 0-based indexing.
    /// This is the synchronous, blocking form of the API.
    /// </summary>
    /// <param name="n">
    /// Zero-based prime index. Must be &gt;= 0.
    /// Index 0 returns 2, index 1 returns 3, index 19 returns 71, and so on.
    /// </param>
    /// <returns>
    /// The prime number at position <paramref name="n"/> in the natural
    /// ordering of primes (2, 3, 5, 7, 11, ...).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="n"/> is negative. Negative indices have no
    /// defined meaning in a 0-based prime sequence.
    /// </exception>
    /// <exception cref="Exceptions.PrimeComputationException">
    /// Thrown when the computation pipeline encounters an unexpected failure
    /// after accepting valid input.
    /// </exception>
    long NthPrime(long n);

    /// <summary>
    /// Returns the Nth prime number asynchronously, with cooperative cancellation support.
    /// This is the preferred overload for server and async-context callers.
    /// </summary>
    /// <param name="n">
    /// Zero-based prime index. Must be &gt;= 0.
    /// </param>
    /// <param name="cancellationToken">
    /// Token that signals the caller has lost interest in the result.
    /// Implementations should check this token periodically during long
    /// generation loops and throw <see cref="OperationCanceledException"/>
    /// when it is signalled, so callers can time out or abort cleanly.
    /// </param>
    /// <returns>
    /// A task that resolves to the prime number at position <paramref name="n"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="n"/> is negative.
    /// </exception>
    /// <exception cref="Exceptions.PrimeComputationException">
    /// Thrown when the computation pipeline fails after accepting valid input.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before
    /// the result is produced.
    /// </exception>
    Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default);
}
