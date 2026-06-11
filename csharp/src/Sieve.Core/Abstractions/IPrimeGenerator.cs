namespace Sieve.Core.Abstractions;

/// <summary>
/// Strategy interface for different prime generation algorithms.
/// Implements Strategy Pattern for algorithm selection.
/// Thread-safe: Implementations should be stateless and safe for concurrent use.
/// </summary>
public interface IPrimeGenerator
{
    /// <summary>
    /// Generates primes from startIndex to endIndex (inclusive, 0-based).
    /// </summary>
    /// <param name="startIndex">Starting index (0-based, where 0 is the first prime: 2)</param>
    /// <param name="endIndex">Ending index (0-based, inclusive)</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations</param>
    /// <returns>Array of primes in the specified range</returns>
    /// <exception cref="ArgumentOutOfRangeException">If startIndex &lt; 0 or endIndex &lt; startIndex</exception>
    /// <exception cref="OperationCanceledException">If operation is cancelled</exception>
    Task<long[]> GeneratePrimesAsync(
        long startIndex, 
        long endIndex, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Estimates memory usage in bytes for generating primes up to the given limit.
    /// Used for capacity planning and algorithm selection.
    /// </summary>
    /// <param name="limit">Upper bound for prime generation</param>
    /// <returns>Estimated memory usage in bytes</returns>
    long EstimateMemoryUsage(long limit);
}
