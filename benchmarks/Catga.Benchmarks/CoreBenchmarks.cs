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
/// Core CQRS performance benchmarks - measures pure framework overhead.
/// Run: dotnet run -c Release -- --filter *Core*
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class CoreBenchmarks
{
    private ICatgaMediator _mediator = null!;
    private IServiceProvider _provider = null!;

    private SimpleCommand _command = null!;
    private SimpleQuery _query = null!;
    private SimpleEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCatga().UseMemoryPack();
        services.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();
        services.AddScoped<IRequestHandler<SimpleCommand, SimpleResult>, SimpleCommandHandler>();
        services.AddScoped<IRequestHandler<SimpleQuery, SimpleResult>, SimpleQueryHandler>();
        services.AddScoped<IEventHandler<SimpleEvent>, SimpleEventHandler>();

        _provider = services.BuildServiceProvider();
        _mediator = _provider.GetRequiredService<ICatgaMediator>();

        _command = new SimpleCommand(1, "test");
        _query = new SimpleQuery(1);
        _event = new SimpleEvent(1, "data");
    }

    [GlobalCleanup]
    public void Cleanup() => (_provider as IDisposable)?.Dispose();

    [Benchmark(Baseline = true, Description = "Command")]
    public ValueTask<CatgaResult<SimpleResult>> Command()
        => _mediator.SendAsync<SimpleCommand, SimpleResult>(_command);

    [Benchmark(Description = "Query")]
    public ValueTask<CatgaResult<SimpleResult>> Query()
        => _mediator.SendAsync<SimpleQuery, SimpleResult>(_query);

    [Benchmark(Description = "Event (1 handler)")]
    public Task Event() => _mediator.PublishAsync(_event);

    [Benchmark(Description = "Command x100")]
    public async Task Command_Batch100()
    {
        for (int i = 0; i < 100; i++)
            await _mediator.SendAsync<SimpleCommand, SimpleResult>(_command);
    }

    [Benchmark(Description = "Event x100")]
    public async Task Event_Batch100()
    {
        for (int i = 0; i < 100; i++)
            await _mediator.PublishAsync(_event);
    }
}

#region Messages

[MemoryPackable]
public partial record SimpleCommand(int Id, string Data) : IRequest<SimpleResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record SimpleQuery(int Id) : IRequest<SimpleResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record SimpleResult(int Id, string Data);

[MemoryPackable]
public partial record SimpleEvent(int Id, string Data) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

#endregion

#region Handlers

public sealed class SimpleCommandHandler : IRequestHandler<SimpleCommand, SimpleResult>
{
    public ValueTask<CatgaResult<SimpleResult>> HandleAsync(SimpleCommand request, CancellationToken ct = default)
        => new(CatgaResult<SimpleResult>.Success(new SimpleResult(request.Id, $"Processed: {request.Data}")));
}

public sealed class SimpleQueryHandler : IRequestHandler<SimpleQuery, SimpleResult>
{
    public ValueTask<CatgaResult<SimpleResult>> HandleAsync(SimpleQuery request, CancellationToken ct = default)
        => new(CatgaResult<SimpleResult>.Success(new SimpleResult(request.Id, "Query result")));
}

public sealed class SimpleEventHandler : IEventHandler<SimpleEvent>
{
    public ValueTask HandleAsync(SimpleEvent @event, CancellationToken ct = default) => ValueTask.CompletedTask;
}

#endregion

#region Infrastructure

internal sealed class NoopResilienceProvider : IResiliencePipelineProvider
{
    public ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecutePersistenceNoRetryAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecutePersistenceNoRetryAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
}

#endregion
