using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Pipeline;

public class RetryBehaviorTests
{
    private readonly ILogger<RetryBehavior<TestCommand, TestResponse>> _logger;
    private readonly CatgaOptions _options;

    public RetryBehaviorTests()
    {
        _logger = Substitute.For<ILogger<RetryBehavior<TestCommand, TestResponse>>>();
        _options = new CatgaOptions
        {
            MaxRetryAttempts = 3,
            RetryDelayMs = 10
        };
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulRequest_ShouldNotRetry()
    {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, _options);
        var request = new TestCommand("Test");
        var expectedResponse = new TestResponse("Success");
        var callCount = 0;

        PipelineDelegate<TestResponse> next = () =>
        {
            callCount++;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithRetryableException_ShouldRetry()
    {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, _options);
        var request = new TestCommand("Test");
        var callCount = 0;
        var expectedResponse = new TestResponse("Success");

        PipelineDelegate<TestResponse> next = () =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new CatgaException("Transient error") { IsRetryable = true };
            }
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_WithNonRetryableException_ShouldNotRetry()
    {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, _options);
        var request = new TestCommand("Test");
        var callCount = 0;

        PipelineDelegate<TestResponse> next = () =>
        {
            callCount++;
            throw new CatgaException("Non-retryable error") { IsRetryable = false };
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        callCount.Should().Be(1);
        result.Exception.Should().BeOfType<CatgaException>();
    }

    [Fact]
    public async Task HandleAsync_WithMaxRetriesExceeded_ShouldReturnFailure()
    {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, _options);
        var request = new TestCommand("Test");
        var callCount = 0;

        PipelineDelegate<TestResponse> next = () =>
        {
            callCount++;
            throw new CatgaException("Persistent error") { IsRetryable = true };
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        callCount.Should().Be(_options.MaxRetryAttempts + 1); // Initial attempt + retries
        result.Exception.Should().BeOfType<CatgaException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnexpectedException_ShouldWrapInCatgaException()
    {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, _options);
        var request = new TestCommand("Test");

        PipelineDelegate<TestResponse> next = () =>
            throw new InvalidOperationException("Unexpected error");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Unexpected error");
        result.Exception.Should().BeOfType<CatgaException>();
        result.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task HandleAsync_WithCustomRetryOptions_ShouldRespectConfiguration()
    {
        // Arrange
        var customOptions = new CatgaOptions
        {
            MaxRetryAttempts = 5,
            RetryDelayMs = 5
        };
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, customOptions);
        var request = new TestCommand("Test");
        var callCount = 0;

        PipelineDelegate<TestResponse> next = () =>
        {
            callCount++;
            throw new CatgaException("Error") { IsRetryable = true };
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        callCount.Should().Be(customOptions.MaxRetryAttempts + 1);
    }

    [Fact]
    public async Task HandleAsync_ShouldLogRetryAttempts()
    {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, TestResponse>(_logger, _options);
        var request = new TestCommand("Test");
        var callCount = 0;

        PipelineDelegate<TestResponse> next = () =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new CatgaException("Error") { IsRetryable = true };
            }
            return new ValueTask<CatgaResult<TestResponse>>(
                CatgaResult<TestResponse>.Success(new TestResponse("Success")));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify retry was logged
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // Test data classes
    public record TestCommand(string Name) : IRequest<TestResponse>
    {
        public long MessageId { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public record TestResponse(string Message);
}
