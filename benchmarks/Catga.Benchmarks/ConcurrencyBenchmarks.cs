using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Resilience;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Benchmarks;

/// <summary>
/// High-concurrency stress test benchmarks.
/// Run: dotnet run -c Release -- --filter *Concurrency*
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class ConcurrencyBenchmarks
{
    private ICatgaMediator _mediator = null!;
    private IServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCatga().UseMemoryPack();
        services.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();
        services.AddScoped<IRequestHandler<StressCommand, StressResult>, StressCommandHandler>();
        services.AddScoped<IEventHandler<StressEvent>, StressEventHandler>();

        _provider = services.BuildServiceProvider();
        _mediator = _provider.GetRequiredService<ICatgaMediator>();
    }

    [GlobalCleanup]
    public void Cleanup() => (_provider as IDisposable)?.Dispose();

    [Benchmark(Baseline = true, Description = "Concurrent 10")]
    public async Task Concurrent_10()
    {
        var tasks = new ValueTask<CatgaResult<StressResult>>[10];
        for (int i = 0; i < 10; i++)
            tasks[i] = _mediator.SendAsync<StressCommand, StressResult>(new StressCommand(i));
        for (int i = 0; i < 10; i++)
            await tasks[i];
    }

    [Benchmark(Description = "Concurrent 50")]
    public async Task Concurrent_50()
    {
        var tasks = new ValueTask<CatgaResult<StressResult>>[50];
        for (int i = 0; i < 50; i++)
            tasks[i] = _mediator.SendAsync<StressCommand, StressResult>(new StressCommand(i));
        for (int i = 0; i < 50; i++)
            await tasks[i];
    }

    [Benchmark(Description = "Concurrent 100")]
    public async Task Concurrent_100()
    {
        var tasks = new ValueTask<CatgaResult<StressResult>>[100];
        for (int i = 0; i < 100; i++)
            tasks[i] = _mediator.SendAsync<StressCommand, StressResult>(new StressCommand(i));
        for (int i = 0; i < 100; i++)
            await tasks[i];
    }

    [Benchmark(Description = "Concurrent 200")]
    public async Task Concurrent_200()
    {
        var tasks = new ValueTask<CatgaResult<StressResult>>[200];
        for (int i = 0; i < 200; i++)
            tasks[i] = _mediator.SendAsync<StressCommand, StressResult>(new StressCommand(i));
        for (int i = 0; i < 200; i++)
            await tasks[i];
    }

    [Benchmark(Description = "Concurrent Events 100")]
    public async Task ConcurrentEvents_100()
    {
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
            tasks[i] = _mediator.PublishAsync(new StressEvent(i));
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Mixed Workload (50 cmd + 50 evt)")]
    public async Task MixedWorkload_100()
    {
        var cmdTasks = new ValueTask<CatgaResult<StressResult>>[50];
        var evtTasks = new Task[50];

        for (int i = 0; i < 50; i++)
        {
            cmdTasks[i] = _mediator.SendAsync<StressCommand, StressResult>(new StressCommand(i));
            evtTasks[i] = _mediator.PublishAsync(new StressEvent(i));
        }

        for (int i = 0; i < 50; i++)
            await cmdTasks[i];
        await Task.WhenAll(evtTasks);
    }

    [Benchmark(Description = "Parallel.ForEachAsync 100")]
    public async Task ParallelForEach_100()
    {
        var items = Enumerable.Range(0, 100);
        await Parallel.ForEachAsync(items, async (i, ct) =>
        {
            await _mediator.SendAsync<StressCommand, StressResult>(new StressCommand(i), ct);
        });
    }
}

#region Messages

[MemoryPackable]
public partial record StressCommand(int Id) : IRequest<StressResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record StressResult(int Id, long Timestamp);

[MemoryPackable]
public partial record StressEvent(int Id) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

#endregion

#region Handlers

public sealed class StressCommandHandler : IRequestHandler<StressCommand, StressResult>
{
    public ValueTask<CatgaResult<StressResult>> HandleAsync(StressCommand request, CancellationToken ct = default)
        => new(CatgaResult<StressResult>.Success(new StressResult(request.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
}

public sealed class StressEventHandler : IEventHandler<StressEvent>
{
    public ValueTask HandleAsync(StressEvent @event, CancellationToken ct = default) => ValueTask.CompletedTask;
}

#endregion
