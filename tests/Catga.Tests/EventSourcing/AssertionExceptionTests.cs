using Catga.EventSourcing.Testing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for AssertionException.
/// </summary>
public class AssertionExceptionTests
{
    [Fact]
    public void Constructor_SetsMessage()
    {
        // Act
        var ex = new AssertionException("Test error message");

        // Assert
        ex.Message.Should().Be("Test error message");
    }

    [Fact]
    public void Constructor_IsException()
    {
        // Act
        var ex = new AssertionException("Test");

        // Assert
        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void Throw_CanBeCaught()
    {
        // Act & Assert
        Action act = () => throw new AssertionException("Expected failure");
        act.Should().Throw<AssertionException>().WithMessage("Expected failure");
    }
}
