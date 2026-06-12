namespace Sieve.Core.Exceptions;

/// <summary>
/// Base exception for all domain-specific failures in the Sieve subsystem.
/// 
/// Why this type exists:
/// 1) It gives callers one stable "catch boundary" for all sieve-originated faults.
/// 2) It distinguishes expected domain failures from unrelated runtime failures
///    such as <see cref="OutOfMemoryException"/>, <see cref="IOException"/>, or
///    infrastructure exceptions thrown by hosting frameworks.
/// 3) It enables layered exception handling policies:
///    - application layer can catch <see cref="SieveException"/> for user-facing
///      error translation;
///    - lower layers can throw more specific subclasses (for example,
///      <see cref="PrimeComputationException"/> or <see cref="PrimeValidationException"/>).
/// 
/// Design note:
/// This class intentionally adds no mutable state. Subclasses may add context
/// properties, but the base remains lightweight and serialization-friendly.
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
