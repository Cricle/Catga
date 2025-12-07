using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Resilience;
using MediatR;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

/// <summary>
/// Fair comparison benchmark between Catga and MediatR.
/// Both use in-memory mediator pattern without network transport.
/// Run: dotnet run -c Release --filter *MediatRComparison*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class MediatRComparisonBenchmarks
{
    private IServiceProvider _catgaProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private ICatgaMediator _catgaMediator = null!;
    private IMediator _mediatr = null!;

    // Catga messages
    private CatgaTestCommand _catgaCommand = null!;
    private CatgaTestQuery _catgaQuery = null!;
    private CatgaTestEvent _catgaEvent = null!;

    // MediatR messages
    private MediatRTestCommand _mediatrCommand = null!;
    private MediatRTestQuery _mediatrQuery = null!;
    private MediatRTestNotification _mediatrNotification = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup Catga
        var catgaServices = new ServiceCollection();
        catgaServices.AddLogging();
        catgaServices.AddCatga().UseMemoryPack();
        catgaServices.AddSingleton<IResiliencePipelineProvider, NoopResiliencePipelineProvider>();
        catgaServices.AddScoped<Catga.Abstractions.IRequestHandler<CatgaTestCommand, CatgaTestResult>, CatgaTestCommandHandler>();
        catgaServices.AddScoped<Catga.Abstractions.IRequestHandler<CatgaTestQuery, CatgaTestResult>, CatgaTestQueryHandler>();
        catgaServices.AddScoped<IEventHandler<CatgaTestEvent>, CatgaTestEventHandler>();
        _catgaProvider = catgaServices.BuildServiceProvider();
        _catgaMediator = _catgaProvider.GetRequiredService<ICatgaMediator>();

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddLogging();
        mediatrServices.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatRComparisonBenchmarks).Assembly));
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<IMediator>();

        // Create test messages
        _catgaCommand = new CatgaTestCommand(1, "test");
        _catgaQuery = new CatgaTestQuery(1);
        _catgaEvent = new CatgaTestEvent(1, "event");

        _mediatrCommand = new MediatRTestCommand(1, "test");
        _mediatrQuery = new MediatRTestQuery(1);
        _mediatrNotification = new MediatRTestNotification(1, "event");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_catgaProvider as IDisposable)?.Dispose();
        (_mediatrProvider as IDisposable)?.Dispose();
    }

    #region Command Benchmarks

    [Benchmark(Description = "Catga Send Command")]
    public async ValueTask<CatgaResult<CatgaTestResult>> Catga_SendCommand()
    {
        return await _catgaMediator.SendAsync<CatgaTestCommand, CatgaTestResult>(_catgaCommand);
    }

    [Benchmark(Description = "MediatR Send Command")]
    public async Task<MediatRTestResult> MediatR_SendCommand()
    {
        return await _mediatr.Send(_mediatrCommand);
    }

    #endregion

    #region Query Benchmarks

    [Benchmark(Description = "Catga Send Query")]
    public async ValueTask<CatgaResult<CatgaTestResult>> Catga_SendQuery()
    {
        return await _catgaMediator.SendAsync<CatgaTestQuery, CatgaTestResult>(_catgaQuery);
    }

    [Benchmark(Description = "MediatR Send Query")]
    public async Task<MediatRTestResult> MediatR_SendQuery()
    {
        return await _mediatr.Send(_mediatrQuery);
    }

    #endregion

    #region Event/Notification Benchmarks

    [Benchmark(Description = "Catga Publish Event")]
    public async Task Catga_PublishEvent()
    {
        await _catgaMediator.PublishAsync(_catgaEvent);
    }

    [Benchmark(Description = "MediatR Publish Notification")]
    public async Task MediatR_PublishNotification()
    {
        await _mediatr.Publish(_mediatrNotification);
    }

    #endregion

    #region Batch Benchmarks

    [Benchmark(Description = "Catga Batch 100 Commands")]
    public async Task Catga_Batch100Commands()
    {
        for (int i = 0; i < 100; i++)
        {
            await _catgaMediator.SendAsync<CatgaTestCommand, CatgaTestResult>(_catgaCommand);
        }
    }

    [Benchmark(Description = "MediatR Batch 100 Commands")]
    public async Task MediatR_Batch100Commands()
    {
        for (int i = 0; i < 100; i++)
        {
            await _mediatr.Send(_mediatrCommand);
        }
    }

    #endregion
}

#region Catga Message Types

[MemoryPackable]
public partial record CatgaTestCommand(int Id, string Data) : Catga.Abstractions.IRequest<CatgaTestResult>;

[MemoryPackable]
public partial record CatgaTestQuery(int Id) : Catga.Abstractions.IRequest<CatgaTestResult>;

[MemoryPackable]
public partial record CatgaTestResult(int Id, string ProcessedData);

[MemoryPackable]
public partial record CatgaTestEvent(int Id, string Data) : IEvent;

public class CatgaTestCommandHandler : Catga.Abstractions.IRequestHandler<CatgaTestCommand, CatgaTestResult>
{
    public ValueTask<CatgaResult<CatgaTestResult>> HandleAsync(CatgaTestCommand request, CancellationToken ct = default)
    {
        var result = new CatgaTestResult(request.Id, $"Processed: {request.Data}");
        return new ValueTask<CatgaResult<CatgaTestResult>>(CatgaResult<CatgaTestResult>.Success(result));
    }
}

public class CatgaTestQueryHandler : Catga.Abstractions.IRequestHandler<CatgaTestQuery, CatgaTestResult>
{
    public ValueTask<CatgaResult<CatgaTestResult>> HandleAsync(CatgaTestQuery request, CancellationToken ct = default)
    {
        var result = new CatgaTestResult(request.Id, "Query result");
        return new ValueTask<CatgaResult<CatgaTestResult>>(CatgaResult<CatgaTestResult>.Success(result));
    }
}

public class CatgaTestEventHandler : IEventHandler<CatgaTestEvent>
{
    public ValueTask HandleAsync(CatgaTestEvent @event, CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }
}

#endregion

#region MediatR Message Types

public record MediatRTestCommand(int Id, string Data) : MediatR.IRequest<MediatRTestResult>;

public record MediatRTestQuery(int Id) : MediatR.IRequest<MediatRTestResult>;

public record MediatRTestResult(int Id, string ProcessedData);

public record MediatRTestNotification(int Id, string Data) : INotification;

public class MediatRTestCommandHandler : MediatR.IRequestHandler<MediatRTestCommand, MediatRTestResult>
{
    public Task<MediatRTestResult> Handle(MediatRTestCommand request, CancellationToken ct)
    {
        var result = new MediatRTestResult(request.Id, $"Processed: {request.Data}");
        return Task.FromResult(result);
    }
}

public class MediatRTestQueryHandler : MediatR.IRequestHandler<MediatRTestQuery, MediatRTestResult>
{
    public Task<MediatRTestResult> Handle(MediatRTestQuery request, CancellationToken ct)
    {
        var result = new MediatRTestResult(request.Id, "Query result");
        return Task.FromResult(result);
    }
}

public class MediatRTestNotificationHandler : INotificationHandler<MediatRTestNotification>
{
    public Task Handle(MediatRTestNotification notification, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

#endregion
