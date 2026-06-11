namespace Sieve.Core.Exceptions;

/// <summary>
/// Thrown when input validation fails (e.g., negative indices, invalid parameters).
/// Indicates a problem with the request rather than the computation itself.
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
