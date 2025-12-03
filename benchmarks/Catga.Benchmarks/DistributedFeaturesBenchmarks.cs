using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline.Behaviors;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmarks for distributed features: Rate Limiting, Compensation.
/// Measures overhead of distributed coordination primitives.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class DistributedFeaturesBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;
    private IDistributedRateLimiter _rateLimiter = null!;
    private DistributedCommand _command = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Configure Catga with compensation
        services.AddCatga()
            .UseMemoryPack()
            .UseAutoCompensation();

        // Register handlers
        services.AddScoped<IRequestHandler<DistributedCommand, DistributedResult>, DistributedCommandHandler>();
        services.AddScoped<IEventHandler<DistributedEvent>, DistributedEventHandler>();

        // Register compensation publisher
        services.AddSingleton<ICompensationPublisher<DistributedCommand>, DistributedCommandCompensation>();

        // Register in-memory rate limiter
        services.AddSingleton<IDistributedRateLimiter, InMemoryRateLimiter>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
        _rateLimiter = _serviceProvider.GetRequiredService<IDistributedRateLimiter>();
        _command = new DistributedCommand("test-123", "benchmark-data");
    }

    [Benchmark(Baseline = true, Description = "Command without compensation")]
    public async Task<CatgaResult<DistributedResult>> Command_NoCompensation()
    {
        return await _mediator.SendAsync<DistributedCommand, DistributedResult>(_command);
    }

    [Benchmark(Description = "Rate Limiter - TryAcquire")]
    public async Task<RateLimitResult> RateLimiter_TryAcquire()
    {
        return await _rateLimiter.TryAcquireAsync("benchmark-key");
    }

    [Benchmark(Description = "Rate Limiter - GetStatistics")]
    public async Task<RateLimitStatistics?> RateLimiter_GetStatistics()
    {
        return await _rateLimiter.GetStatisticsAsync("benchmark-key");
    }

    [Benchmark(Description = "Rate Limiter - Batch 100")]
    public async Task RateLimiter_Batch100()
    {
        for (int i = 0; i < 100; i++)
        {
            await _rateLimiter.TryAcquireAsync($"batch-key-{i % 10}");
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
public partial record DistributedCommand(string Id, string Data) : IRequest<DistributedResult>;

[MemoryPackable]
public partial record DistributedResult(string Id, bool Success);

[MemoryPackable]
public partial record DistributedEvent(string Id, string Reason) : IEvent;

[MemoryPackable]
public partial record DistributedFailedEvent(string Id, string Reason, DateTime FailedAt) : IEvent;

// Handlers
public class DistributedCommandHandler : IRequestHandler<DistributedCommand, DistributedResult>
{
    public Task<CatgaResult<DistributedResult>> HandleAsync(DistributedCommand request, CancellationToken ct = default)
    {
        return Task.FromResult(CatgaResult<DistributedResult>.Success(new DistributedResult(request.Id, true)));
    }
}

public class DistributedEventHandler : IEventHandler<DistributedEvent>
{
    public Task HandleAsync(DistributedEvent @event, CancellationToken ct = default) => Task.CompletedTask;
}

// Compensation
public class DistributedCommandCompensation : CompensationPublisher<DistributedCommand, DistributedFailedEvent>
{
    protected override DistributedFailedEvent? CreateCompensationEvent(DistributedCommand request, string? error)
        => new(request.Id, error ?? "Unknown", DateTime.UtcNow);
}

// In-memory implementations for benchmarking
public sealed class InMemoryRateLimiter : IDistributedRateLimiter
{
    private readonly Dictionary<string, (long count, DateTime resetAt)> _counters = new();
    private readonly object _lock = new();

    public ValueTask<RateLimitResult> TryAcquireAsync(string key, int permits = 1, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (!_counters.TryGetValue(key, out var entry) || entry.resetAt < now)
            {
                _counters[key] = (permits, now.AddMinutes(1));
                return ValueTask.FromResult(RateLimitResult.Acquired(100 - permits));
            }

            if (entry.count + permits <= 100)
            {
                _counters[key] = (entry.count + permits, entry.resetAt);
                return ValueTask.FromResult(RateLimitResult.Acquired(100 - entry.count - permits));
            }

            return ValueTask.FromResult(RateLimitResult.Rejected(
                RateLimitRejectionReason.RateLimitExceeded,
                entry.resetAt - now));
        }
    }

    public async ValueTask<RateLimitResult> WaitAsync(string key, int permits = 1, TimeSpan timeout = default, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout == default ? TimeSpan.FromSeconds(30) : timeout);
        while (DateTime.UtcNow < deadline)
        {
            var result = await TryAcquireAsync(key, permits, ct);
            if (result.IsAcquired) return result;
            await Task.Delay(10, ct);
        }
        return RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
    }

    public ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_counters.TryGetValue(key, out var entry))
            {
                return ValueTask.FromResult<RateLimitStatistics?>(new RateLimitStatistics
                {
                    CurrentCount = entry.count,
                    Limit = 100,
                    ResetAfter = entry.resetAt - DateTime.UtcNow
                });
            }
            return ValueTask.FromResult<RateLimitStatistics?>(null);
        }
    }
}
