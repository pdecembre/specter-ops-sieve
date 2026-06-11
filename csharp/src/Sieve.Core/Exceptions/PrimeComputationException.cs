namespace Sieve.Core.Exceptions;

/// <summary>
/// Thrown when prime computation fails due to algorithm errors or resource constraints.
/// Includes the requested index for debugging purposes.
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
