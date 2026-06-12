namespace Sieve.Core.Exceptions;

/// <summary>
/// Exception raised when caller-supplied input violates the API contract.
/// 
/// Use this exception for deterministic, caller-fixable problems such as:
/// 1) negative prime indices,
/// 2) logically invalid ranges,
/// 3) configuration values outside supported bounds.
/// 
/// Design intent:
/// - distinguish request-shape failures from runtime computation failures
///   (<see cref="PrimeComputationException"/>),
/// - allow upstream handlers to map validation failures to client-visible
///   "bad request" style responses.
/// </summary>
public class PrimeValidationException : SieveException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrimeValidationException"/> class.
    /// </summary>
    /// <param name="message">The validation error message</param>
    public PrimeValidationException(string message) : base(message) 
    { 
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PrimeValidationException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The validation error message</param>
    /// <param name="innerException">The inner exception that caused the validation failure</param>
    public PrimeValidationException(string message, Exception innerException) 
        : base(message, innerException) 
    { 
    }
}
