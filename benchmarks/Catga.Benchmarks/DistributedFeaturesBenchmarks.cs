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
/// Benchmarks for distributed features: Rate Limiting, Leader Election, Compensation.
/// Measures overhead of distributed coordination primitives.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class DistributedFeaturesBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private ICatgaMediator _mediator = null!;
    private IDistributedRateLimiter _rateLimiter = null!;
    private ILeaderElection _leaderElection = null!;
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

        // Register in-memory leader election
        services.AddSingleton<ILeaderElection, InMemoryLeaderElection>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
        _rateLimiter = _serviceProvider.GetRequiredService<IDistributedRateLimiter>();
        _leaderElection = _serviceProvider.GetRequiredService<ILeaderElection>();
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

    [Benchmark(Description = "Leader Election - TryAcquire")]
    public async Task<ILeadershipHandle?> LeaderElection_TryAcquire()
    {
        var handle = await _leaderElection.TryAcquireLeadershipAsync("benchmark-election");
        if (handle != null)
            await handle.DisposeAsync();
        return handle;
    }

    [Benchmark(Description = "Leader Election - IsLeader")]
    public async Task<bool> LeaderElection_IsLeader()
    {
        return await _leaderElection.IsLeaderAsync("benchmark-election");
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

public sealed class InMemoryLeaderElection : ILeaderElection
{
    private readonly Dictionary<string, (string nodeId, DateTime expiresAt)> _leaders = new();
    private readonly object _lock = new();
    private readonly string _nodeId = Guid.NewGuid().ToString("N")[..8];

    public ValueTask<ILeadershipHandle?> TryAcquireLeadershipAsync(string electionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_leaders.TryGetValue(electionId, out var entry) && entry.expiresAt > now && entry.nodeId != _nodeId)
            {
                return ValueTask.FromResult<ILeadershipHandle?>(null);
            }

            _leaders[electionId] = (_nodeId, now.AddSeconds(15));
            return ValueTask.FromResult<ILeadershipHandle?>(new InMemoryLeadershipHandle(electionId, _nodeId, this));
        }
    }

    public async ValueTask<ILeadershipHandle> AcquireLeadershipAsync(string electionId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var handle = await TryAcquireLeadershipAsync(electionId, ct);
            if (handle != null) return handle;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"Failed to acquire leadership for {electionId}");
    }

    public ValueTask<bool> IsLeaderAsync(string electionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_leaders.TryGetValue(electionId, out var entry))
            {
                return ValueTask.FromResult(entry.nodeId == _nodeId && entry.expiresAt > DateTime.UtcNow);
            }
            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<LeaderInfo?> GetLeaderAsync(string electionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_leaders.TryGetValue(electionId, out var entry) && entry.expiresAt > DateTime.UtcNow)
            {
                return ValueTask.FromResult<LeaderInfo?>(new LeaderInfo
                {
                    NodeId = entry.nodeId,
                    AcquiredAt = entry.expiresAt.AddSeconds(-15)
                });
            }
            return ValueTask.FromResult<LeaderInfo?>(null);
        }
    }

    public async IAsyncEnumerable<LeadershipChange> WatchAsync(string electionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        LeaderInfo? lastLeader = null;
        while (!ct.IsCancellationRequested)
        {
            var current = await GetLeaderAsync(electionId, ct);
            if (!Equals(lastLeader, current))
            {
                yield return new LeadershipChange
                {
                    Type = current.HasValue ? LeadershipChangeType.Elected : LeadershipChangeType.Lost,
                    PreviousLeader = lastLeader,
                    NewLeader = current,
                    Timestamp = DateTimeOffset.UtcNow
                };
                lastLeader = current;
            }
            await Task.Delay(1000, ct);
        }
    }

    internal void Release(string electionId, string nodeId)
    {
        lock (_lock)
        {
            if (_leaders.TryGetValue(electionId, out var entry) && entry.nodeId == nodeId)
            {
                _leaders.Remove(electionId);
            }
        }
    }

    internal void Extend(string electionId, string nodeId)
    {
        lock (_lock)
        {
            if (_leaders.TryGetValue(electionId, out var entry) && entry.nodeId == nodeId)
            {
                _leaders[electionId] = (nodeId, DateTime.UtcNow.AddSeconds(15));
            }
        }
    }

    private sealed class InMemoryLeadershipHandle : ILeadershipHandle
    {
        private readonly InMemoryLeaderElection _election;
        private bool _isLeader = true;

        public string ElectionId { get; }
        public string NodeId { get; }
        public bool IsLeader => _isLeader;
        public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.UtcNow;
        public event Action? OnLeadershipLost;

        public InMemoryLeadershipHandle(string electionId, string nodeId, InMemoryLeaderElection election)
        {
            ElectionId = electionId;
            NodeId = nodeId;
            _election = election;
        }

        public ValueTask ExtendAsync(CancellationToken ct = default)
        {
            _election.Extend(ElectionId, NodeId);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _isLeader = false;
            _election.Release(ElectionId, NodeId);
            OnLeadershipLost?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
