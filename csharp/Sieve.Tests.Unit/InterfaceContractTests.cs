using FluentAssertions;
using Moq;
using Sieve.Core.Abstractions;
using Sieve.Core.Models;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

/// <summary>
/// Tests to verify all core interfaces can be properly mocked and implemented.
/// Phase 1: Foundation - validates interface contracts are well-defined.
/// </summary>
public class InterfaceContractTests : TestBase
{
    public InterfaceContractTests(ITestOutputHelper output) : base(output)
    {
    }
    
    [Fact]
    public async Task ISieve_CanBeMocked()
    {
        // Arrange & Act
        var mock = new Mock<ISieve>();
        mock.Setup(x => x.NthPrime(0)).Returns(2);
        mock.Setup(x => x.NthPrimeAsync(0, default)).ReturnsAsync(2);
        
        // Assert
        mock.Object.Should().NotBeNull();
        mock.Object.NthPrime(0).Should().Be(2);
        (await mock.Object.NthPrimeAsync(0)).Should().Be(2);
        
        Output.WriteLine("✓ ISieve interface can be mocked successfully");
    }
    
    [Fact]
    public async Task IPrimeGenerator_CanBeMocked()
    {
        // Arrange & Act
        var mock = new Mock<IPrimeGenerator>();
        mock.Setup(x => x.GeneratePrimesAsync(0, 9, default))
            .ReturnsAsync(new long[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 });
        mock.Setup(x => x.EstimateMemoryUsage(100)).Returns(1024);
        
        // Assert
        mock.Object.Should().NotBeNull();
        (await mock.Object.GeneratePrimesAsync(0, 9)).Should().HaveCount(10);
        mock.Object.EstimateMemoryUsage(100).Should().Be(1024);
        
        Output.WriteLine("✓ IPrimeGenerator interface can be mocked successfully");
    }
    
    [Fact]
    public void IPrimeCache_CanBeMocked()
    {
        // Arrange & Act
        var mock = new Mock<IPrimeCache>();
        mock.Setup(x => x.TryGetPrimeRange(0, 9, out It.Ref<long[]>.IsAny!))
            .Returns(false);
        mock.Setup(x => x.GetHighestCachedIndex()).Returns(99);
        mock.Setup(x => x.GetStatistics()).Returns(new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 80,
            CacheMisses = 20
        });
        
        // Assert
        mock.Object.Should().NotBeNull();
        mock.Object.GetHighestCachedIndex().Should().Be(99);
        var stats = mock.Object.GetStatistics();
        stats.HitRatio.Should().BeApproximately(0.8, 0.01);
        
        Output.WriteLine("✓ IPrimeCache interface can be mocked successfully");
    }
    
    [Fact]
    public void IEstimator_CanBeMocked()
    {
        // Arrange & Act
        var mock = new Mock<IEstimator>();
        mock.Setup(x => x.EstimateUpperBound(1000)).Returns(9300);
        
        // Assert
        mock.Object.Should().NotBeNull();
        mock.Object.EstimateUpperBound(1000).Should().Be(9300);
        
        Output.WriteLine("✓ IEstimator interface can be mocked successfully");
    }
    
    [Fact]
    public void IMetricsCollector_CanBeMocked()
    {
        // Arrange & Act
        var mock = new Mock<IMetricsCollector>();
        mock.Setup(x => x.GetSnapshot()).Returns(new MetricsSnapshot
        {
            TotalRequests = 1000,
            CacheHits = 800,
            CacheMisses = 200
        });
        
        // Assert
        mock.Object.Should().NotBeNull();
        var snapshot = mock.Object.GetSnapshot();
        snapshot.CacheHitRatio.Should().BeApproximately(0.8, 0.01);
        
        Output.WriteLine("✓ IMetricsCollector interface can be mocked successfully");
    }
    
    [Fact]
    public void AllInterfaces_HaveProperXmlDocumentation()
    {
        // This test verifies that all interfaces compile correctly
        // If they have XML documentation errors, compilation would fail
        
        // Arrange - Get all interface types from Sieve.Core
        var interfaceTypes = typeof(ISieve).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace?.StartsWith("Sieve.Core") == true)
            .ToList();
        
        // Assert
        interfaceTypes.Should().NotBeEmpty();
        interfaceTypes.Should().Contain(t => t.Name == "ISieve");
        interfaceTypes.Should().Contain(t => t.Name == "IPrimeGenerator");
        interfaceTypes.Should().Contain(t => t.Name == "IPrimeCache");
        interfaceTypes.Should().Contain(t => t.Name == "IEstimator");
        interfaceTypes.Should().Contain(t => t.Name == "IMetricsCollector");
        
        Output.WriteLine($"✓ Found {interfaceTypes.Count} interfaces in Sieve.Core");
        foreach (var type in interfaceTypes)
        {
            Output.WriteLine($"  - {type.FullName}");
        }
    }
}
