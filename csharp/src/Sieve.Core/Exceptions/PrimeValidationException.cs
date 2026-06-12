namespace Sieve.Core.Exceptions;

/// <summary>
/// Exception raised when caller-supplied input violates the API contract.
///
/// <para>
/// Use this exception for deterministic, caller-fixable problems such as:
/// 1) negative prime indices — the prime sequence is defined only for n &gt;= 0;
/// 2) logically invalid ranges (for example endIndex &lt; startIndex);
/// 3) configuration values outside the supported bounds of the implementation.
/// </para>
///
/// <para>
/// Distinction from <see cref="PrimeComputationException"/>:
/// - <see cref="PrimeValidationException"/> signals a request-shape error that the
///   caller must fix before retrying. Retrying the same invalid request will always
///   produce the same exception.
/// - <see cref="PrimeComputationException"/> signals a runtime failure for an
///   otherwise valid request. The caller may legitimately retry.
/// </para>
///
/// <para>
/// In an HTTP API layer this distinction maps naturally: validation exceptions
/// typically translate to HTTP 400 Bad Request, while computation exceptions
/// may translate to HTTP 500 Internal Server Error or a retry with backoff.
/// </para>
/// </summary>
public class PrimeValidationException : SieveException
{
    /// <summary>
    /// Initializes a new <see cref="PrimeValidationException"/> with a description
    /// of the validation rule that was violated.
    /// </summary>
    /// <param name="message">
    /// A sentence describing which input constraint was violated and what value
    /// would be acceptable. For example: "Index must be non-negative; received -5."
    /// </param>
    public PrimeValidationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="PrimeValidationException"/> that wraps a
    /// lower-level exception that surfaced during validation.
    /// </summary>
    /// <param name="message">
    /// A sentence describing which input constraint was violated.
    /// </param>
    /// <param name="innerException">
    /// The underlying exception that provided evidence of the violation,
    /// if any. Preserving it keeps the full causal chain visible in logs.
    /// </param>
    public PrimeValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
