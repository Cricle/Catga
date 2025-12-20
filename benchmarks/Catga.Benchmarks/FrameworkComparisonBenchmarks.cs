using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Resilience;
using MassTransit;
using MassTransit.Mediator;
using MediatR;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

/// <summary>
/// Framework comparison: Catga vs MediatR vs MassTransit (in-memory mediator)
/// Run: dotnet run -c Release -- --filter *FrameworkComparison*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FrameworkComparisonBenchmarks
{
    private IServiceProvider _catgaProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private IServiceProvider _masstransitProvider = null!;

    private ICatgaMediator _catga = null!;
    private MediatR.IMediator _mediatr = null!;
    private MassTransit.Mediator.IMediator _masstransit = null!;

    // Messages
    private FwCatgaCommand _catgaCmd = null!;
    private FwCatgaEvent _catgaEvt = null!;
    private FwMediatRCommand _mediatrCmd = null!;
    private FwMediatRNotification _mediatrEvt = null!;
    private FwMassTransitCommand _masstransitCmd = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Catga
        var catgaServices = new ServiceCollection();
        catgaServices.AddLogging();
        catgaServices.AddCatga(o => o.Minimal()).UseMemoryPack();
        catgaServices.AddSingleton<IResiliencePipelineProvider, NoopResilienceProvider>();
        catgaServices.AddScoped<Catga.Abstractions.IRequestHandler<FwCatgaCommand, FwResult>, FwCatgaCommandHandler>();
        catgaServices.AddScoped<IEventHandler<FwCatgaEvent>, FwCatgaEventHandler>();
        _catgaProvider = catgaServices.BuildServiceProvider();
        _catga = _catgaProvider.GetRequiredService<ICatgaMediator>();

        // MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddLogging();
        mediatrServices.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(FrameworkComparisonBenchmarks).Assembly));
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();

        // MassTransit
        var masstransitServices = new ServiceCollection();
        masstransitServices.AddLogging();
        masstransitServices.AddMediator(cfg =>
        {
            cfg.AddConsumer<FwMassTransitCommandHandler>();
        });
        _masstransitProvider = masstransitServices.BuildServiceProvider();
        _masstransit = _masstransitProvider.GetRequiredService<MassTransit.Mediator.IMediator>();

        // Create messages
        _catgaCmd = new FwCatgaCommand(1, "test");
        _catgaEvt = new FwCatgaEvent(1, "data");
        _mediatrCmd = new FwMediatRCommand(1, "test");
        _mediatrEvt = new FwMediatRNotification(1, "data");
        _masstransitCmd = new FwMassTransitCommand(1, "test");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_masstransitProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            (_masstransitProvider as IDisposable)?.Dispose();

        (_catgaProvider as IDisposable)?.Dispose();
        (_mediatrProvider as IDisposable)?.Dispose();
    }

    #region Single Command

    [Benchmark(Baseline = true, Description = "Catga")]
    [BenchmarkCategory("Command")]
    public ValueTask<CatgaResult<FwResult>> Catga_Command()
        => _catga.SendAsync<FwCatgaCommand, FwResult>(_catgaCmd);

    [Benchmark(Description = "MediatR")]
    [BenchmarkCategory("Command")]
    public Task<FwMediatRResult> MediatR_Command()
        => _mediatr.Send(_mediatrCmd);

    [Benchmark(Description = "MassTransit")]
    [BenchmarkCategory("Command")]
    public async Task<FwMassTransitResult> MassTransit_Command()
    {
        var client = _masstransit.CreateRequestClient<FwMassTransitCommand>();
        var response = await client.GetResponse<FwMassTransitResult>(_masstransitCmd);
        return response.Message;
    }

    #endregion

    #region Event/Notification

    [Benchmark(Baseline = true, Description = "Catga")]
    [BenchmarkCategory("Event")]
    public Task Catga_Event()
        => _catga.PublishAsync(_catgaEvt);

    [Benchmark(Description = "MediatR")]
    [BenchmarkCategory("Event")]
    public Task MediatR_Event()
        => _mediatr.Publish(_mediatrEvt);

    #endregion

    #region Batch 100

    [Benchmark(Baseline = true, Description = "Catga")]
    [BenchmarkCategory("Batch100")]
    public async Task Catga_Batch100()
    {
        for (int i = 0; i < 100; i++)
            await _catga.SendAsync<FwCatgaCommand, FwResult>(_catgaCmd);
    }

    [Benchmark(Description = "MediatR")]
    [BenchmarkCategory("Batch100")]
    public async Task MediatR_Batch100()
    {
        for (int i = 0; i < 100; i++)
            await _mediatr.Send(_mediatrCmd);
    }

    [Benchmark(Description = "MassTransit")]
    [BenchmarkCategory("Batch100")]
    public async Task MassTransit_Batch100()
    {
        var client = _masstransit.CreateRequestClient<FwMassTransitCommand>();
        for (int i = 0; i < 100; i++)
        {
            var response = await client.GetResponse<FwMassTransitResult>(_masstransitCmd);
        }
    }

    #endregion
}

#region Catga Types

[MemoryPackable]
public partial record FwCatgaCommand(int Id, string Data) : Catga.Abstractions.IRequest<FwResult>
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record FwCatgaEvent(int Id, string Data) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
}

[MemoryPackable]
public partial record FwResult(int Id, string Data);

public sealed class FwCatgaCommandHandler : Catga.Abstractions.IRequestHandler<FwCatgaCommand, FwResult>
{
    public ValueTask<CatgaResult<FwResult>> HandleAsync(FwCatgaCommand request, CancellationToken ct = default)
        => new(CatgaResult<FwResult>.Success(new FwResult(request.Id, $"Processed: {request.Data}")));
}

public sealed class FwCatgaEventHandler : IEventHandler<FwCatgaEvent>
{
    public ValueTask HandleAsync(FwCatgaEvent @event, CancellationToken ct = default) => ValueTask.CompletedTask;
}

#endregion

#region MediatR Types

public record FwMediatRCommand(int Id, string Data) : MediatR.IRequest<FwMediatRResult>;
public record FwMediatRResult(int Id, string Data);
public record FwMediatRNotification(int Id, string Data) : INotification;

public sealed class FwMediatRCommandHandler : MediatR.IRequestHandler<FwMediatRCommand, FwMediatRResult>
{
    public Task<FwMediatRResult> Handle(FwMediatRCommand request, CancellationToken ct)
        => Task.FromResult(new FwMediatRResult(request.Id, $"Processed: {request.Data}"));
}

public sealed class FwMediatRNotificationHandler : INotificationHandler<FwMediatRNotification>
{
    public Task Handle(FwMediatRNotification notification, CancellationToken ct) => Task.CompletedTask;
}

#endregion

#region MassTransit Types

public record FwMassTransitCommand(int Id, string Data);
public record FwMassTransitResult(int Id, string Data);

public sealed class FwMassTransitCommandHandler : MassTransit.IConsumer<FwMassTransitCommand>
{
    public Task Consume(ConsumeContext<FwMassTransitCommand> context)
        => context.RespondAsync(new FwMassTransitResult(context.Message.Id, $"Processed: {context.Message.Data}"));
}

#endregion
