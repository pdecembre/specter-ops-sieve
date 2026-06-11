using Microsoft.Extensions.DependencyInjection;
using Sieve.Extensions;
using CoreSieve = Sieve.Core.Abstractions.ISieve;

namespace Sieve;

/// <summary>
/// Public API contract preserved for backwards compatibility with existing consumers/tests.
///
/// Note:
/// This facade mirrors the historical interface shape while internally delegating
/// to the more complete core abstraction in <c>Sieve.Core.Abstractions.ISieve</c>.
/// </summary>
public interface ISieve
{
    /// <summary>
    /// Returns the 0-based nth prime (for example: 0 -> 2, 1 -> 3).
    /// </summary>
    long NthPrime(long n);
}

/// <summary>
/// Factory for constructing a fully wired Sieve instance with default configuration.
/// </summary>
public static class SieveFactory
{
    /// <summary>
    /// Creates a new backwards-compatible sieve facade instance.
    /// </summary>
    public static ISieve Create()
    {
        return new SieveImplementation();
    }
}

/// <summary>
/// Backwards-compatible implementation that delegates to the core orchestrator.
/// </summary>
public sealed class SieveImplementation : ISieve
{
    private readonly CoreSieve _inner;

    /// <summary>
    /// Creates a Sieve instance backed by default dependency-injected services.
    /// </summary>
    public SieveImplementation()
        : this(BuildDefaultCoreSieve())
    {
    }

    internal SieveImplementation(CoreSieve inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public long NthPrime(long n)
    {
        return _inner.NthPrime(n);
    }

    private static CoreSieve BuildDefaultCoreSieve()
    {
        var services = new ServiceCollection();
        services.AddSieveServices();

        // Kept alive for process lifetime of this façade instance.
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<CoreSieve>();
    }
}