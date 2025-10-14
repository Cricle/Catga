using BenchmarkDotNet.Attributes;
using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

/// <summary>
/// Concurrency performance benchmarks - high-concurrency stress testing
/// Target: Linear scaling with concurrency, no contention
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class ConcurrencyPerformanceBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ConcurrentCommand, ConcurrentResult>, ConcurrentCommandHandler>();
        services.AddScoped<IEventHandler<ConcurrentEvent>, ConcurrentEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [Benchmark(Baseline = true, Description = "Concurrent Commands (10)")]
    public async Task ConcurrentCommands_10()
    {
        var tasks = new ValueTask<CatgaResult<ConcurrentResult>>[10];
        for (int i = 0; i < 10; i++)
        {
            var cmd = new ConcurrentCommand(i);
            tasks[i] = _mediator.SendAsync<ConcurrentCommand, ConcurrentResult>(cmd);
        }
        for (int i = 0; i < 10; i++)
        {
            await tasks[i];
        }
    }

    [Benchmark(Description = "Concurrent Commands (100)")]
    public async Task ConcurrentCommands_100()
    {
        var tasks = new ValueTask<CatgaResult<ConcurrentResult>>[100];
        for (int i = 0; i < 100; i++)
        {
            var cmd = new ConcurrentCommand(i);
            tasks[i] = _mediator.SendAsync<ConcurrentCommand, ConcurrentResult>(cmd);
        }
        for (int i = 0; i < 100; i++)
        {
            await tasks[i];
        }
    }

    [Benchmark(Description = "Concurrent Commands (1000)")]
    public async Task ConcurrentCommands_1000()
    {
        var tasks = new ValueTask<CatgaResult<ConcurrentResult>>[1000];
        for (int i = 0; i < 1000; i++)
        {
            var cmd = new ConcurrentCommand(i);
            tasks[i] = _mediator.SendAsync<ConcurrentCommand, ConcurrentResult>(cmd);
        }
        for (int i = 0; i < 1000; i++)
        {
            await tasks[i];
        }
    }

    [Benchmark(Description = "Concurrent Events (100)")]
    public async Task ConcurrentEvents_100()
    {
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            var evt = new ConcurrentEvent(i);
            tasks[i] = _mediator.PublishAsync(evt);
        }
        await Task.WhenAll(tasks);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

[MemoryPackable]
public partial record ConcurrentCommand(int Id) : IRequest<ConcurrentResult>;

[MemoryPackable]
public partial record ConcurrentResult(int Id, long Timestamp);

[MemoryPackable]
public partial record ConcurrentEvent(int Id) : IEvent;

public class ConcurrentCommandHandler : IRequestHandler<ConcurrentCommand, ConcurrentResult>
{
    public Task<CatgaResult<ConcurrentResult>> HandleAsync(
        ConcurrentCommand request,
        CancellationToken cancellationToken = default)
    {
        var result = new ConcurrentResult(request.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Task.FromResult(CatgaResult<ConcurrentResult>.Success(result));
    }
}

public class ConcurrentEventHandler : IEventHandler<ConcurrentEvent>
{
    public Task HandleAsync(ConcurrentEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

