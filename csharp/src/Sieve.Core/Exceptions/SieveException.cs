namespace Sieve.Core.Exceptions;

/// <summary>
/// Base exception for all domain-specific failures in the Sieve subsystem.
///
/// <para>
/// Why this type exists:
/// 1) It gives callers one stable "catch boundary" for all sieve-originated faults.
/// 2) It distinguishes expected domain failures from unrelated runtime failures
///    such as <see cref="OutOfMemoryException"/>, <see cref="IOException"/>, or
///    infrastructure exceptions thrown by hosting frameworks.
/// 3) It enables layered exception handling policies:
///    - the application layer can catch <see cref="SieveException"/> for user-facing
///      error translation without knowing which concrete subtype was thrown;
///    - lower layers can throw more specific subclasses (for example,
///      <see cref="PrimeComputationException"/> or <see cref="PrimeValidationException"/>)
///      so that code that cares about the distinction can act on it.
/// </para>
///
/// <para>
/// Design note:
/// This class intentionally adds no mutable state beyond the message and inner
/// exception inherited from <see cref="Exception"/>. Subclasses may add context
/// properties (for example <see cref="PrimeComputationException.RequestedIndex"/>),
/// but the base remains lightweight and serialization-friendly.
/// </para>
/// </summary>
public class SieveException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="SieveException"/> with a human-readable
    /// description of what went wrong.
    /// </summary>
    /// <param name="message">
    /// A sentence describing the failure. This text is surfaced in logs,
    /// exception reports, and debugger displays, so it should be specific
    /// enough for a developer to diagnose the problem without a stack trace.
    /// </param>
    public SieveException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="SieveException"/> that wraps a lower-level
    /// exception that caused this failure.
    /// </summary>
    /// <param name="message">
    /// A sentence describing the failure at the Sieve domain level.
    /// </param>
    /// <param name="innerException">
    /// The original exception that caused this failure. Preserving the inner
    /// exception keeps the full causal chain visible in logs and debuggers.
    /// For example, if a memory allocation inside the generator throws
    /// <see cref="OutOfMemoryException"/>, that exception should become the
    /// inner exception of a <see cref="PrimeComputationException"/> so the
    /// root cause is not silently swallowed.
    /// </param>
    public SieveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
