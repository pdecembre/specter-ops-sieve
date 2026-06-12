namespace Sieve.Core.Exceptions;

/// <summary>
/// Exception raised when a prime-number computation could not be completed.
///
/// <para>
/// Typical scenarios that produce this exception:
/// 1) A generation algorithm fails unexpectedly (for example an arithmetic
///    overflow or an unexpected null result from an internal helper).
/// 2) The generated prime range does not contain the requested index, indicating
///    an internal contract violation between the orchestrator and generator.
/// 3) Resource pressure or platform limitations prevent the computation from
///    finishing (for example an <see cref="OutOfMemoryException"/> while
///    allocating a large sieve bitmap).
/// </para>
///
/// <para>
/// Distinction from <see cref="PrimeValidationException"/>:
/// - <see cref="PrimeValidationException"/> = the caller supplied input that the
///   API cannot accept (for example a negative index). The caller must fix their
///   request before retrying.
/// - <see cref="PrimeComputationException"/> = input was valid, but something
///   inside the computation pipeline failed. The caller may retry or degrade
///   gracefully; the failure is not their fault.
/// </para>
///
/// <para>
/// The <see cref="RequestedIndex"/> property is included so that logs and
/// telemetry can correlate the exception to the exact nth-prime request that
/// triggered it, without having to parse the message string.
/// </para>
/// </summary>
public class PrimeComputationException : SieveException
{
    /// <summary>
    /// Gets the 0-based prime index that was being computed when the failure
    /// occurred. Useful for correlating exception reports to specific requests.
    /// </summary>
    public long RequestedIndex { get; }

    /// <summary>
    /// Initializes a new <see cref="PrimeComputationException"/> for a failure
    /// that has no lower-level causing exception.
    /// </summary>
    /// <param name="requestedIndex">
    /// The 0-based prime index that was being computed when the failure occurred.
    /// </param>
    /// <param name="message">
    /// A sentence describing what went wrong during computation.
    /// </param>
    public PrimeComputationException(long requestedIndex, string message)
        : base(message)
    {
        RequestedIndex = requestedIndex;
    }

    /// <summary>
    /// Initializes a new <see cref="PrimeComputationException"/> that wraps a
    /// lower-level exception that caused the computation to fail.
    /// </summary>
    /// <param name="requestedIndex">
    /// The 0-based prime index that was being computed when the failure occurred.
    /// </param>
    /// <param name="message">
    /// A sentence describing what went wrong during computation.
    /// </param>
    /// <param name="innerException">
    /// The original exception from the generation pipeline (for example an
    /// <see cref="OutOfMemoryException"/> or <see cref="OverflowException"/>).
    /// Preserving it keeps the full causal chain visible in logs and debuggers.
    /// </param>
    public PrimeComputationException(long requestedIndex, string message, Exception innerException)
        : base(message, innerException)
    {
        RequestedIndex = requestedIndex;
    }
}
