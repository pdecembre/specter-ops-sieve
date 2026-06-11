using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit.Infrastructure;

/// <summary>
/// Base class for all test classes providing common functionality.
/// Provides test output helper and mock logger integration.
/// </summary>
public abstract class TestBase : IDisposable
{
    /// <summary>
    /// Gets the xUnit test output helper for writing test output.
    /// </summary>
    protected ITestOutputHelper Output { get; }
    
    /// <summary>
    /// Gets a mock logger for capturing log messages in tests.
    /// </summary>
    protected Mock<ILogger> MockLogger { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="TestBase"/> class.
    /// </summary>
    /// <param name="output">The xUnit test output helper</param>
    protected TestBase(ITestOutputHelper output)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        MockLogger = new Mock<ILogger>();
        
        // Capture log messages to test output
        MockLogger.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback((LogLevel level, EventId eventId, object state, Exception? exception, Delegate formatter) =>
            {
                var message = formatter.DynamicInvoke(state, exception)?.ToString() ?? state.ToString();
                Output.WriteLine($"[{level}] {message}");
                if (exception != null)
                {
                    Output.WriteLine($"Exception: {exception}");
                }
            });
    }
    
    /// <summary>
    /// Disposes resources used by the test.
    /// </summary>
    public virtual void Dispose()
    {
        // Cleanup resources
        GC.SuppressFinalize(this);
    }
}
