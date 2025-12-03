using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for ErrorCodes and ErrorInfo
/// </summary>
public class ErrorCodesTests
{
    [Fact]
    public void ErrorCodes_ShouldHaveExpectedValues()
    {
        // Assert
        ErrorCodes.ValidationFailed.Should().Be("VALIDATION_FAILED");
        ErrorCodes.HandlerFailed.Should().Be("HANDLER_FAILED");
        ErrorCodes.PipelineFailed.Should().Be("PIPELINE_FAILED");
        ErrorCodes.PersistenceFailed.Should().Be("PERSISTENCE_FAILED");
        ErrorCodes.LockFailed.Should().Be("LOCK_FAILED");
        ErrorCodes.TransportFailed.Should().Be("TRANSPORT_FAILED");
        ErrorCodes.SerializationFailed.Should().Be("SERIALIZATION_FAILED");
        ErrorCodes.Timeout.Should().Be("TIMEOUT");
        ErrorCodes.Cancelled.Should().Be("CANCELLED");
        ErrorCodes.InternalError.Should().Be("INTERNAL_ERROR");
    }

    [Fact]
    public void ErrorInfo_FromException_ShouldCreateCorrectInfo()
    {
        // Arrange
        var ex = new InvalidOperationException("Test error");

        // Act
        var errorInfo = ErrorInfo.FromException(ex);

        // Assert
        errorInfo.Code.Should().Be(ErrorCodes.InternalError);
        errorInfo.Message.Should().Contain("Test error");
        errorInfo.Exception.Should().Be(ex);
    }

    [Fact]
    public void ErrorInfo_FromException_WithCode_ShouldUseProvidedCode()
    {
        // Arrange
        var ex = new TimeoutException("Timeout occurred");

        // Act
        var errorInfo = ErrorInfo.FromException(ex, ErrorCodes.Timeout);

        // Assert
        errorInfo.Code.Should().Be(ErrorCodes.Timeout);
        errorInfo.Message.Should().Contain("Timeout occurred");
    }

    [Fact]
    public void ErrorInfo_Validation_ShouldCreateValidationError()
    {
        // Act
        var errorInfo = ErrorInfo.Validation("Name is required");

        // Assert
        errorInfo.Code.Should().Be(ErrorCodes.ValidationFailed);
        errorInfo.Message.Should().Be("Name is required");
        errorInfo.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ErrorInfo_Timeout_ShouldCreateTimeoutError()
    {
        // Act
        var errorInfo = ErrorInfo.Timeout("Operation timed out after 30s");

        // Assert
        errorInfo.Code.Should().Be(ErrorCodes.Timeout);
        errorInfo.Message.Should().Contain("30s");
        errorInfo.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void ErrorInfo_WithDetails_ShouldStoreDetails()
    {
        // Act
        var errorInfo = new ErrorInfo
        {
            Code = ErrorCodes.ValidationFailed,
            Message = "Validation failed",
            Details = "Field: email, Value: invalid"
        };

        // Assert
        errorInfo.Details.Should().Contain("email");
    }

    [Fact]
    public void ErrorInfo_IsRetryable_DefaultShouldBeFalse()
    {
        // Act
        var errorInfo = new ErrorInfo
        {
            Code = ErrorCodes.InternalError,
            Message = "Unknown error"
        };

        // Assert
        errorInfo.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ErrorInfo_IsRetryable_CanBeSetToTrue()
    {
        // Act
        var errorInfo = new ErrorInfo
        {
            Code = ErrorCodes.TransportFailed,
            Message = "Connection failed",
            IsRetryable = true
        };

        // Assert
        errorInfo.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void ErrorInfo_FromException_WithRetryable_ShouldSetFlag()
    {
        // Arrange
        var ex = new Exception("Network error");

        // Act
        var errorInfo = ErrorInfo.FromException(ex, ErrorCodes.TransportFailed, isRetryable: true);

        // Assert
        errorInfo.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void ErrorInfo_Validation_WithDetails_ShouldIncludeDetails()
    {
        // Act
        var errorInfo = ErrorInfo.Validation("Validation failed", "Field 'email' is invalid");

        // Assert
        errorInfo.Details.Should().Contain("email");
    }
}
