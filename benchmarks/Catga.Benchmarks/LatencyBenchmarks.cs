using System.Diagnostics;
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
/// Latency benchmarks - measure P50, P95, P99 latencies
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, warmupCount: 5, iterationCount: 100)]
public class LatencyBenchmarks
{
    private ICatgaMediator _mediator = null!;
    private IServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddCatga(options =>
        {
            options.EnableLogging = false;
            options.EnableIdempotency = false;
            options.EnableRetry = false;
        });

        services.AddTransient<IRequestHandler<LatencyTestCommand, LatencyTestResponse>, LatencyTestHandler>();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "E2E Latency: Simple Command")]
    public async Task<long> E2E_Latency_SimpleCommand()
    {
        var sw = Stopwatch.StartNew();
        await _mediator.SendAsync<LatencyTestCommand, LatencyTestResponse>(
            new LatencyTestCommand { Data = "test" });
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark(Description = "E2E Latency: With Pipeline")]
    public async Task<long> E2E_Latency_WithPipeline()
    {
        var sw = Stopwatch.StartNew();
        await _mediator.SendAsync<LatencyTestCommand, LatencyTestResponse>(
            new LatencyTestCommand { Data = "test" });
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark(Description = "P99 Latency: Under Load")]
    public async Task<long> P99_Latency_UnderLoad()
    {
        // Simulate concurrent load
        var tasks = new Task<long>[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                await _mediator.SendAsync<LatencyTestCommand, LatencyTestResponse>(
                    new LatencyTestCommand { Data = $"test-{i}" });
                sw.Stop();
                return sw.ElapsedTicks;
            });
        }

        var results = await Task.WhenAll(tasks);
        Array.Sort(results);
        return results[(int)(results.Length * 0.99)]; // P99
    }
}

public class LatencyTestCommand : ICommand<LatencyTestResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string Data { get; set; } = string.Empty;
}

public class LatencyTestResponse
{
    public string Result { get; set; } = string.Empty;
}

public class LatencyTestHandler : IRequestHandler<LatencyTestCommand, LatencyTestResponse>
{
    public Task<CatgaResult<LatencyTestResponse>> HandleAsync(
        LatencyTestCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<LatencyTestResponse>.Success(
            new LatencyTestResponse { Result = request.Data.ToUpperInvariant() }));
    }
}

