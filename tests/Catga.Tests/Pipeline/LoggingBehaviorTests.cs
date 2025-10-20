using Catga.Core;
using Catga.Exceptions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Catga.Abstractions;

namespace Catga.Tests.Pipeline;

public class LoggingBehaviorTests
{
    private readonly ILogger<LoggingBehavior<TestCommand, TestResponse>> _logger;

    public LoggingBehaviorTests()
    {
        _logger = Substitute.For<ILogger<LoggingBehavior<TestCommand, TestResponse>>>();
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulRequest_ShouldReturnSuccess()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var request = new TestCommand("Test");
        var expectedResponse = new TestResponse("Success");

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task HandleAsync_WithFailedRequest_ShouldReturnFailure()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var request = new TestCommand("Test");
        var errorMessage = "Operation failed";

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Failure(errorMessage));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(errorMessage);
    }

    [Fact]
    public async Task HandleAsync_WithException_ShouldReturnFailure()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var request = new TestCommand("Test");
        var expectedException = new InvalidOperationException("Test exception");

        PipelineDelegate<TestResponse> next = () => throw expectedException;

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert - 异常应被捕获并转换为 CatgaResult.Failure
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Test exception");
        result.ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
    }

    [Fact]
    public async Task HandleAsync_WithCorrelationId_ShouldSucceed()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var correlationId = MessageExtensions.NewMessageId();
        var request = new TestCommand("Test") { CorrelationId = correlationId };
        var expectedResponse = new TestResponse("Success");

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task HandleAsync_WithAsyncWork_ShouldCompleteSuccessfully()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var request = new TestCommand("Test");
        var expectedResponse = new TestResponse("Success");

        PipelineDelegate<TestResponse> next = async () =>
        {
            await Task.Delay(10); // Simulate some work
            return CatgaResult<TestResponse>.Success(expectedResponse);
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task HandleAsync_WithCatgaException_ShouldReturnFailure()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var request = new TestCommand("Test");
        var catgaException = new CatgaException("Test error");

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(
                CatgaResult<TestResponse>.Failure("Error", catgaException));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().Be(catgaException);
    }

    // Test data classes
    public record TestCommand(string Name) : IRequest<TestResponse>
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public record TestResponse(string Message);
}
