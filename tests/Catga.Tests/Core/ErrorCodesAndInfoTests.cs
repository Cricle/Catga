using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// ErrorCodes和ErrorInfo单元测试
/// 目标覆盖率: 从 0% → 100%
/// </summary>
public class ErrorCodesAndInfoTests
{
    #region ErrorCodes Constants Tests

    [Fact]
    public void ErrorCodes_ShouldHaveValidationFailed()
    {
        ErrorCodes.ValidationFailed.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveHandlerFailed()
    {
        ErrorCodes.HandlerFailed.Should().Be("HANDLER_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHavePipelineFailed()
    {
        ErrorCodes.PipelineFailed.Should().Be("PIPELINE_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHavePersistenceFailed()
    {
        ErrorCodes.PersistenceFailed.Should().Be("PERSISTENCE_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveLockFailed()
    {
        ErrorCodes.LockFailed.Should().Be("LOCK_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveTransportFailed()
    {
        ErrorCodes.TransportFailed.Should().Be("TRANSPORT_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveSerializationFailed()
    {
        ErrorCodes.SerializationFailed.Should().Be("SERIALIZATION_FAILED");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveTimeout()
    {
        ErrorCodes.Timeout.Should().Be("TIMEOUT");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveCancelled()
    {
        ErrorCodes.Cancelled.Should().Be("CANCELLED");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveInternalError()
    {
        ErrorCodes.InternalError.Should().Be("INTERNAL_ERROR");
    }

    #endregion

    #region ErrorInfo Construction Tests

    [Fact]
    public void ErrorInfo_WithRequiredProperties_ShouldCreate()
    {
        // Act
        var error = new ErrorInfo
        {
            Code = "TEST_ERROR",
            Message = "Test error message"
        };

        // Assert
        error.Code.Should().Be("TEST_ERROR");
        error.Message.Should().Be("Test error message");
        error.IsRetryable.Should().BeFalse();
        error.Exception.Should().BeNull();
        error.Details.Should().BeNull();
    }

    [Fact]
    public void ErrorInfo_WithAllProperties_ShouldCreate()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        var error = new ErrorInfo
        {
            Code = "TEST_ERROR",
            Message = "Test message",
            IsRetryable = true,
            Exception = exception,
            Details = "Additional details"
        };

        // Assert
        error.Code.Should().Be("TEST_ERROR");
        error.Message.Should().Be("Test message");
        error.IsRetryable.Should().BeTrue();
        error.Exception.Should().BeSameAs(exception);
        error.Details.Should().Be("Additional details");
    }

    #endregion

    #region ErrorInfo.FromException Tests

    [Fact]
    public void FromException_WithException_ShouldCreateErrorInfo()
    {
        // Arrange
        var exception = new InvalidOperationException("Something went wrong");

        // Act
        var error = ErrorInfo.FromException(exception);

        // Assert
        error.Code.Should().Be(ErrorCodes.InternalError);
        error.Message.Should().Be("Something went wrong");
        error.IsRetryable.Should().BeFalse();
        error.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public void FromException_WithCustomCode_ShouldUseCustomCode()
    {
        // Arrange
        var exception = new InvalidOperationException("Error");

        // Act
        var error = ErrorInfo.FromException(exception, "CUSTOM_ERROR");

        // Assert
        error.Code.Should().Be("CUSTOM_ERROR");
        error.Message.Should().Be("Error");
    }

    [Fact]
    public void FromException_WithRetryable_ShouldSetRetryable()
    {
        // Arrange
        var exception = new TimeoutException("Timeout");

        // Act
        var error = ErrorInfo.FromException(exception, "TIMEOUT", isRetryable: true);

        // Assert
        error.IsRetryable.Should().BeTrue();
        error.Code.Should().Be("TIMEOUT");
    }

    [Fact]
    public void FromException_WithNullCode_ShouldUseInternalError()
    {
        // Arrange
        var exception = new Exception("Error");

        // Act
        var error = ErrorInfo.FromException(exception, null);

        // Assert
        error.Code.Should().Be(ErrorCodes.InternalError);
    }

    #endregion

    #region ErrorInfo.Validation Tests

    [Fact]
    public void Validation_WithMessage_ShouldCreateValidationError()
    {
        // Act
        var error = ErrorInfo.Validation("Invalid input");

        // Assert
        error.Code.Should().Be(ErrorCodes.ValidationFailed);
        error.Message.Should().Be("Invalid input");
        error.IsRetryable.Should().BeFalse();
        error.Details.Should().BeNull();
    }

    [Fact]
    public void Validation_WithDetails_ShouldIncludeDetails()
    {
        // Act
        var error = ErrorInfo.Validation("Invalid email", "Email format is incorrect");

        // Assert
        error.Code.Should().Be(ErrorCodes.ValidationFailed);
        error.Message.Should().Be("Invalid email");
        error.Details.Should().Be("Email format is incorrect");
    }

    [Fact]
    public void Validation_ShouldNotBeRetryable()
    {
        // Act
        var error = ErrorInfo.Validation("Error");

        // Assert
        error.IsRetryable.Should().BeFalse();
    }

    #endregion

    #region ErrorInfo.Timeout Tests

    [Fact]
    public void Timeout_WithMessage_ShouldCreateTimeoutError()
    {
        // Act
        var error = ErrorInfo.Timeout("Operation timed out");

        // Assert
        error.Code.Should().Be(ErrorCodes.Timeout);
        error.Message.Should().Be("Operation timed out");
        error.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void Timeout_ShouldBeRetryable()
    {
        // Act
        var error = ErrorInfo.Timeout("Timeout");

        // Assert
        error.IsRetryable.Should().BeTrue();
    }

    #endregion

    #region ErrorInfo Struct Behavior Tests

    [Fact]
    public void ErrorInfo_AsStruct_ShouldBeValueType()
    {
        // Act & Assert
        typeof(ErrorInfo).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void ErrorInfo_RecordStruct_ShouldSupportEquality()
    {
        // Arrange
        var error1 = new ErrorInfo { Code = "TEST", Message = "Test" };
        var error2 = new ErrorInfo { Code = "TEST", Message = "Test" };
        var error3 = new ErrorInfo { Code = "OTHER", Message = "Test" };

        // Assert
        error1.Should().Be(error2);
        error1.Should().NotBe(error3);
    }

    [Fact]
    public void ErrorInfo_WithDifferentRetryable_ShouldNotBeEqual()
    {
        // Arrange
        var error1 = new ErrorInfo { Code = "TEST", Message = "Test", IsRetryable = true };
        var error2 = new ErrorInfo { Code = "TEST", Message = "Test", IsRetryable = false };

        // Assert
        error1.Should().NotBe(error2);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ErrorInfo_CanBeUsedInCatgaResult()
    {
        // Arrange
        var error = ErrorInfo.Validation("Invalid request");

        // Act
        var result = CatgaResult<string>.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid request");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void ErrorInfo_FromException_CanBeUsedInCatgaResult()
    {
        // Arrange
        var exception = new InvalidOperationException("Operation failed");
        var error = ErrorInfo.FromException(exception, ErrorCodes.HandlerFailed);

        // Act
        var result = CatgaResult.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Operation failed");
        result.ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
    }

    #endregion
}







