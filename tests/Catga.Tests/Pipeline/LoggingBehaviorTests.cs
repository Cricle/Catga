using Catga.Handlers;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Pipeline;

/// <summary>
/// LoggingBehavior 测试
/// </summary>
public class LoggingBehaviorTests
{
    [Fact]
    public async Task HandleAsync_ShouldLogRequestAndResponse()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LogTestRequest, LogTestResponse>>>();
        var behavior = new LoggingBehavior<LogTestRequest, LogTestResponse>(logger);

        var request = new LogTestRequest { Value = "test" };
        var expectedResult = CatgaResult<LogTestResponse>.Success(new LogTestResponse { Message = "OK" });

        RequestHandlerDelegate<LogTestResponse> next = () => Task.FromResult(expectedResult);

        // Act
        var result = await behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResult);

        // 验证日志调用
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("LogTestRequest")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task HandleAsync_WithException_ShouldLogError()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LogTestRequest, LogTestResponse>>>();
        var behavior = new LoggingBehavior<LogTestRequest, LogTestResponse>(logger);

        var request = new LogTestRequest { Value = "test" };
        var exception = new InvalidOperationException("Test error");

        RequestHandlerDelegate<LogTestResponse> next = () => throw exception;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await behavior.HandleAsync(request, next, CancellationToken.None)
        );

        // 验证错误日志
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task HandleAsync_WithFailureResult_ShouldLogWarning()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LogTestRequest, LogTestResponse>>>();
        var behavior = new LoggingBehavior<LogTestRequest, LogTestResponse>(logger);

        var request = new LogTestRequest { Value = "test" };
        var failureResult = CatgaResult<LogTestResponse>.Failure("Error occurred");

        RequestHandlerDelegate<LogTestResponse> next = () => Task.FromResult(failureResult);

        // Act
        var result = await behavior.HandleAsync(request, next, CancellationToken.None);

        // Assert
        result.Should().Be(failureResult);

        // 验证警告日志
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("failed")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }
}

// 测试用的消息类型
public record LogTestRequest : IRequest<LogTestResponse>
{
    public string Value { get; init; } = string.Empty;
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

public record LogTestResponse
{
    public string Message { get; init; } = string.Empty;
}

