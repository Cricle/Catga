using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Benchmarks;

/// <summary>
/// Throughput benchmarks - measure ops/second under various loads
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, warmupCount: 3, iterationCount: 10)]
public class ThroughputBenchmarks
{
    private ICatgaMediator _mediator = null!;
    private IServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddCatga(options =>
        {
            options.EnableLogging = false; // Disable for max performance
            options.EnableIdempotency = false;
            options.EnableRetry = false;
        });

        services.AddTransient<IRequestHandler<SimpleCommand, SimpleResponse>, SimpleCommandHandler>();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Throughput: 1K requests")]
    [Arguments(1_000)]
    public async Task Throughput_1K(int count)
    {
        var tasks = new Task<CatgaResult<SimpleResponse>>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = _mediator.SendAsync<SimpleCommand, SimpleResponse>(
                new SimpleCommand { Value = i }).AsTask();
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Throughput: 10K requests")]
    [Arguments(10_000)]
    public async Task Throughput_10K(int count)
    {
        var tasks = new Task<CatgaResult<SimpleResponse>>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = _mediator.SendAsync<SimpleCommand, SimpleResponse>(
                new SimpleCommand { Value = i }).AsTask();
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Throughput: 100K requests")]
    [Arguments(100_000)]
    public async Task Throughput_100K(int count)
    {
        // Process in batches to avoid excessive memory
        const int batchSize = 10_000;
        for (int batch = 0; batch < count; batch += batchSize)
        {
            var remaining = Math.Min(batchSize, count - batch);
            var tasks = new Task<CatgaResult<SimpleResponse>>[remaining];

            for (int i = 0; i < remaining; i++)
            {
                tasks[i] = _mediator.SendAsync<SimpleCommand, SimpleResponse>(
                    new SimpleCommand { Value = batch + i }).AsTask();
            }
            await Task.WhenAll(tasks);
        }
    }

    [Benchmark(Description = "Throughput: Sequential 1K")]
    [Arguments(1_000)]
    public async Task Throughput_Sequential_1K(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await _mediator.SendAsync<SimpleCommand, SimpleResponse>(
                new SimpleCommand { Value = i });
        }
    }
}

public class SimpleCommand : ICommand<SimpleResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public int Value { get; set; }
}

public class SimpleResponse
{
    public int Result { get; set; }
}

public class SimpleCommandHandler : IRequestHandler<SimpleCommand, SimpleResponse>
{
    public Task<CatgaResult<SimpleResponse>> HandleAsync(
        SimpleCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<SimpleResponse>.Success(
            new SimpleResponse { Result = request.Value * 2 }));
    }
}

