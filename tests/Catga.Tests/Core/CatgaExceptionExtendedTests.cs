using Catga.Exceptions;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Extended unit tests for CatgaException
/// </summary>
public class CatgaExceptionExtendedTests
{
    [Fact]
    public void Constructor_WithMessage_ShouldSetMessage()
    {
        // Act
        var ex = new CatgaException("Test error");

        // Assert
        ex.Message.Should().Be("Test error");
    }

    [Fact]
    public void Constructor_WithMessageAndErrorCode_ShouldSetBoth()
    {
        // Act
        var ex = new CatgaException("Test error", "ERR_001");

        // Assert
        ex.Message.Should().Be("Test error");
        ex.ErrorCode.Should().Be("ERR_001");
    }

    [Fact]
    public void Constructor_WithInnerException_ShouldSetInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");

        // Act
        var ex = new CatgaException("Outer error", inner);

        // Assert
        ex.Message.Should().Be("Outer error");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void IsRetryable_DefaultValue_ShouldBeFalse()
    {
        // Act
        var ex = new CatgaException("Test error");

        // Assert
        ex.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_WhenSet_ShouldReturnValue()
    {
        // Act
        var ex = new CatgaException("Test error") { IsRetryable = true };

        // Assert
        ex.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void ErrorCode_WhenNotSet_ShouldBeNull()
    {
        // Act
        var ex = new CatgaException("Test error");

        // Assert
        ex.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void ToString_ShouldContainMessage()
    {
        // Arrange
        var ex = new CatgaException("Test error message");

        // Act
        var str = ex.ToString();

        // Assert
        str.Should().Contain("Test error message");
    }

    [Fact]
    public void Exception_CanBeCaughtAsException()
    {
        // Arrange & Act
        Exception? caught = null;
        try
        {
            throw new CatgaException("Test");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert
        caught.Should().NotBeNull();
        caught.Should().BeOfType<CatgaException>();
    }

    [Fact]
    public void Exception_WithAllProperties_ShouldPreserveAll()
    {
        // Arrange
        var inner = new ArgumentException("arg error");

        // Act
        var ex = new CatgaException("Main error", "ERR_999")
        {
            IsRetryable = true
        };

        // Assert
        ex.Message.Should().Be("Main error");
        ex.ErrorCode.Should().Be("ERR_999");
        ex.IsRetryable.Should().BeTrue();
    }
}
