namespace Sieve.Core.Abstractions;

/// <summary>
/// Primary interface for retrieving the Nth prime number using 0-based indexing.
/// Thread-safe: Yes - all implementations must support concurrent access.
/// </summary>
public interface ISieve
{
    /// <summary>
    /// Retrieves the Nth prime number (0-indexed).
    /// </summary>
    /// <param name="n">Zero-based index (0 returns 2, 1 returns 3, etc.)</param>
    /// <returns>The Nth prime number</returns>
    /// <exception cref="ArgumentOutOfRangeException">If n &lt; 0</exception>
    /// <exception cref="Sieve.Core.Exceptions.PrimeComputationException">If computation fails</exception>
    long NthPrime(long n);
    
    /// <summary>
    /// Asynchronous version supporting cancellation.
    /// </summary>
    /// <param name="n">Zero-based index</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>The Nth prime number</returns>
    Task<long> NthPrimeAsync(long n, CancellationToken cancellationToken = default);
}
