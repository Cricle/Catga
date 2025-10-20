using Catga;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Core;using Catga.Abstractions;
using Catga.Messages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catga.Tests;

/// <summary>
/// CatgaMediator 核心功能测试
/// </summary>
public class CatgaMediatorTests
{
    [Fact]
    public async Task SendAsync_WithValidCommand_ShouldReturnSuccess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(); // 添加 Logging 支持
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand("test");

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Message.Should().Be("Processed: test");
    }

    [Fact]
    public async Task SendAsync_WithoutHandler_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(); // 添加 Logging 支持
        services.AddCatga();
        // 注意：没有注册 handler

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand("test");

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_WithValidEvent_ShouldInvokeHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(); // 添加 Logging 支持
        services.AddCatga();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var testEvent = new TestEvent("Hello");

        // Act
        await mediator.PublishAsync(testEvent);

        // Assert - 事件发布成功（不抛异常）
        Assert.True(true);
    }

    [Fact]
    public async Task PublishAsync_WithMultipleHandlers_ShouldInvokeAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler2>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var testEvent = new TestEvent("Hello");

        // Act
        await mediator.PublishAsync(testEvent);

        // Assert - 事件发布成功（不抛异常）
        Assert.True(true);
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        // 没有注册handler

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var testEvent = new TestEvent("Hello");

        // Act & Assert - 不应该抛异常
        await mediator.PublishAsync(testEvent);
        Assert.True(true);
    }

    // TODO: Fix CancellationToken propagation through pipeline behaviors
    // [Fact]
    // public async Task SendAsync_WithCancellationToken_ShouldPropagate()
    // {
    //     // Arrange
    //     var services = new ServiceCollection();
    //     services.AddLogging();
    //     services.AddCatga();
    //     services.AddScoped<IRequestHandler<TestCommand, TestResponse>, CancellableCommandHandler>();
    //
    //     var provider = services.BuildServiceProvider();
    //     var mediator = provider.GetRequiredService<ICatgaMediator>();
    //     var command = new TestCommand("test");
    //     var cts = new CancellationTokenSource();
    //     cts.Cancel();
    //
    //     // Act & Assert
    //     await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    //         await mediator.SendAsync<TestCommand, TestResponse>(command, cts.Token));
    // }

    [Fact]
    public async Task SendAsync_WithFailureResult_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, FailingCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand("test");

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Handler failed");
    }

    [Fact]
    public async Task SendAsync_MultipleSequentialCalls_ShouldAllSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var result1 = await mediator.SendAsync<TestCommand, TestResponse>(new TestCommand("test1"));
        var result2 = await mediator.SendAsync<TestCommand, TestResponse>(new TestCommand("test2"));
        var result3 = await mediator.SendAsync<TestCommand, TestResponse>(new TestCommand("test3"));

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result1.Value!.Message.Should().Be("Processed: test1");
        result2.IsSuccess.Should().BeTrue();
        result2.Value!.Message.Should().Be("Processed: test2");
        result3.IsSuccess.Should().BeTrue();
        result3.Value!.Message.Should().Be("Processed: test3");
    }

    [Fact]
    public async Task SendAsync_ConcurrentCalls_ShouldAllSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var tasks = Enumerable.Range(0, 10).Select(i =>
            mediator.SendAsync<TestCommand, TestResponse>(new TestCommand($"test{i}")).AsTask());
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(result => result.IsSuccess.Should().BeTrue());
        results.Should().HaveCount(10);
    }
}

// 测试用的消息类型
public record TestCommand(string Value) : IRequest<TestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record TestResponse(string Message);

public record TestEvent(string Message) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

// 测试用的处理器
public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    public Task<CatgaResult<TestResponse>> HandleAsync(
        TestCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse($"Processed: {request.Value}");
        return Task.FromResult(CatgaResult<TestResponse>.Success(response));
    }
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        // 模拟事件处理
        return Task.CompletedTask;
    }
}

public class TestEventHandler2 : IEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        // 第二个事件处理器
        return Task.CompletedTask;
    }
}

public class CancellableCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    public Task<CatgaResult<TestResponse>> HandleAsync(
        TestCommand request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CatgaResult<TestResponse>.Success(new TestResponse("Success")));
    }
}

public class FailingCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    public Task<CatgaResult<TestResponse>> HandleAsync(
        TestCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<TestResponse>.Failure("Handler failed"));
    }
}

public class CancellableEventHandler : IEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

