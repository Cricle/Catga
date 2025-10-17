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
/// CQRS core performance benchmarks - Command/Query/Event throughput
/// Target: less than 1 microsecond per operation, zero allocations
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
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
        services.AddCatga().UseMemoryPack(); // Add serializer for CATGA002
        services.AddScoped<IRequestHandler<BenchCommand, BenchCommandResult>, BenchCommandHandler>();
        services.AddScoped<IRequestHandler<BenchQuery, BenchQueryResult>, BenchQueryHandler>();
        services.AddScoped<IEventHandler<BenchEvent>, BenchEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
        _command = new BenchCommand(123, "test-data");
        _query = new BenchQuery(456);
        _event = new BenchEvent(789, "event-data");
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
    }
}

// Benchmark message types
[MemoryPackable]
public partial record BenchCommand(int Id, string Data) : IRequest<BenchCommandResult>
{
    public string MessageId { get; init; } = MessageExtensions.NewMessageId();
}

[MemoryPackable]
public partial record BenchCommandResult(int Id, string ProcessedData);

[MemoryPackable]
public partial record BenchQuery(int Id) : IRequest<BenchQueryResult>
{
    public string MessageId { get; init; } = MessageExtensions.NewMessageId();
}

[MemoryPackable]
public partial record BenchQueryResult(int Id, string Data);

[MemoryPackable]
public partial record BenchEvent(int Id, string Data) : IEvent
{
    public string MessageId { get; init; } = MessageExtensions.NewMessageId();
}

// Benchmark handlers - minimal logic for pure framework overhead measurement
public class BenchCommandHandler : IRequestHandler<BenchCommand, BenchCommandResult>
{
    public Task<CatgaResult<BenchCommandResult>> HandleAsync(
        BenchCommand request,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchCommandResult(request.Id, $"Processed: {request.Data}");
        return Task.FromResult(CatgaResult<BenchCommandResult>.Success(result));
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
        // Minimal processing
        return Task.CompletedTask;
    }
}

