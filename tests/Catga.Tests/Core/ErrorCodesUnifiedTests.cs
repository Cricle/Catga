using Catga.Core;
using Catga.Exceptions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive tests for the unified ErrorCodes system, CatgaException hierarchy, and CatgaResult.
/// </summary>
public class ErrorCodesUnifiedTests
{
    // ── ErrorCodes constants ──────────────────────────────────────────────────

    [Fact]
    public void ErrorCodes_CoreCodes_HaveCorrectValues()
    {
        ErrorCodes.ValidationFailed.Should().Be("VALIDATION_FAILED");
        ErrorCodes.HandlerFailed.Should().Be("HANDLER_FAILED");
        ErrorCodes.HandlerNotFound.Should().Be("HANDLER_NOT_FOUND");
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
    public void ErrorCodes_HttpDomainCodes_HaveCorrectValues()
    {
        ErrorCodes.NotFound.Should().Be("NOT_FOUND");
        ErrorCodes.Conflict.Should().Be("CONFLICT");
        ErrorCodes.Unauthorized.Should().Be("UNAUTHORIZED");
        ErrorCodes.Forbidden.Should().Be("FORBIDDEN");
    }

    [Fact]
    public void ErrorCodes_FlowCodes_HaveCorrectValues()
    {
        ErrorCodes.FlowFailed.Should().Be("FLOW_FAILED");
        ErrorCodes.FlowCancelled.Should().Be("FLOW_CANCELLED");
        ErrorCodes.FlowTimeout.Should().Be("FLOW_TIMEOUT");
        ErrorCodes.FlowCompensating.Should().Be("FLOW_COMPENSATING");
    }

    [Fact]
    public void ErrorCodes_AllCodes_AreNonEmpty()
    {
        var codes = new[]
        {
            ErrorCodes.ValidationFailed, ErrorCodes.HandlerFailed, ErrorCodes.HandlerNotFound,
            ErrorCodes.PipelineFailed, ErrorCodes.PersistenceFailed, ErrorCodes.LockFailed,
            ErrorCodes.TransportFailed, ErrorCodes.SerializationFailed, ErrorCodes.Timeout,
            ErrorCodes.Cancelled, ErrorCodes.InternalError, ErrorCodes.NotFound,
            ErrorCodes.Conflict, ErrorCodes.Unauthorized, ErrorCodes.Forbidden,
            ErrorCodes.FlowFailed, ErrorCodes.FlowCancelled, ErrorCodes.FlowTimeout,
            ErrorCodes.FlowCompensating
        };
        codes.Should().AllSatisfy(c => c.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void ErrorCodes_AllCodes_AreUnique()
    {
        var codes = new[]
        {
            ErrorCodes.ValidationFailed, ErrorCodes.HandlerFailed, ErrorCodes.HandlerNotFound,
            ErrorCodes.PipelineFailed, ErrorCodes.PersistenceFailed, ErrorCodes.LockFailed,
            ErrorCodes.TransportFailed, ErrorCodes.SerializationFailed, ErrorCodes.Timeout,
            ErrorCodes.Cancelled, ErrorCodes.InternalError, ErrorCodes.NotFound,
            ErrorCodes.Conflict, ErrorCodes.Unauthorized, ErrorCodes.Forbidden,
            ErrorCodes.FlowFailed, ErrorCodes.FlowCancelled, ErrorCodes.FlowTimeout,
            ErrorCodes.FlowCompensating
        };
        codes.Should().OnlyHaveUniqueItems();
    }

    // ── ErrorInfo ─────────────────────────────────────────────────────────────

    [Fact]
    public void ErrorInfo_FromException_UsesInternalErrorByDefault()
    {
        var ex = new InvalidOperationException("boom");
        var info = ErrorInfo.FromException(ex);
        info.Code.Should().Be(ErrorCodes.InternalError);
        info.Message.Should().Be("boom");
        info.Exception.Should().BeSameAs(ex);
        info.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ErrorInfo_FromException_WithCustomCode()
    {
        var ex = new TimeoutException("timed out");
        var info = ErrorInfo.FromException(ex, ErrorCodes.Timeout, isRetryable: true);
        info.Code.Should().Be(ErrorCodes.Timeout);
        info.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void ErrorInfo_Validation_SetsCorrectFields()
    {
        var info = ErrorInfo.Validation("Name required", "field=name");
        info.Code.Should().Be(ErrorCodes.ValidationFailed);
        info.IsRetryable.Should().BeFalse();
        info.Details.Should().Contain("name");
    }

    [Fact]
    public void ErrorInfo_Timeout_IsRetryable()
    {
        var info = ErrorInfo.Timeout("30s exceeded");
        info.Code.Should().Be(ErrorCodes.Timeout);
        info.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void ErrorInfo_NotFound_SetsCorrectCode()
    {
        var info = ErrorInfo.NotFound("Order 123 not found");
        info.Code.Should().Be(ErrorCodes.NotFound);
        info.IsRetryable.Should().BeFalse();
    }

    // ── CatgaException hierarchy ──────────────────────────────────────────────

    [Fact]
    public void CatgaException_SetsErrorCodeAndRetryable()
    {
        var ex = new CatgaException("test", ErrorCodes.HandlerFailed, isRetryable: false);
        ex.ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
        ex.IsRetryable.Should().BeFalse();
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void CatgaException_WithInnerException_PreservesInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new CatgaException("outer", inner, ErrorCodes.PipelineFailed);
        ex.InnerException.Should().BeSameAs(inner);
        ex.ErrorCode.Should().Be(ErrorCodes.PipelineFailed);
    }

    [Fact]
    public void CatgaTimeoutException_UsesTimeoutCode_AndIsRetryable()
    {
        var ex = new CatgaTimeoutException("timed out");
        ex.ErrorCode.Should().Be(ErrorCodes.Timeout);
        ex.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void CatgaValidationException_UsesValidationCode_AndNotRetryable()
    {
        var errors = new List<string> { "Name required", "Email invalid" };
        var ex = new CatgaValidationException("Validation failed", errors);
        ex.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        ex.IsRetryable.Should().BeFalse();
        ex.ValidationErrors.Should().HaveCount(2);
    }

    [Fact]
    public void HandlerNotFoundException_UsesHandlerNotFoundCode()
    {
        var ex = new HandlerNotFoundException("CreateOrderCommand");
        ex.ErrorCode.Should().Be(ErrorCodes.HandlerNotFound);
        ex.Message.Should().Contain("CreateOrderCommand");
        ex.IsRetryable.Should().BeFalse();
    }

    // ── CatgaResult ───────────────────────────────────────────────────────────

    [Fact]
    public void CatgaResult_Success_IsSuccessTrue()
    {
        var result = CatgaResult<string>.Success("hello");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.Error.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void CatgaResult_Failure_WithString_IsSuccessFalse()
    {
        var result = CatgaResult<string>.Failure("something went wrong");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("something went wrong");
        result.Value.Should().BeNull();
    }

    [Fact]
    public void CatgaResult_Failure_WithException_CopiesErrorCode()
    {
        var ex = new CatgaTimeoutException("timed out");
        var result = CatgaResult<int>.Failure("timeout", ex);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Timeout);
        result.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void CatgaResult_Failure_WithErrorInfo_CopiesCodeAndMessage()
    {
        var info = ErrorInfo.Validation("Name required");
        var result = CatgaResult<string>.Failure(info);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
        result.Error.Should().Be("Name required");
    }

    [Fact]
    public void CatgaResult_NoValue_Success_IsSuccessTrue()
    {
        var result = CatgaResult.Success();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CatgaResult_NoValue_Failure_IsSuccessFalse()
    {
        var result = CatgaResult.Failure("error");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("error");
    }

    [Fact]
    public void CatgaResult_NoValue_Failure_WithErrorInfo()
    {
        var info = ErrorInfo.NotFound("resource missing");
        var result = CatgaResult.Failure(info);
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
