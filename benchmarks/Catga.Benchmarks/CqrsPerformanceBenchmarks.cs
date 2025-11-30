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
/// Target: less than 1 microsecond per operation, zero allocations
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
    private ActivityListener? _listener;

    private static bool Quick => string.Equals(Environment.GetEnvironmentVariable("E2E_QUICK"), "true", StringComparison.OrdinalIgnoreCase);

    [ParamsSource(nameof(BoolOffThenOn))]
    public bool TracingEnabled { get; set; }

    [ParamsSource(nameof(BoolOffThenOn))]
    public bool ResilienceEnabled { get; set; }

    [ParamsSource(nameof(PayloadCases))]
    public int PayloadBytes { get; set; }

    [ParamsSource(nameof(DelayCases))]
    public int HandlerDelayMs { get; set; }

    public static IEnumerable<bool> BoolOffThenOn() => Quick ? new[] { false } : new[] { false, true };
    public static IEnumerable<int> PayloadCases() => Quick ? new[] { 0, 512 } : new[] { 0, 512, 4096 };
    public static IEnumerable<int> DelayCases() => Quick ? new[] { 0, 1 } : new[] { 0, 1, 5 };

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddCatga().UseMemoryPack();
        if (ResilienceEnabled)
        {
            builder.UseResilience();
        }
        else
        {
            services.AddSingleton<IResiliencePipelineProvider, NoopResiliencePipelineProvider>();
        }
        services.AddScoped<IRequestHandler<BenchCommand, BenchCommandResult>, BenchCommandHandler>();
        services.AddScoped<IRequestHandler<BenchQuery, BenchQueryResult>, BenchQueryHandler>();
        services.AddScoped<IEventHandler<BenchEvent>, BenchEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
        BenchRuntime.HandlerDelayMs = HandlerDelayMs;
        BenchRuntime.Payload = PayloadBytes > 0 ? new string('x', PayloadBytes) : string.Empty;
        _command = new BenchCommand(123, BenchRuntime.Payload);
        _query = new BenchQuery(456);
        _event = new BenchEvent(789, "event-data");

        if (TracingEnabled)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = _ => { }
            };
            ActivitySource.AddActivityListener(_listener);
        }
    }

    [Benchmark(Baseline = true, Description = "Send Command (single)")]
    public async Task<CatgaResult<BenchCommandResult>> SendCommand_Single()
    {
        return await _mediator.SendAsync<BenchCommand, BenchCommandResult>(_command);
    }

    [Benchmark(Description = "Send Query (single)")]
    public async Task<CatgaResult<BenchQueryResult>> SendQuery_Single()
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
        {
            disposable.Dispose();
        }
        _listener?.Dispose();
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
    public Task<CatgaResult<BenchCommandResult>> HandleAsync(
        BenchCommand request,
        CancellationToken cancellationToken = default)
    {
        if (BenchRuntime.HandlerDelayMs > 0)
            return HandleWithDelayAsync(request, cancellationToken);
        var result = new BenchCommandResult(request.Id, $"Processed: {request.Data}");
        return Task.FromResult(CatgaResult<BenchCommandResult>.Success(result));
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
    public Task<CatgaResult<BenchQueryResult>> HandleAsync(
        BenchQuery request,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchQueryResult(request.Id, "Query result data");
        return Task.FromResult(CatgaResult<BenchQueryResult>.Success(result));
    }
}

public class BenchEventHandler : IEventHandler<BenchEvent>
{
    public Task HandleAsync(BenchEvent @event, CancellationToken cancellationToken = default)
    {
        if (BenchRuntime.HandlerDelayMs > 0)
            return Task.Delay(BenchRuntime.HandlerDelayMs, cancellationToken);
        return Task.CompletedTask;
    }
}

internal static class BenchRuntime
{
    public static int HandlerDelayMs;
    public static string Payload = string.Empty;
}



