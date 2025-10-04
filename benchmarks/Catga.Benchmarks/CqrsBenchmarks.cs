using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CatCat.Benchmarks;

/// <summary>
/// CQRS 性能基准测试
/// 测试命令、查询、事件的吞吐量和延迟
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, RuntimeMoniker.Net90, warmupCount: 3, iterationCount: 10)]
public class CqrsBenchmarks
{
    private ITransitMediator _mediator = null!;
    private IServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 配置 Transit
        services.AddTransit(options =>
        {
            options.EnableIdempotency = true;
            options.EnableRetry = true;
            options.MaxRetryAttempts = 3;
        });

        // 注册测试处理器
        services.AddTransient<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddTransient<IRequestHandler<TestQuery, TestResponse>, TestQueryHandler>();
        services.AddTransient<IEventHandler<TestEvent>, TestEventHandler>();

        // 配置日志（使用 NullLogger 以避免日志开销）
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ITransitMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    /// <summary>
    /// 单次命令处理
    /// </summary>
    [Benchmark(Description = "单次命令处理")]
    public async Task<TransitResult<TestResponse>> SendCommand_Single()
    {
        var command = new TestCommand { Value = 42 };
        return await _mediator.SendAsync<TestCommand, TestResponse>(command);
    }

    /// <summary>
    /// 单次查询处理
    /// </summary>
    [Benchmark(Description = "单次查询处理")]
    public async Task<TransitResult<TestResponse>> SendQuery_Single()
    {
        var query = new TestQuery { Id = 1 };
        return await _mediator.SendAsync<TestQuery, TestResponse>(query);
    }

    /// <summary>
    /// 单次事件发布
    /// </summary>
    [Benchmark(Description = "单次事件发布")]
    public async Task PublishEvent_Single()
    {
        var evt = new TestEvent { Data = "test" };
        await _mediator.PublishAsync(evt);
    }

    /// <summary>
    /// 批量命令处理 (100 个)
    /// </summary>
    [Benchmark(Description = "批量命令处理 (100)")]
    public async Task SendCommand_Batch100()
    {
        var tasks = new Task<TransitResult<TestResponse>>[100];
        for (int i = 0; i < 100; i++)
        {
            var command = new TestCommand { Value = i };
            tasks[i] = _mediator.SendAsync<TestCommand, TestResponse>(command);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 批量查询处理 (100 个)
    /// </summary>
    [Benchmark(Description = "批量查询处理 (100)")]
    public async Task SendQuery_Batch100()
    {
        var tasks = new Task<TransitResult<TestResponse>>[100];
        for (int i = 0; i < 100; i++)
        {
            var query = new TestQuery { Id = i };
            tasks[i] = _mediator.SendAsync<TestQuery, TestResponse>(query);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 批量事件发布 (100 个)
    /// </summary>
    [Benchmark(Description = "批量事件发布 (100)")]
    public async Task PublishEvent_Batch100()
    {
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            var evt = new TestEvent { Data = $"test-{i}" };
            tasks[i] = _mediator.PublishAsync(evt);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 高并发命令处理 (1000 个)
    /// </summary>
    [Benchmark(Description = "高并发命令处理 (1000)")]
    public async Task SendCommand_HighConcurrency1000()
    {
        var tasks = new Task<TransitResult<TestResponse>>[1000];
        for (int i = 0; i < 1000; i++)
        {
            var command = new TestCommand { Value = i };
            tasks[i] = _mediator.SendAsync<TestCommand, TestResponse>(command);
        }
        await Task.WhenAll(tasks);
    }
}

// 测试消息和处理器

public class TestCommand : ICommand<TestResponse>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public int Value { get; set; }
}

public class TestQuery : IQuery<TestResponse>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public int Id { get; set; }
}

public class TestEvent : IEvent
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Data { get; set; } = string.Empty;
}

public class TestResponse
{
    public int Result { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
{
    public Task<TransitResult<TestResponse>> HandleAsync(
        TestCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse
        {
            Result = request.Value * 2,
            Message = "Command processed"
        };
        return Task.FromResult(TransitResult<TestResponse>.Success(response));
    }
}

public class TestQueryHandler : IRequestHandler<TestQuery, TestResponse>
{
    public Task<TransitResult<TestResponse>> HandleAsync(
        TestQuery request,
        CancellationToken cancellationToken = default)
    {
        var response = new TestResponse
        {
            Result = request.Id,
            Message = "Query processed"
        };
        return Task.FromResult(TransitResult<TestResponse>.Success(response));
    }
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent notification, CancellationToken cancellationToken = default)
    {
        // 模拟事件处理
        return Task.CompletedTask;
    }
}

