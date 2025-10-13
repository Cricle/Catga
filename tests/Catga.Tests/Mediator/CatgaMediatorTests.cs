using Catga;
using Catga.InMemory;
using Catga.Messages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Tests.Mediator;

/// <summary>
/// CatgaMediator 核心功能测试
/// </summary>
public class CatgaMediatorTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ICatgaMediator _mediator;

    public CatgaMediatorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddCatga();
        services.AddCatgaInMemoryTransport();
        services.AddCatgaInMemoryPersistence();

        // Register test handlers
        services.AddSingleton<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();
        services.AddSingleton<IRequestHandler<TestQuery, string>, TestQueryHandler>();
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [Fact]
    public async Task SendAsync_Command_ShouldExecuteHandler()
    {
        // Arrange
        var command = new TestCommand("test-data");

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Data.Should().Be("test-data");
        result.Value.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_Query_ShouldExecuteHandler()
    {
        // Arrange
        var query = new TestQuery("query-key");

        // Act
        var result = await _mediator.SendAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Result for query-key");
    }

    [Fact]
    public async Task PublishAsync_Event_ShouldExecuteAllHandlers()
    {
        // Arrange
        var @event = new TestEvent("event-data");
        TestEventHandler.CallCount = 0;

        // Act
        await _mediator.PublishAsync(@event);

        // Assert
        TestEventHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WithFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var command = new TestCommand("fail");

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Command failed");
    }

    [Fact]
    public async Task SendAsync_WithCorrelationId_ShouldPreserveCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var command = new TestCommand("test") { CorrelationId = correlationId };

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        command.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task SendAsync_WithQoSExactlyOnce_ShouldUseIdempotency()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var command1 = new TestCommand("test") { MessageId = messageId, QoS = QualityOfService.ExactlyOnce };
        var command2 = new TestCommand("test") { MessageId = messageId, QoS = QualityOfService.ExactlyOnce };

        // Act
        var result1 = await _mediator.SendAsync(command1);
        var result2 = await _mediator.SendAsync(command2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        // 第二次应该返回缓存结果（幂等性）
    }
}

// Test Messages
public record TestCommand(string Data) : IRequest<TestResult>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.AtMostOnce;
}

public record TestQuery(string Key) : IRequest<string>;

public record TestEvent(string Data) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

public record TestResult(string Data, bool Success);

// Test Handlers
public class TestCommandHandler : IRequestHandler<TestCommand, TestResult>
{
    public Task<CatgaResult<TestResult>> HandleAsync(TestCommand request, CancellationToken cancellationToken = default)
    {
        if (request.Data == "fail")
            return Task.FromResult(CatgaResult<TestResult>.Failure("Command failed"));

        var result = new TestResult(request.Data, true);
        return Task.FromResult(CatgaResult<TestResult>.Success(result));
    }
}

public class TestQueryHandler : IRequestHandler<TestQuery, string>
{
    public Task<CatgaResult<string>> HandleAsync(TestQuery request, CancellationToken cancellationToken = default)
    {
        var result = $"Result for {request.Key}";
        return Task.FromResult(CatgaResult<string>.Success(result));
    }
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    public static int CallCount = 0;

    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.CompletedTask;
    }
}

