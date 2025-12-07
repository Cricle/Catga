using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Benchmarks;

/// <summary>
/// CQRS core performance benchmarks - Command/Query/Event throughput
/// Run: dotnet run -c Release --filter *CqrsPerformance*
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class CqrsPerformanceBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;
    private BenchCommand _command = null!;
    private BenchQuery _query = null!;
    private BenchEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga().UseMemoryPack();
        services.AddSingleton<IResiliencePipelineProvider, NoopResiliencePipelineProvider>();
        services.AddScoped<IRequestHandler<BenchCommand, BenchCommandResult>, BenchCommandHandler>();
        services.AddScoped<IRequestHandler<BenchQuery, BenchQueryResult>, BenchQueryHandler>();
        services.AddScoped<IEventHandler<BenchEvent>, BenchEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
        BenchRuntime.HandlerDelayMs = 0;
        BenchRuntime.Payload = string.Empty;
        _command = new BenchCommand(123, string.Empty);
        _query = new BenchQuery(456);
        _event = new BenchEvent(789, "event-data");
    }

    [Benchmark(Baseline = true, Description = "Send Command (single)")]
    public async ValueTask<CatgaResult<BenchCommandResult>> SendCommand_Single()
    {
        return await _mediator.SendAsync<BenchCommand, BenchCommandResult>(_command);
    }

    [Benchmark(Description = "Send Query (single)")]
    public async ValueTask<CatgaResult<BenchQueryResult>> SendQuery_Single()
    {
        return await _mediator.SendAsync<BenchQuery, BenchQueryResult>(_query);
    }

    [Benchmark(Description = "Publish Event (single)")]
    public async Task PublishEvent_Single()
    {
        await _mediator.PublishAsync(_event);
    }

    [Benchmark(Description = "Send Command (batch 100)")]
    public async Task SendCommand_Batch100()
    {
        for (int i = 0; i < 100; i++)
        {
            await _mediator.SendAsync<BenchCommand, BenchCommandResult>(_command);
        }
    }

    [Benchmark(Description = "Publish Event (batch 100)")]
    public async Task PublishEvent_Batch100()
    {
        for (int i = 0; i < 100; i++)
        {
            await _mediator.PublishAsync(_event);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }
}

// Benchmark message types
[MemoryPackable]
public partial record BenchCommand(int Id, string Data) : IRequest<BenchCommandResult>;

[MemoryPackable]
public partial record BenchCommandResult(int Id, string ProcessedData);

[MemoryPackable]
public partial record BenchQuery(int Id) : IRequest<BenchQueryResult>;

[MemoryPackable]
public partial record BenchQueryResult(int Id, string Data);

[MemoryPackable]
public partial record BenchEvent(int Id, string Data) : IEvent;

// Benchmark handlers - minimal logic for pure framework overhead measurement
public class BenchCommandHandler : IRequestHandler<BenchCommand, BenchCommandResult>
{
    public ValueTask<CatgaResult<BenchCommandResult>> HandleAsync(
        BenchCommand request,
        CancellationToken cancellationToken = default)
    {
        if (BenchRuntime.HandlerDelayMs > 0)
            return new ValueTask<CatgaResult<BenchCommandResult>>(HandleWithDelayAsync(request, cancellationToken));
        var result = new BenchCommandResult(request.Id, $"Processed: {request.Data}");
        return new ValueTask<CatgaResult<BenchCommandResult>>(CatgaResult<BenchCommandResult>.Success(result));
    }

    private static async Task<CatgaResult<BenchCommandResult>> HandleWithDelayAsync(BenchCommand request, CancellationToken ct)
    {
        await Task.Delay(BenchRuntime.HandlerDelayMs, ct);
        var result = new BenchCommandResult(request.Id, $"Processed: {request.Data}");
        return CatgaResult<BenchCommandResult>.Success(result);
    }
}

public class BenchQueryHandler : IRequestHandler<BenchQuery, BenchQueryResult>
{
    public ValueTask<CatgaResult<BenchQueryResult>> HandleAsync(
        BenchQuery request,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchQueryResult(request.Id, "Query result data");
        return new ValueTask<CatgaResult<BenchQueryResult>>(CatgaResult<BenchQueryResult>.Success(result));
    }
}

public class BenchEventHandler : IEventHandler<BenchEvent>
{
    public ValueTask HandleAsync(BenchEvent @event, CancellationToken cancellationToken = default)
    {
        if (BenchRuntime.HandlerDelayMs > 0)
            return new ValueTask(Task.Delay(BenchRuntime.HandlerDelayMs, cancellationToken));
        return ValueTask.CompletedTask;
    }
}

internal static class BenchRuntime
{
    public static int HandlerDelayMs;
    public static string Payload = string.Empty;
}



