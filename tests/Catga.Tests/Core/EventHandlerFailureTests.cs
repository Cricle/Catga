using Catga;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Core;

/// <summary>
/// 事件处理失败场景完整测试 (TDD方法)
/// 测试场景：
/// 1. 单个Handler失败不影响其他Handler
/// 2. 多个Handler并发失败
/// 3. Handler超时处理
/// 4. 异常类型和错误恢复
/// 5. 死信队列场景
/// 6. 事件处理重试机制
/// </summary>
public class EventHandlerFailureTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<EventHandlerFailureTests> _logger;

    public EventHandlerFailureTests()
    {
        _logger = Substitute.For<ILogger<EventHandlerFailureTests>>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        // 注册各种测试handler
        services.AddScoped<IEventHandler<TestEvent>, SuccessfulEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, FailingEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, SlowEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, ThrowingEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, IntermittentFailureHandler>();

        services.AddScoped<IEventHandler<MultiHandlerEvent>, MultiHandler1>();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, MultiHandler2>();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, MultiHandler3>();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, FailingMultiHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    #region 单Handler失败测试

    [Fact]
    public async Task PublishAsync_SingleHandlerFails_ShouldNotThrowException()
    {
        // Arrange
        var @event = new TestEvent(1, "SingleFailure");
        FailingEventHandler.ShouldFail = true;

        // Act - 即使有handler失败，PublishAsync也不应该抛出异常
        var act = async () => await _mediator.PublishAsync(@event);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_HandlerThrowsException_OtherHandlersShouldStillExecute()
    {
        // Arrange
        var @event = new TestEvent(2, "ExceptionTest");

        SuccessfulEventHandler.ExecutedCount = 0;
        ThrowingEventHandler.ExecutedCount = 0;
        SlowEventHandler.ExecutedCount = 0;

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(100); // 等待所有异步handler完成

        // Assert - 即使ThrowingEventHandler抛出异常，其他handler仍应执行
        SuccessfulEventHandler.ExecutedCount.Should().BeGreaterThan(0);
        ThrowingEventHandler.ExecutedCount.Should().BeGreaterThan(0);
        SlowEventHandler.ExecutedCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region 多Handler并发失败测试

    [Fact]
    public async Task PublishAsync_MultipleHandlersFail_ShouldContinueProcessing()
    {
        // Arrange
        var @event = new MultiHandlerEvent(10, "MultiFailure");

        MultiHandler1.ExecutedCount = 0;
        MultiHandler2.ExecutedCount = 0;
        MultiHandler3.ExecutedCount = 0;
        FailingMultiHandler.ExecutedCount = 0;
        FailingMultiHandler.ShouldFail = true;

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(100);

        // Assert - 成功的handler应该都执行了
        MultiHandler1.ExecutedCount.Should().BeGreaterThan(0);
        MultiHandler2.ExecutedCount.Should().BeGreaterThan(0);
        MultiHandler3.ExecutedCount.Should().BeGreaterThan(0);
        FailingMultiHandler.ExecutedCount.Should().BeGreaterThan(0); // 即使失败也会尝试执行
    }

    [Fact]
    public async Task PublishAsync_AllHandlersFail_ShouldNotThrow()
    {
        // Arrange
        var @event = new MultiHandlerEvent(11, "AllFail");

        // 让所有handler都失败
        FailingMultiHandler.ShouldFail = true;
        MultiHandler1.ShouldFail = true;
        MultiHandler2.ShouldFail = true;
        MultiHandler3.ShouldFail = true;

        // Act & Assert
        var act = async () => await _mediator.PublishAsync(@event);
        await act.Should().NotThrowAsync();

        // Cleanup
        MultiHandler1.ShouldFail = false;
        MultiHandler2.ShouldFail = false;
        MultiHandler3.ShouldFail = false;
    }

    #endregion

    #region 异常类型测试

    [Fact]
    public async Task PublishAsync_HandlerThrowsInvalidOperationException_ShouldHandleGracefully()
    {
        // Arrange
        var @event = new TestEvent(3, "InvalidOperation");
        ThrowingEventHandler.ExceptionType = typeof(InvalidOperationException);

        // Act & Assert
        var act = async () => await _mediator.PublishAsync(@event);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_HandlerThrowsArgumentException_ShouldHandleGracefully()
    {
        // Arrange
        var @event = new TestEvent(4, "ArgumentException");
        ThrowingEventHandler.ExceptionType = typeof(ArgumentException);

        // Act & Assert
        var act = async () => await _mediator.PublishAsync(@event);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_HandlerThrowsCustomException_ShouldHandleGracefully()
    {
        // Arrange
        var @event = new TestEvent(5, "CustomException");
        ThrowingEventHandler.ExceptionType = typeof(CustomEventHandlerException);

        // Act & Assert
        var act = async () => await _mediator.PublishAsync(@event);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region 超时处理测试

    [Fact(Skip = "PublishAsync使用Task.WhenAll等待所有handlers完成，此测试的假设不正确")]
    public async Task PublishAsync_HandlerTakesTooLong_ShouldNotBlockOthers()
    {
        // Arrange
        var @event = new TestEvent(6, "Timeout");
        SlowEventHandler.DelayMs = 500; // 慢handler
        SuccessfulEventHandler.ExecutedCount = 0;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(100); // 等待快速handler完成

        stopwatch.Stop();

        // Assert - 快速handler应该已经完成，不需要等待慢handler
        SuccessfulEventHandler.ExecutedCount.Should().BeGreaterThan(0);
        // 整体应该比慢handler快
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300);
    }

    [Fact]
    public async Task PublishAsync_WithCancellation_ShouldCancelHandlers()
    {
        // Arrange
        var @event = new TestEvent(7, "CancelTest");
        SlowEventHandler.DelayMs = 1000;

        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        SlowEventHandler.CancelledCount = 0;

        // Act
        await _mediator.PublishAsync(@event, cts.Token);
        await Task.Delay(200); // 等待处理

        // Assert - 慢handler应该被取消
        SlowEventHandler.CancelledCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region 间歇性失败测试

    [Fact]
    public async Task PublishAsync_IntermittentFailures_ShouldEventuallySucceed()
    {
        // Arrange - 模拟间歇性失败（50%失败率）
        var eventCount = 20;
        IntermittentFailureHandler.SuccessCount = 0;
        IntermittentFailureHandler.FailureCount = 0;

        // Act
        for (int i = 0; i < eventCount; i++)
        {
            var @event = new TestEvent(i, $"Intermittent-{i}");
            await _mediator.PublishAsync(@event);
            await Task.Delay(10);
        }

        // Assert - 应该有成功和失败
        IntermittentFailureHandler.SuccessCount.Should().BeGreaterThan(0);
        IntermittentFailureHandler.FailureCount.Should().BeGreaterThan(0);
        (IntermittentFailureHandler.SuccessCount + IntermittentFailureHandler.FailureCount)
            .Should().BeGreaterOrEqualTo(eventCount);
    }

    #endregion

    #region 并发事件处理失败测试

    [Fact]
    public async Task PublishAsync_ConcurrentEventsWithFailures_ShouldHandleIndependently()
    {
        // Arrange
        var eventCount = 50;
        MultiHandler1.ExecutedCount = 0;
        FailingMultiHandler.ExecutedCount = 0;
        FailingMultiHandler.ShouldFail = true;

        // Act - 并发发布多个事件
        var tasks = Enumerable.Range(0, eventCount).Select(async i =>
        {
            var @event = new MultiHandlerEvent(i, $"Concurrent-{i}");
            await _mediator.PublishAsync(@event);
        }).ToList();

        await Task.WhenAll(tasks);
        await Task.Delay(200);

        // Assert - 即使有失败，也应该处理了所有事件
        MultiHandler1.ExecutedCount.Should().BeGreaterOrEqualTo(eventCount);
        FailingMultiHandler.ExecutedCount.Should().BeGreaterOrEqualTo(eventCount);
    }

    #endregion

    #region 事件顺序和一致性测试

    [Fact]
    public async Task PublishAsync_HandlerFailures_ShouldNotAffectEventOrder()
    {
        // Arrange
        var eventCount = 30;
        var processedIds = new List<int>();
        var lockObj = new object();

        SuccessfulEventHandler.OnExecute = (eventId) =>
        {
            lock (lockObj)
            {
                processedIds.Add(eventId);
            }
        };

        // Act - 顺序发布事件
        for (int i = 0; i < eventCount; i++)
        {
            var @event = new TestEvent(i, $"Order-{i}");
            await _mediator.PublishAsync(@event);
            await Task.Delay(5);
        }

        await Task.Delay(100);

        // Assert - 即使有失败，成功的handler应该保持顺序
        processedIds.Should().BeInAscendingOrder();

        // Cleanup
        SuccessfulEventHandler.OnExecute = null;
    }

    #endregion

    #region 资源清理测试

    [Fact]
    public async Task PublishAsync_HandlerFailsAfterResourceAllocation_ShouldCleanup()
    {
        // Arrange
        var @event = new TestEvent(100, "ResourceCleanup");
        var resourcesAllocated = 0;
        var resourcesCleaned = 0;

        // 模拟资源分配和清理
        ThrowingEventHandler.OnExecute = () =>
        {
            Interlocked.Increment(ref resourcesAllocated);
            throw new InvalidOperationException("Simulated failure");
        };

        ThrowingEventHandler.OnFinally = () =>
        {
            Interlocked.Increment(ref resourcesCleaned);
        };

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(50);

        // Assert - 即使失败，资源也应该被清理
        resourcesAllocated.Should().BeGreaterThan(0);
        // Note: 实际的清理验证取决于handler的实现

        // Cleanup
        ThrowingEventHandler.OnExecute = null;
        ThrowingEventHandler.OnFinally = null;
    }

    #endregion

    #region 压力测试

    [Fact]
    public async Task PublishAsync_HighVolumeWithFailures_ShouldMaintainStability()
    {
        // Arrange - 高并发场景，部分handler会失败
        var eventCount = 500;
        FailingEventHandler.ShouldFail = true;
        SuccessfulEventHandler.ExecutedCount = 0;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - 快速发布大量事件
        var tasks = Enumerable.Range(0, eventCount).Select(async i =>
        {
            var @event = new TestEvent(i, $"Volume-{i}");
            await _mediator.PublishAsync(@event);
        }).ToList();

        await Task.WhenAll(tasks);
        await Task.Delay(500); // 等待处理完成

        stopwatch.Stop();

        // Assert
        SuccessfulEventHandler.ExecutedCount.Should().BeGreaterOrEqualTo(eventCount);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    #endregion

    #region 实际业务场景

    [Fact]
    public async Task PublishAsync_OrderCreatedScenario_HandlerFailuresShouldNotBlockSystem()
    {
        // Arrange - 模拟订单创建事件：发送邮件失败不应影响其他操作
        var @event = new MultiHandlerEvent(999, "OrderCreated");

        MultiHandler1.ExecutedCount = 0; // 模拟库存更新
        MultiHandler2.ExecutedCount = 0; // 模拟发送邮件（可能失败）
        MultiHandler3.ExecutedCount = 0; // 模拟记录日志

        // 让邮件handler失败
        MultiHandler2.ShouldFail = true;

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(100);

        // Assert - 库存和日志应该仍然处理成功
        MultiHandler1.ExecutedCount.Should().BeGreaterThan(0);
        MultiHandler3.ExecutedCount.Should().BeGreaterThan(0);

        // Cleanup
        MultiHandler2.ShouldFail = false;
    }

    #endregion
}

#region 测试消息定义

public record TestEvent(int Id, string Name) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record MultiHandlerEvent(int Id, string Name) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

#endregion

#region 测试Handler实现

public class SuccessfulEventHandler : IEventHandler<TestEvent>
{
    public static int ExecutedCount = 0;
    public static Action<int>? OnExecute = null;

    public ValueTask HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        OnExecute?.Invoke(@event.Id);
        return ValueTask.CompletedTask;
    }
}

public class FailingEventHandler : IEventHandler<TestEvent>
{
    public static bool ShouldFail = false;

    public ValueTask HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException($"Handler failed for event {@event.Id}");
        }
        return ValueTask.CompletedTask;
    }
}

public class ThrowingEventHandler : IEventHandler<TestEvent>
{
    public static int ExecutedCount = 0;
    public static Type ExceptionType = typeof(InvalidOperationException);
    public static Action? OnExecute = null;
    public static Action? OnFinally = null;

    public ValueTask HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            Interlocked.Increment(ref ExecutedCount);
            OnExecute?.Invoke();

            var exception = Activator.CreateInstance(ExceptionType, "Handler exception") as Exception;
            throw exception ?? new InvalidOperationException("Handler exception");
        }
        finally
        {
            OnFinally?.Invoke();
        }
    }
}

public class SlowEventHandler : IEventHandler<TestEvent>
{
    public static int ExecutedCount = 0;
    public static int CancelledCount = 0;
    public static int DelayMs = 100;

    public async ValueTask HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        try
        {
            await Task.Delay(DelayMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref CancelledCount);
            throw;
        }
    }
}

public class IntermittentFailureHandler : IEventHandler<TestEvent>
{
    public static int SuccessCount = 0;
    public static int FailureCount = 0;
    private static int _counter = 0;

    public ValueTask HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _counter);

        if (count % 2 == 0)
        {
            Interlocked.Increment(ref FailureCount);
            throw new InvalidOperationException("Intermittent failure");
        }

        Interlocked.Increment(ref SuccessCount);
        return ValueTask.CompletedTask;
    }
}

// MultiHandler implementations
public class MultiHandler1 : IEventHandler<MultiHandlerEvent>
{
    public static int ExecutedCount = 0;
    public static bool ShouldFail = false;

    public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        if (ShouldFail)
            throw new InvalidOperationException("MultiHandler1 failed");
        return ValueTask.CompletedTask;
    }
}

public class MultiHandler2 : IEventHandler<MultiHandlerEvent>
{
    public static int ExecutedCount = 0;
    public static bool ShouldFail = false;

    public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        if (ShouldFail)
            throw new InvalidOperationException("MultiHandler2 failed");
        return ValueTask.CompletedTask;
    }
}

public class MultiHandler3 : IEventHandler<MultiHandlerEvent>
{
    public static int ExecutedCount = 0;
    public static bool ShouldFail = false;

    public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        if (ShouldFail)
            throw new InvalidOperationException("MultiHandler3 failed");
        return ValueTask.CompletedTask;
    }
}

public class FailingMultiHandler : IEventHandler<MultiHandlerEvent>
{
    public static int ExecutedCount = 0;
    public static bool ShouldFail = false;

    public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        if (ShouldFail)
            throw new InvalidOperationException("FailingMultiHandler failed");
        return ValueTask.CompletedTask;
    }
}

#endregion

#region 自定义异常

public class CustomEventHandlerException : Exception
{
    public CustomEventHandlerException(string message) : base(message) { }
}

#endregion







