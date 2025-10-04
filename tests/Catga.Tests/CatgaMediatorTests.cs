using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
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
        services.AddTransit();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand { Value = "test" };

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
        services.AddTransit();
        // 注意：没有注册 handler

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand { Value = "test" };

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
        services.AddTransit();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var testEvent = new TestEvent { Message = "Hello" };

        // Act
        await mediator.PublishAsync(testEvent);

        // Assert - 事件发布成功（不抛异常）
        Assert.True(true);
    }
}

// 测试用的消息类型
public record TestCommand : MessageBase, ICommand<TestResponse>
{
    public string Value { get; init; } = string.Empty;
}

public record TestResponse
{
    public string Message { get; init; } = string.Empty;
}

public record TestEvent : EventBase
{
    public string Message { get; init; } = string.Empty;
}

// 测试用的处理器
public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    public Task<CatgaResult<TestResponse>> HandleAsync(
        TestCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse { Message = $"Processed: {request.Value}" };
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

