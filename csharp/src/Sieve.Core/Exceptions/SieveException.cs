namespace Sieve.Core.Exceptions;

/// <summary>
/// Base exception for all Sieve-related errors.
/// Provides a common base for exception handling and filtering.
/// </summary>
public class SieveException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SieveException"/> class.
    /// </summary>
    /// <param name="message">The error message</param>
    public SieveException(string message) : base(message) 
    { 
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="SieveException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception that caused this error</param>
    public SieveException(string message, Exception innerException) 
        : base(message, innerException) 
    { 
    }
}
