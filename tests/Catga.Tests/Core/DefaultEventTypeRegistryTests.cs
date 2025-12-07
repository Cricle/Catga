using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for DefaultEventTypeRegistry.
/// </summary>
public class DefaultEventTypeRegistryTests
{
    [Fact]
    public void Register_AddsEventType()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act
        registry.Register("TestEvent", typeof(TestEvent));

        // Assert
        var resolved = registry.Resolve("TestEvent");
        resolved.Should().Be(typeof(TestEvent));
    }

    [Fact]
    public void Resolve_UnregisteredType_ReturnsNull()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act
        var result = registry.Resolve("NonExistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_EmptyTypeName_ReturnsNull()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act
        var result = registry.Resolve("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NullTypeName_ReturnsNull()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act
        var result = registry.Resolve(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Register_EmptyTypeName_DoesNotThrow()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act & Assert - should not throw
        registry.Register("", typeof(TestEvent));
        registry.Register(null!, typeof(TestEvent));
    }

    [Fact]
    public void Register_MultipleTypes_AllResolvable()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act
        registry.Register("Event1", typeof(TestEvent));
        registry.Register("Event2", typeof(AnotherEvent));

        // Assert
        registry.Resolve("Event1").Should().Be(typeof(TestEvent));
        registry.Resolve("Event2").Should().Be(typeof(AnotherEvent));
    }

    [Fact]
    public void Register_SameNameTwice_OverwritesPrevious()
    {
        // Arrange
        var registry = new DefaultEventTypeRegistry();

        // Act
        registry.Register("Event", typeof(TestEvent));
        registry.Register("Event", typeof(AnotherEvent));

        // Assert
        registry.Resolve("Event").Should().Be(typeof(AnotherEvent));
    }

    private record TestEvent : IEvent
    {
        public long MessageId { get; init; }
    }

    private record AnotherEvent : IEvent
    {
        public long MessageId { get; init; }
    }
}
