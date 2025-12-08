using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for ErrorInfo.
/// </summary>
public class ErrorInfoTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        // Act
        var error = new ErrorInfo { Code = "ERR001", Message = "Something went wrong" };

        // Assert
        error.Code.Should().Be("ERR001");
        error.Message.Should().Be("Something went wrong");
        error.Exception.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithException_SetsException()
    {
        // Arrange
        var ex = new InvalidOperationException("Test");

        // Act
        var error = new ErrorInfo { Code = "ERR002", Message = "Error occurred", Exception = ex };

        // Assert
        error.Code.Should().Be("ERR002");
        error.Message.Should().Be("Error occurred");
        error.Exception.Should().Be(ex);
    }

    [Fact]
    public void FromException_CreatesErrorInfo()
    {
        // Arrange
        var ex = new ArgumentException("Invalid argument");

        // Act
        var error = ErrorInfo.FromException(ex);

        // Assert
        error.Message.Should().Be("Invalid argument");
        error.Exception.Should().Be(ex);
        error.Code.Should().Be(ErrorCodes.InternalError);
    }

    [Fact]
    public void FromException_WithCode_UsesProvidedCode()
    {
        // Arrange
        var ex = new ArgumentException("Invalid argument");

        // Act
        var error = ErrorInfo.FromException(ex, "CUSTOM_ERR");

        // Assert
        error.Code.Should().Be("CUSTOM_ERR");
    }

    [Fact]
    public void FromException_WithRetryable_SetsFlag()
    {
        // Arrange
        var ex = new TimeoutException("Timeout");

        // Act
        var error = ErrorInfo.FromException(ex, isRetryable: true);

        // Assert
        error.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void Validation_CreatesValidationError()
    {
        // Act
        var error = ErrorInfo.Validation("Field is required", "FieldName: Name");

        // Assert
        error.Code.Should().Be(ErrorCodes.ValidationFailed);
        error.Message.Should().Be("Field is required");
        error.Details.Should().Be("FieldName: Name");
        error.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void IsRetryable_DefaultsFalse()
    {
        // Act
        var error = new ErrorInfo { Code = "ERR", Message = "Test" };

        // Assert
        error.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void Details_CanBeSet()
    {
        // Act
        var error = new ErrorInfo { Code = "ERR", Message = "Test", Details = "Extra info" };

        // Assert
        error.Details.Should().Be("Extra info");
    }
}






