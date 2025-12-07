using Catga.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for PersistentSubscription.
/// </summary>
public class PersistentSubscriptionTests
{
    [Fact]
    public void Constructor_SetsNameAndPattern()
    {
        // Act
        var sub = new PersistentSubscription("my-sub", "orders*");

        // Assert
        sub.Name.Should().Be("my-sub");
        sub.StreamPattern.Should().Be("orders*");
        sub.Position.Should().Be(-1);
        sub.ProcessedCount.Should().Be(0);
    }

    [Theory]
    [InlineData("*", "orders-1", true)]
    [InlineData("*", "customers-1", true)]
    [InlineData("orders*", "orders-1", true)]
    [InlineData("orders*", "orders-abc", true)]
    [InlineData("orders*", "customers-1", false)]
    [InlineData("orders-1", "orders-1", true)]
    [InlineData("orders-1", "orders-2", false)]
    public void MatchesStream_VariousPatterns(string pattern, string streamId, bool expected)
    {
        // Arrange
        var sub = new PersistentSubscription("test", pattern);

        // Act
        var result = sub.MatchesStream(streamId);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void MatchesEventType_EmptyFilter_MatchesAll()
    {
        // Arrange
        var sub = new PersistentSubscription("test", "*");

        // Act & Assert
        sub.MatchesEventType("OrderCreated").Should().BeTrue();
        sub.MatchesEventType("CustomerUpdated").Should().BeTrue();
        sub.MatchesEventType("AnyEvent").Should().BeTrue();
    }

    [Fact]
    public void MatchesEventType_WithFilter_MatchesOnlyListed()
    {
        // Arrange
        var sub = new PersistentSubscription("test", "*")
        {
            EventTypeFilter = ["OrderCreated", "OrderUpdated"]
        };

        // Act & Assert
        sub.MatchesEventType("OrderCreated").Should().BeTrue();
        sub.MatchesEventType("OrderUpdated").Should().BeTrue();
        sub.MatchesEventType("CustomerCreated").Should().BeFalse();
    }

    [Fact]
    public void UpdatePosition_UpdatesPositionAndTimestamp()
    {
        // Arrange
        var sub = new PersistentSubscription("test", "*");
        var beforeUpdate = DateTime.UtcNow;

        // Act
        sub.UpdatePosition(42);

        // Assert
        sub.Position.Should().Be(42);
        sub.LastProcessedAt.Should().NotBeNull();
        sub.LastProcessedAt!.Value.Should().BeOnOrAfter(beforeUpdate);
    }

    [Fact]
    public void CreatedAt_IsSetOnConstruction()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var sub = new PersistentSubscription("test", "*");

        // Assert
        sub.CreatedAt.Should().BeOnOrAfter(before);
        sub.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Order*", "OrderAggregate-123", true)]
    [InlineData("Order*", "OrderAggregate-abc", true)]
    [InlineData("Customer*", "OrderAggregate-123", false)]
    [InlineData("", "anything", false)]
    public void MatchesStream_PrefixPatterns(string pattern, string streamId, bool expected)
    {
        // Arrange
        var sub = new PersistentSubscription("test", pattern);

        // Act
        var result = sub.MatchesStream(streamId);

        // Assert
        result.Should().Be(expected);
    }
}
