using Catga;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Core;

/// <summary>
/// Handler缓存机制性能测试 (TDD方法)
/// 测试场景：
/// 1. Handler解析性能
/// 2. 多次解析的一致性
/// 3. 并发解析安全性
/// 4. 不同生命周期的Handler
/// 5. 大量Handler的解析效率
/// 6. 内存分配优化
/// </summary>
public class HandlerCachePerformanceTests
{
    #region 基础解析性能测试

    [Fact]
    public async Task GetRequestHandler_ShouldResolveQuickly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var command = new TestCommand("Performance");
        var iterations = 1000;

        // Warmup
        await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await mediator.SendAsync<TestCommand, TestResponse>(command);
        }
        stopwatch.Stop();

        // Assert - 1000次解析和执行应该在200ms内完成
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200);
        var avgMicroseconds = (stopwatch.ElapsedMilliseconds * 1000.0) / iterations;
        avgMicroseconds.Should().BeLessThan(200); // 平均每次 < 200μs
    }

    [Fact]
    public async Task GetEventHandlers_MultipleHandlers_ShouldResolveEfficiently()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler1>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler2>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler3>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler4>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler5>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var @event = new CacheTestEvent(1, "MultiHandler");
        var iterations = 500;

        // Warmup
        await mediator.PublishAsync(@event);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await mediator.PublishAsync(@event);
        }
        stopwatch.Stop();

        // Assert - 500次事件发布（每次解析5个handler）应该在500ms内完成
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    #endregion

    #region 不同生命周期Handler测试

    [Fact]
    public async Task Scoped_Vs_Transient_Vs_Singleton_PerformanceComparison()
    {
        // Arrange - 测试三种生命周期的性能差异
        var iterations = 500;

        // Scoped
        var scopedServices = new ServiceCollection();
        scopedServices.AddLogging();
        scopedServices.AddCatga();
        scopedServices.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        var scopedProvider = scopedServices.BuildServiceProvider();
        var scopedMediator = scopedProvider.GetRequiredService<ICatgaMediator>();

        // Transient
        var transientServices = new ServiceCollection();
        transientServices.AddLogging();
        transientServices.AddCatga();
        transientServices.AddTransient<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        var transientProvider = transientServices.BuildServiceProvider();
        var transientMediator = transientProvider.GetRequiredService<ICatgaMediator>();

        // Singleton
        var singletonServices = new ServiceCollection();
        singletonServices.AddLogging();
        singletonServices.AddCatga();
        singletonServices.AddSingleton<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        var singletonProvider = singletonServices.BuildServiceProvider();
        var singletonMediator = singletonProvider.GetRequiredService<ICatgaMediator>();

        var command = new TestCommand("Lifecycle");

        // Act & Measure - Scoped
        var scopedStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await scopedMediator.SendAsync<TestCommand, TestResponse>(command);
        }
        scopedStopwatch.Stop();

        // Act & Measure - Transient
        var transientStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await transientMediator.SendAsync<TestCommand, TestResponse>(command);
        }
        transientStopwatch.Stop();

        // Act & Measure - Singleton
        var singletonStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await singletonMediator.SendAsync<TestCommand, TestResponse>(command);
        }
        singletonStopwatch.Stop();

        // Assert - Singleton应该最快，Scoped次之，Transient最慢
        singletonStopwatch.ElapsedMilliseconds.Should().BeLessThan(scopedStopwatch.ElapsedMilliseconds + 100);
        scopedStopwatch.ElapsedMilliseconds.Should().BeLessThan(300);
        transientStopwatch.ElapsedMilliseconds.Should().BeLessThan(400);
    }

    #endregion

    #region 并发解析安全性测试

    [Fact]
    public async Task ConcurrentHandlerResolution_ShouldBeThreadSafe()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var concurrentRequests = 100;
        var successCount = 0;

        // Act - 并发解析handler
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            var command = new TestCommand($"Concurrent-{i}");
            var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

            if (result.IsSuccess)
                Interlocked.Increment(ref successCount);
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert
        successCount.Should().Be(concurrentRequests);
    }

    [Fact]
    public async Task ConcurrentEventHandlerResolution_ShouldHandleMultipleHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler1>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler2>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler3>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        TestEventHandler1.ExecutedCount = 0;
        TestEventHandler2.ExecutedCount = 0;
        TestEventHandler3.ExecutedCount = 0;

        var concurrentEvents = 50;

        // Act
        var tasks = Enumerable.Range(0, concurrentEvents).Select(async i =>
        {
            var @event = new CacheTestEvent(i, $"ConcurrentEvent-{i}");
            await mediator.PublishAsync(@event);
        }).ToList();

        await Task.WhenAll(tasks);
        await Task.Delay(100); // 等待所有handler完成

        // Assert - 每个handler应该处理了所有事件
        TestEventHandler1.ExecutedCount.Should().BeGreaterOrEqualTo(concurrentEvents);
        TestEventHandler2.ExecutedCount.Should().BeGreaterOrEqualTo(concurrentEvents);
        TestEventHandler3.ExecutedCount.Should().BeGreaterOrEqualTo(concurrentEvents);
    }

    #endregion

    #region 大量Handler测试

    [Fact]
    public async Task GetEventHandlers_With20Handlers_ShouldResolveEfficiently()
    {
        // Arrange - 注册20个不同的handler
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        for (int i = 0; i < 20; i++)
        {
            services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler1>();
        }

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var @event = new CacheTestEvent(1, "ManyHandlers");

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await mediator.PublishAsync(@event);
        stopwatch.Stop();

        // Assert - 解析20个handler应该很快
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task GetEventHandlers_With50Handlers_ShouldStillPerform()
    {
        // Arrange - 压力测试：50个handler
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        for (int i = 0; i < 50; i++)
        {
            services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler1>();
        }

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var @event = new CacheTestEvent(1, "ExtremeHandlers");

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await mediator.PublishAsync(@event);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200);
    }

    #endregion

    #region 解析一致性测试

    [Fact]
    public async Task MultipleResolutions_ShouldReturnConsistentHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var command = new TestCommand("Consistency");
        var results = new List<CatgaResult<TestResponse>>();

        // Act - 多次执行，验证一致性
        for (int i = 0; i < 10; i++)
        {
            var result = await mediator.SendAsync<TestCommand, TestResponse>(command);
            results.Add(result);
        }

        // Assert - 所有结果应该一致
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task EventHandlerResolution_ShouldReturnAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler1>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler2>();
        services.AddScoped<IEventHandler<CacheTestEvent>, TestEventHandler3>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        TestEventHandler1.ExecutedCount = 0;
        TestEventHandler2.ExecutedCount = 0;
        TestEventHandler3.ExecutedCount = 0;

        var @event = new CacheTestEvent(1, "AllHandlers");

        // Act
        await mediator.PublishAsync(@event);
        await Task.Delay(50);

        // Assert - 所有handler都应该被调用
        TestEventHandler1.ExecutedCount.Should().Be(1);
        TestEventHandler2.ExecutedCount.Should().Be(1);
        TestEventHandler3.ExecutedCount.Should().Be(1);
    }

    #endregion

    #region 内存分配测试

    [Fact]
    public async Task HandlerResolution_ShouldMinimizeAllocations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var command = new TestCommand("Allocation");

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            await mediator.SendAsync<TestCommand, TestResponse>(command);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeMemory = GC.GetTotalMemory(false);

        // Act - 执行1000次
        for (int i = 0; i < 1000; i++)
        {
            await mediator.SendAsync<TestCommand, TestResponse>(command);
        }

        var afterMemory = GC.GetTotalMemory(false);
        var allocatedMB = (afterMemory - beforeMemory) / (1024.0 * 1024.0);

        // Assert - 1000次调用的内存分配应该合理（< 10MB）
        allocatedMB.Should().BeLessThan(10);
    }

    #endregion

    #region 高负载性能测试

    [Fact]
    public async Task HandlerResolution_UnderHighLoad_ShouldMaintainPerformance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var totalOperations = 10000;
        var concurrentBatches = 10;
        var operationsPerBatch = totalOperations / concurrentBatches;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - 高负载：10批并发，每批1000个操作
        var tasks = Enumerable.Range(0, concurrentBatches).Select(async batchIndex =>
        {
            for (int i = 0; i < operationsPerBatch; i++)
            {
                var command = new TestCommand($"Batch{batchIndex}-Op{i}");
                await mediator.SendAsync<TestCommand, TestResponse>(command);
            }
        }).ToList();

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - 10000次操作应该在合理时间内完成
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
        var opsPerSecond = totalOperations / (stopwatch.ElapsedMilliseconds / 1000.0);
        opsPerSecond.Should().BeGreaterThan(2000); // 至少2000 ops/s
    }

    #endregion

    #region Scope生命周期测试

    [Fact]
    public async Task ScopedHandlers_ShouldBeDifferentAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ScopedTestCommand, TestResponse>, ScopedTestCommandHandler>();

        var provider = services.BuildServiceProvider();

        var command = new ScopedTestCommand(1, "Scoped");
        var instanceIds = new List<Guid>();

        // Act - 在不同scope中执行
        for (int i = 0; i < 5; i++)
        {
            using var scope = provider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result = await mediator.SendAsync<ScopedTestCommand, TestResponse>(command);

            if (result.IsSuccess && result.Value != null)
            {
                instanceIds.Add(result.Value.InstanceId);
            }
        }

        // Assert - 每个scope应该有不同的handler实例
        instanceIds.Should().HaveCount(5);
        instanceIds.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task SingletonHandlers_ShouldBeSameAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton<IRequestHandler<SingletonTestCommand, TestResponse>, SingletonTestCommandHandler>();

        var provider = services.BuildServiceProvider();

        var command = new SingletonTestCommand(1, "Singleton");
        var instanceIds = new List<Guid>();

        // Act - 在不同scope中执行
        for (int i = 0; i < 5; i++)
        {
            using var scope = provider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result = await mediator.SendAsync<SingletonTestCommand, TestResponse>(command);

            if (result.IsSuccess && result.Value != null)
            {
                instanceIds.Add(result.Value.InstanceId);
            }
        }

        // Assert - 所有scope应该使用同一个handler实例
        instanceIds.Should().HaveCount(5);
        instanceIds.Distinct().Should().HaveCount(1);
    }

    #endregion
}

#region 测试消息定义

public record TestCommand(string Name) : IRequest<TestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record TestResponse(string ProcessedName, Guid InstanceId = default);

public record CacheTestEvent(int Id, string Name) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

#endregion

#region 测试Handler实现

public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    public Task<CatgaResult<TestResponse>> HandleAsync(
        TestCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse($"Processed-{request.Name}");
        return Task.FromResult(CatgaResult<TestResponse>.Success(response));
    }
}

// Separate command for scoped handler tests
public record ScopedTestCommand(int Id, string Name) : IRequest<TestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public class ScopedTestCommandHandler : IRequestHandler<ScopedTestCommand, TestResponse>
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public Task<CatgaResult<TestResponse>> HandleAsync(
        ScopedTestCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse($"Scoped-{request.Name}", _instanceId);
        return Task.FromResult(CatgaResult<TestResponse>.Success(response));
    }
}

// Separate command for singleton handler tests
public record SingletonTestCommand(int Id, string Name) : IRequest<TestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public class SingletonTestCommandHandler : IRequestHandler<SingletonTestCommand, TestResponse>
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public Task<CatgaResult<TestResponse>> HandleAsync(
        SingletonTestCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse($"Singleton-{request.Name}", _instanceId);
        return Task.FromResult(CatgaResult<TestResponse>.Success(response));
    }
}

public class TestEventHandler1 : IEventHandler<CacheTestEvent>
{
    public static int ExecutedCount = 0;

    public Task HandleAsync(CacheTestEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        return Task.CompletedTask;
    }
}

public class TestEventHandler2 : IEventHandler<CacheTestEvent>
{
    public static int ExecutedCount = 0;

    public Task HandleAsync(CacheTestEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        return Task.CompletedTask;
    }
}

public class TestEventHandler3 : IEventHandler<CacheTestEvent>
{
    public static int ExecutedCount = 0;

    public Task HandleAsync(CacheTestEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExecutedCount);
        return Task.CompletedTask;
    }
}

public class TestEventHandler4 : IEventHandler<CacheTestEvent>
{
    public Task HandleAsync(CacheTestEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class TestEventHandler5 : IEventHandler<CacheTestEvent>
{
    public Task HandleAsync(CacheTestEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

#endregion

