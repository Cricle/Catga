using Catga.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for ProjectionCheckpoint.
/// </summary>
public class ProjectionCheckpointTests
{
    [Fact]
    public void Constructor_SetsRequiredProperties()
    {
        // Act
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = "TestProjection"
        };

        // Assert
        checkpoint.ProjectionName.Should().Be("TestProjection");
    }

    [Fact]
    public void Position_CanBeSetAndGet()
    {
        // Arrange
        var checkpoint = new ProjectionCheckpoint { ProjectionName = "Test" };

        // Act
        checkpoint.Position = 100;

        // Assert
        checkpoint.Position.Should().Be(100);
    }

    [Fact]
    public void LastUpdated_CanBeSetAndGet()
    {
        // Arrange
        var checkpoint = new ProjectionCheckpoint { ProjectionName = "Test" };
        var now = DateTime.UtcNow;

        // Act
        checkpoint.LastUpdated = now;

        // Assert
        checkpoint.LastUpdated.Should().Be(now);
    }

    [Fact]
    public void StreamId_IsOptional()
    {
        // Act
        var checkpoint = new ProjectionCheckpoint { ProjectionName = "Test" };

        // Assert
        checkpoint.StreamId.Should().BeNull();
    }

    [Fact]
    public void StreamId_CanBeSet()
    {
        // Act
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = "Test",
            StreamId = "stream-123"
        };

        // Assert
        checkpoint.StreamId.Should().Be("stream-123");
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = "OrderProjection",
            Position = 500,
            LastUpdated = now,
            StreamId = "Order-123"
        };

        // Assert
        checkpoint.ProjectionName.Should().Be("OrderProjection");
        checkpoint.Position.Should().Be(500);
        checkpoint.LastUpdated.Should().Be(now);
        checkpoint.StreamId.Should().Be("Order-123");
    }

    [Fact]
    public void Position_DefaultsToZero()
    {
        // Act
        var checkpoint = new ProjectionCheckpoint { ProjectionName = "Test" };

        // Assert
        checkpoint.Position.Should().Be(0);
    }

    [Fact]
    public void LastUpdated_DefaultsToMinValue()
    {
        // Act
        var checkpoint = new ProjectionCheckpoint { ProjectionName = "Test" };

        // Assert
        checkpoint.LastUpdated.Should().Be(default(DateTime));
    }
}
