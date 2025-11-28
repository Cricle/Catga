using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Simplified tests for LoggingBehavior focusing on behavior rather than specific log calls
/// (Source-generated LoggerMessage makes detailed log verification difficult)
/// </summary>
public class LoggingBehaviorSimpleTests
{
    private readonly ILogger<LoggingBehavior<TestRequest, TestResponse>> _mockLogger;
    private readonly LoggingBehavior<TestRequest, TestResponse> _behavior;

    public LoggingBehaviorSimpleTests()
    {
        _mockLogger = Substitute.For<ILogger<LoggingBehavior<TestRequest, TestResponse>>>();
        _mockLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _behavior = new LoggingBehavior<TestRequest, TestResponse>(_mockLogger);
    }

    // ==================== Success Path ====================

    [Fact]
    public async Task HandleAsync_WithSuccessfulRequest_ShouldReturnSuccess()
    {
        // Arrange
        var request = new TestRequest { MessageId = 123, Data = "test" };
        var expectedResponse = new TestResponse { Result = "success" };

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task HandleAsync_ShouldPassThroughSuccessResult()
    {
        // Arrange
        var request = new TestRequest { MessageId = 456, Data = "test" };
        var response = new TestResponse { Result = "result-456" };
        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(response));

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Result.Should().Be("result-456");
    }

    // ==================== Failure Path ====================

    [Fact]
    public async Task HandleAsync_WithFailedRequest_ShouldPassThroughFailure()
    {
        // Arrange
        var request = new TestRequest { MessageId = 789, Data = "test" };
        var errorMessage = "Test error";

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Failure(errorMessage));

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(errorMessage);
    }

    [Fact]
    public async Task HandleAsync_WithFailureAndErrorInfo_ShouldPreserveErrorInfo()
    {
        // Arrange
        var request = new TestRequest { MessageId = 111, Data = "test" };
        var errorInfo = ErrorInfo.Validation("Validation failed");

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Failure(errorInfo));

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Validation failed");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    // ==================== Exception Handling ====================

    [Fact]
    public async Task HandleAsync_WithException_ShouldCatchAndReturnFailure()
    {
        // Arrange
        var request = new TestRequest { MessageId = 222, Data = "test" };
        var exception = new InvalidOperationException("Test exception");

        PipelineDelegate<TestResponse> next = () => throw exception;

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithArgumentException_ShouldHandleCorrectly()
    {
        // Arrange
        var request = new TestRequest { MessageId = 333, Data = "test" };
        var exception = new ArgumentException("Invalid argument");

        PipelineDelegate<TestResponse> next = () => throw exception;

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
        result.Error.Should().Contain("Invalid argument");
    }

    [Fact]
    public async Task HandleAsync_WithOperationCanceledException_ShouldReturnFailure()
    {
        // Arrange
        var request = new TestRequest { MessageId = 444, Data = "test" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        PipelineDelegate<TestResponse> next = () => throw new OperationCanceledException(cts.Token);

        // Act
        var result = await _behavior.HandleAsync(request, next, cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
    }

    // ==================== Multiple Requests ====================

    [Fact]
    public async Task HandleAsync_WithMultipleSequentialRequests_ShouldHandleAll()
    {
        // Arrange & Act
        var result1 = await _behavior.HandleAsync(
            new TestRequest { MessageId = 1, Data = "test1" },
            () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse { Result = "1" })),
            CancellationToken.None);

        var result2 = await _behavior.HandleAsync(
            new TestRequest { MessageId = 2, Data = "test2" },
            () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse { Result = "2" })),
            CancellationToken.None);

        var result3 = await _behavior.HandleAsync(
            new TestRequest { MessageId = 3, Data = "test3" },
            () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse { Result = "3" })),
            CancellationToken.None);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result1.Value!.Result.Should().Be("1");

        result2.IsSuccess.Should().BeTrue();
        result2.Value!.Result.Should().Be("2");

        result3.IsSuccess.Should().BeTrue();
        result3.Value!.Result.Should().Be("3");
    }

    // ==================== Different Response Types ====================

    [Fact]
    public async Task HandleAsync_WithStringResponse_ShouldWork()
    {
        // Arrange
        var request = new StringRequest { MessageId = 555, Data = "test" };
        var behavior = new LoggingBehavior<StringRequest, string>(
            Substitute.For<ILogger<LoggingBehavior<StringRequest, string>>>());

        PipelineDelegate<string> next = () =>
            ValueTask.FromResult(CatgaResult<string>.Success("string-result"));

        // Act
        var result = await behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("string-result");
    }

    [Fact]
    public async Task HandleAsync_WithIntResponse_ShouldWork()
    {
        // Arrange
        var request = new IntRequest { MessageId = 666, Data = "test" };
        var behavior = new LoggingBehavior<IntRequest, int>(
            Substitute.For<ILogger<LoggingBehavior<IntRequest, int>>>());

        PipelineDelegate<int> next = () =>
            ValueTask.FromResult(CatgaResult<int>.Success(42));

        // Act
        var result = await behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    // ==================== Duration Tracking ====================

    [Fact]
    public async Task HandleAsync_ShouldHandleLongRunningRequests()
    {
        // Arrange
        var request = new TestRequest { MessageId = 777, Data = "test" };

        PipelineDelegate<TestResponse> next = async () =>
        {
            await Task.Delay(50); // Simulate work
            return CatgaResult<TestResponse>.Success(new TestResponse { Result = "slow" });
        };

        // Act
        var result = await _behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert - Should complete successfully even with delay
        result.IsSuccess.Should().BeTrue();
        result.Value!.Result.Should().Be("slow");
    }

    // ==================== Test Helpers ====================

    public record TestRequest : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    public record StringRequest : IRequest<string>
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record IntRequest : IRequest<int>
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }
}

