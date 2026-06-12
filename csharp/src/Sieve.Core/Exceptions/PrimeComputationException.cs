namespace Sieve.Core.Exceptions;

/// <summary>
/// Exception raised when a prime-number computation could not be completed.
/// 
/// Typical scenarios:
/// 1) A generation algorithm fails unexpectedly.
/// 2) A computed range does not contain the requested index (internal contract violation).
/// 3) Resource pressure or other execution failures occur while generating primes.
/// 
/// This type is intentionally different from <see cref="PrimeValidationException"/>:
/// - validation exception = caller supplied invalid input.
/// - computation exception = input was valid, but the computation pipeline failed.
/// 
/// The <see cref="RequestedIndex"/> property is included so logs and telemetry can
/// correlate the failure to the exact nth-prime request that triggered it.
/// </summary>
public class PrimeComputationException : SieveException
{
    /// <summary>
    /// Gets the prime index that was requested when the error occurred.
    /// </summary>
    public long RequestedIndex { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PrimeComputationException"/> class.
    /// </summary>
    /// <param name="requestedIndex">The prime index that was being computed</param>
    /// <param name="message">The error message</param>
    public PrimeComputationException(long requestedIndex, string message) 
        : base(message)
    {
        RequestedIndex = requestedIndex;
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PrimeComputationException"/> class with an inner exception.
    /// </summary>
    /// <param name="requestedIndex">The prime index that was being computed</param>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception that caused the computation failure</param>
    public PrimeComputationException(long requestedIndex, string message, Exception innerException) 
        : base(message, innerException)
    {
        RequestedIndex = requestedIndex;
    }
}
