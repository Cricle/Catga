using Catga.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for ConcurrencyException.
/// </summary>
public class ConcurrencyExceptionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        // Arrange & Act
        var exception = new ConcurrencyException("stream-1", 5, 3);

        // Assert
        exception.StreamId.Should().Be("stream-1");
        exception.ExpectedVersion.Should().Be(5);
        exception.ActualVersion.Should().Be(3);
    }

    [Fact]
    public void Message_ContainsStreamIdAndVersions()
    {
        // Arrange & Act
        var exception = new ConcurrencyException("order-123", 10, 8);

        // Assert
        exception.Message.Should().Contain("order-123");
        exception.Message.Should().Contain("10");
        exception.Message.Should().Contain("8");
    }

    [Fact]
    public void Constructor_WithNegativeVersions_Works()
    {
        // Arrange & Act
        var exception = new ConcurrencyException("new-stream", -1, 0);

        // Assert
        exception.ExpectedVersion.Should().Be(-1);
        exception.ActualVersion.Should().Be(0);
    }

    [Fact]
    public void IsException_CanBeCaught()
    {
        // Arrange
        var exception = new ConcurrencyException("test", 1, 2);

        // Act & Assert
        Action act = () => throw exception;
        act.Should().Throw<ConcurrencyException>()
            .WithMessage("*Concurrency conflict*");
    }

    [Fact]
    public void InheritsFromException()
    {
        // Arrange
        var exception = new ConcurrencyException("test", 1, 2);

        // Assert
        exception.Should().BeAssignableTo<Exception>();
    }
}
