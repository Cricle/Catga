using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Benchmarks;

/// <summary>
/// Pipeline benchmarks - measure impact of behaviors
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, warmupCount: 3, iterationCount: 10)]
public class PipelineBenchmarks
{
    private ICatgaMediator _noPipelineMediator = null!;
    private ICatgaMediator _withPipelineMediator = null!;
    private IServiceProvider _noPipelineProvider = null!;
    private IServiceProvider _withPipelineProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup without pipeline
        var noPipelineServices = new ServiceCollection();
        noPipelineServices.AddCatga(options =>
        {
            options.EnableLogging = false;
            options.EnableValidation = false;
            options.EnableRetry = false;
            options.EnableIdempotency = false;
        });
        noPipelineServices.AddTransient<IRequestHandler<PipelineTestCommand, PipelineTestResponse>, PipelineTestHandler>();
        noPipelineServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        _noPipelineProvider = noPipelineServices.BuildServiceProvider();
        _noPipelineMediator = _noPipelineProvider.GetRequiredService<ICatgaMediator>();

        // Setup with full pipeline
        var withPipelineServices = new ServiceCollection();
        withPipelineServices.AddCatga(options =>
        {
            options.EnableLogging = true;
            options.EnableValidation = true;
            options.EnableRetry = true;
            options.EnableIdempotency = true;
            options.MaxRetryAttempts = 3;
        });
        withPipelineServices.AddTransient<IRequestHandler<PipelineTestCommand, PipelineTestResponse>, PipelineTestHandler>();
        withPipelineServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _withPipelineProvider = withPipelineServices.BuildServiceProvider();
        _withPipelineMediator = _withPipelineProvider.GetRequiredService<ICatgaMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_noPipelineProvider as IDisposable)?.Dispose();
        (_withPipelineProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "No Pipeline")]
    public async Task NoPipeline()
    {
        await _noPipelineMediator.SendAsync<PipelineTestCommand, PipelineTestResponse>(
            new PipelineTestCommand { Value = 42 });
    }

    [Benchmark(Description = "With Full Pipeline")]
    public async Task WithFullPipeline()
    {
        await _withPipelineMediator.SendAsync<PipelineTestCommand, PipelineTestResponse>(
            new PipelineTestCommand { Value = 42 });
    }

    [Benchmark(Description = "Pipeline Overhead (1K requests)")]
    public async Task PipelineOverhead_1K()
    {
        var tasks = new Task<CatgaResult<PipelineTestResponse>>[1000];
        for (int i = 0; i < 1000; i++)
        {
            tasks[i] = _withPipelineMediator.SendAsync<PipelineTestCommand, PipelineTestResponse>(
                new PipelineTestCommand { Value = i }).AsTask();
        }
        await Task.WhenAll(tasks);
    }
}

public class PipelineTestCommand : ICommand<PipelineTestResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public int Value { get; set; }
}

public class PipelineTestResponse
{
    public int Result { get; set; }
}

public class PipelineTestHandler : IRequestHandler<PipelineTestCommand, PipelineTestResponse>
{
    public Task<CatgaResult<PipelineTestResponse>> HandleAsync(
        PipelineTestCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<PipelineTestResponse>.Success(
            new PipelineTestResponse { Result = request.Value * 2 }));
    }
}

