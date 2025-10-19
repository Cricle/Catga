using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

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
    public async Task HandleAsync_WithException_ShouldPropagateException()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var request = new TestCommand("Test");
        var expectedException = new InvalidOperationException("Test exception");

        PipelineDelegate<TestResponse> next = () => throw expectedException;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await behavior.HandleAsync(request, next));

        exception.Should().Be(expectedException);
    }

    [Fact]
    public async Task HandleAsync_WithCorrelationId_ShouldSucceed()
    {
        // Arrange
        var behavior = new LoggingBehavior<TestCommand, TestResponse>(_logger);
        var correlationId = Guid.NewGuid().ToString();
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
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public record TestResponse(string Message);
}
