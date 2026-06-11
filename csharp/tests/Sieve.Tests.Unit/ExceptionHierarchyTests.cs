using FluentAssertions;
using Sieve.Core.Exceptions;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

/// <summary>
/// Tests to verify the exception hierarchy is properly implemented.
/// Phase 1: Foundation - validates exception types and inheritance.
/// </summary>
public class ExceptionHierarchyTests : TestBase
{
    public ExceptionHierarchyTests(ITestOutputHelper output) : base(output)
    {
    }
    
    [Fact]
    public void SieveException_CanBeInstantiated()
    {
        // Arrange & Act
        var exception = new SieveException("Test message");
        
        // Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be("Test message");
        exception.Should().BeAssignableTo<Exception>();
        
        Output.WriteLine("✓ SieveException can be instantiated");
    }
    
    [Fact]
    public void SieveException_WithInnerException_PreservesInner()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner exception");
        
        // Act
        var exception = new SieveException("Outer message", inner);
        
        // Assert
        exception.InnerException.Should().BeSameAs(inner);
        exception.Message.Should().Be("Outer message");
        
        Output.WriteLine("✓ SieveException preserves inner exception");
    }
    
    [Fact]
    public void PrimeComputationException_InheritsFromSieveException()
    {
        // Arrange & Act
        var exception = new PrimeComputationException(1000, "Computation failed");
        
        // Assert
        exception.Should().BeAssignableTo<SieveException>();
        exception.RequestedIndex.Should().Be(1000);
        exception.Message.Should().Be("Computation failed");
        
        Output.WriteLine("✓ PrimeComputationException inherits from SieveException");
    }
    
    [Fact]
    public void PrimeComputationException_WithInnerException_PreservesInner()
    {
        // Arrange
        var inner = new OutOfMemoryException("Out of memory");
        
        // Act
        var exception = new PrimeComputationException(5000, "Failed to compute", inner);
        
        // Assert
        exception.InnerException.Should().BeSameAs(inner);
        exception.RequestedIndex.Should().Be(5000);
        
        Output.WriteLine("✓ PrimeComputationException preserves inner exception");
    }
    
    [Fact]
    public void PrimeValidationException_InheritsFromSieveException()
    {
        // Arrange & Act
        var exception = new PrimeValidationException("Invalid input");
        
        // Assert
        exception.Should().BeAssignableTo<SieveException>();
        exception.Message.Should().Be("Invalid input");
        
        Output.WriteLine("✓ PrimeValidationException inherits from SieveException");
    }
    
    [Fact]
    public void PrimeValidationException_WithInnerException_PreservesInner()
    {
        // Arrange
        var inner = new ArgumentException("Bad argument");
        
        // Act
        var exception = new PrimeValidationException("Validation failed", inner);
        
        // Assert
        exception.InnerException.Should().BeSameAs(inner);
        
        Output.WriteLine("✓ PrimeValidationException preserves inner exception");
    }
    
    [Fact]
    public void AllCustomExceptions_CanBeCaughtAsSieveException()
    {
        // Arrange
        var exceptions = new Exception[]
        {
            new SieveException("Base exception"),
            new PrimeComputationException(100, "Computation error"),
            new PrimeValidationException("Validation error")
        };
        
        // Act & Assert
        foreach (var exception in exceptions)
        {
            exception.Should().BeAssignableTo<SieveException>(
                $"{exception.GetType().Name} should be assignable to SieveException");
        }
        
        Output.WriteLine("✓ All custom exceptions can be caught as SieveException");
    }
    
    [Fact]
    public void ExceptionHierarchy_AllowsPolymorphicHandling()
    {
        // Arrange
        var caughtExceptions = new List<SieveException>();
        
        // Act - Throw and catch different exception types
        try
        {
            throw new PrimeComputationException(1000, "Test");
        }
        catch (SieveException ex)
        {
            caughtExceptions.Add(ex);
        }
        
        try
        {
            throw new PrimeValidationException("Test");
        }
        catch (SieveException ex)
        {
            caughtExceptions.Add(ex);
        }
        
        // Assert
        caughtExceptions.Should().HaveCount(2);
        caughtExceptions[0].Should().BeOfType<PrimeComputationException>();
        caughtExceptions[1].Should().BeOfType<PrimeValidationException>();
        
        Output.WriteLine("✓ Exception hierarchy supports polymorphic catch blocks");
    }
}
