using FluentAssertions;
using Sieve.Core.Models;
using Sieve.Tests.Unit.Infrastructure;
using Xunit.Abstractions;

namespace Sieve.Tests.Unit;

/// <summary>
/// Tests to verify model types (CacheStatistics, MetricsSnapshot) work correctly.
/// Phase 1: Foundation - validates immutable record types.
/// </summary>
public class ModelsTests : TestBase
{
    public ModelsTests(ITestOutputHelper output) : base(output)
    {
    }
    
    [Fact]
    public void CacheStatistics_CanBeInstantiated()
    {
        // Arrange & Act
        var stats = new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 80,
            CacheMisses = 20,
            EntriesCount = 50,
            MemoryUsageBytes = 1024 * 1024
        };
        
        // Assert
        stats.Should().NotBeNull();
        stats.TotalRequests.Should().Be(100);
        stats.CacheHits.Should().Be(80);
        stats.CacheMisses.Should().Be(20);
        stats.EntriesCount.Should().Be(50);
        stats.MemoryUsageBytes.Should().Be(1024 * 1024);
        
        Output.WriteLine("✓ CacheStatistics can be instantiated with init properties");
    }
    
    [Fact]
    public void CacheStatistics_HitRatio_CalculatesCorrectly()
    {
        // Arrange & Act
        var stats = new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 75,
            CacheMisses = 25
        };
        
        // Assert
        stats.HitRatio.Should().BeApproximately(0.75, 0.001);
        
        Output.WriteLine($"✓ CacheStatistics.HitRatio calculated correctly: {stats.HitRatio:P2}");
    }
    
    [Fact]
    public void CacheStatistics_HitRatio_ReturnsZeroWhenNoRequests()
    {
        // Arrange & Act
        var stats = new CacheStatistics
        {
            TotalRequests = 0,
            CacheHits = 0,
            CacheMisses = 0
        };
        
        // Assert
        stats.HitRatio.Should().Be(0.0);
        
        Output.WriteLine("✓ CacheStatistics.HitRatio returns 0 when TotalRequests is 0");
    }
    
    [Fact]
    public void CacheStatistics_IsImmutable()
    {
        // Arrange
        var stats1 = new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 80,
            CacheMisses = 20
        };
        
        // Act - Create modified copy using 'with' expression
        var stats2 = stats1 with { TotalRequests = 200 };
        
        // Assert
        stats1.TotalRequests.Should().Be(100, "original should be unchanged");
        stats2.TotalRequests.Should().Be(200, "copy should have new value");
        stats2.CacheHits.Should().Be(80, "other properties should be preserved");
        
        Output.WriteLine("✓ CacheStatistics is immutable (record type)");
    }
    
    [Fact]
    public void CacheStatistics_SupportsValueEquality()
    {
        // Arrange
        var stats1 = new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 80,
            CacheMisses = 20
        };
        
        var stats2 = new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 80,
            CacheMisses = 20
        };
        
        // Act & Assert
        stats1.Should().Be(stats2);
        (stats1 == stats2).Should().BeTrue();
        
        Output.WriteLine("✓ CacheStatistics supports value equality (record type)");
    }
    
    [Fact]
    public void MetricsSnapshot_CanBeInstantiated()
    {
        // Arrange & Act
        var snapshot = new MetricsSnapshot
        {
            TotalRequests = 1000,
            CacheHits = 800,
            CacheMisses = 200,
            GenerationCalls = 50,
            TotalPrimesGenerated = 50000
        };
        
        // Assert
        snapshot.Should().NotBeNull();
        snapshot.TotalRequests.Should().Be(1000);
        snapshot.CacheHits.Should().Be(800);
        snapshot.CacheMisses.Should().Be(200);
        snapshot.GenerationCalls.Should().Be(50);
        snapshot.TotalPrimesGenerated.Should().Be(50000);
        
        Output.WriteLine("✓ MetricsSnapshot can be instantiated with init properties");
    }
    
    [Fact]
    public void MetricsSnapshot_CacheHitRatio_CalculatesCorrectly()
    {
        // Arrange & Act
        var snapshot = new MetricsSnapshot
        {
            TotalRequests = 1000,
            CacheHits = 850,
            CacheMisses = 150
        };
        
        // Assert
        snapshot.CacheHitRatio.Should().BeApproximately(0.85, 0.001);
        
        Output.WriteLine($"✓ MetricsSnapshot.CacheHitRatio calculated correctly: {snapshot.CacheHitRatio:P2}");
    }
    
    [Fact]
    public void MetricsSnapshot_CacheHitRatio_ReturnsZeroWhenNoRequests()
    {
        // Arrange & Act
        var snapshot = new MetricsSnapshot
        {
            TotalRequests = 0,
            CacheHits = 0,
            CacheMisses = 0
        };
        
        // Assert
        snapshot.CacheHitRatio.Should().Be(0.0);
        
        Output.WriteLine("✓ MetricsSnapshot.CacheHitRatio returns 0 when TotalRequests is 0");
    }
    
    [Fact]
    public void MetricsSnapshot_AveragePrimesPerGeneration_CalculatesCorrectly()
    {
        // Arrange & Act
        var snapshot = new MetricsSnapshot
        {
            GenerationCalls = 100,
            TotalPrimesGenerated = 150000
        };
        
        // Assert
        snapshot.AveragePrimesPerGeneration.Should().BeApproximately(1500.0, 0.1);
        
        Output.WriteLine($"✓ MetricsSnapshot.AveragePrimesPerGeneration: {snapshot.AveragePrimesPerGeneration:N1}");
    }
    
    [Fact]
    public void MetricsSnapshot_AveragePrimesPerGeneration_ReturnsZeroWhenNoGenerations()
    {
        // Arrange & Act
        var snapshot = new MetricsSnapshot
        {
            GenerationCalls = 0,
            TotalPrimesGenerated = 0
        };
        
        // Assert
        snapshot.AveragePrimesPerGeneration.Should().Be(0.0);
        
        Output.WriteLine("✓ MetricsSnapshot.AveragePrimesPerGeneration returns 0 when no generations");
    }
    
    [Fact]
    public void MetricsSnapshot_IsImmutable()
    {
        // Arrange
        var snapshot1 = new MetricsSnapshot
        {
            TotalRequests = 1000,
            CacheHits = 800
        };
        
        // Act - Create modified copy
        var snapshot2 = snapshot1 with { TotalRequests = 2000 };
        
        // Assert
        snapshot1.TotalRequests.Should().Be(1000, "original should be unchanged");
        snapshot2.TotalRequests.Should().Be(2000, "copy should have new value");
        
        Output.WriteLine("✓ MetricsSnapshot is immutable (record type)");
    }
    
    [Fact]
    public void MetricsSnapshot_SupportsValueEquality()
    {
        // Arrange
        var snapshot1 = new MetricsSnapshot
        {
            TotalRequests = 1000,
            CacheHits = 800,
            CacheMisses = 200
        };
        
        var snapshot2 = new MetricsSnapshot
        {
            TotalRequests = 1000,
            CacheHits = 800,
            CacheMisses = 200
        };
        
        // Act & Assert
        snapshot1.Should().Be(snapshot2);
        (snapshot1 == snapshot2).Should().BeTrue();
        
        Output.WriteLine("✓ MetricsSnapshot supports value equality (record type)");
    }
}
