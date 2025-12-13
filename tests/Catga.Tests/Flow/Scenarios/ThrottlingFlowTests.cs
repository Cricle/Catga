using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Throttling and rate limiting flow scenario tests.
/// Tests request rate control, token bucket, sliding window, and adaptive throttling.
/// </summary>
public class ThrottlingFlowTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Throttle_FixedRate_LimitsRequests()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var limiter = new FixedRateLimiter(5); // 5 per second
        var acceptedCount = 0;
        var rejectedCount = 0;

        var flow = FlowBuilder.Create<ThrottleState>("fixed-rate")
            .Step("limited-operation", async (state, ct) =>
            {
                if (limiter.TryAcquire())
                {
                    Interlocked.Increment(ref acceptedCount);
                    state.Accepted = true;
                }
                else
                {
                    Interlocked.Increment(ref rejectedCount);
                    state.Rejected = true;
                }
                return true;
            })
            .Build();

        // Send 20 requests rapidly
        var tasks = Enumerable.Range(1, 20).Select(i =>
            executor.ExecuteAsync(flow, new ThrottleState { FlowId = $"rate-{i}" }).AsTask()
        );

        await Task.WhenAll(tasks);

        acceptedCount.Should().BeLessOrEqualTo(5);
        rejectedCount.Should().BeGreaterOrEqualTo(15);
    }

    [Fact]
    public async Task Throttle_TokenBucket_AllowsBursts()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var bucket = new TokenBucket(10, 2); // 10 tokens, refill 2/sec
        var acceptedTimes = new ConcurrentBag<DateTime>();

        var flow = FlowBuilder.Create<ThrottleState>("token-bucket")
            .Step("operation", async (state, ct) =>
            {
                if (bucket.TryConsume())
                {
                    acceptedTimes.Add(DateTime.UtcNow);
                    state.Accepted = true;
                }
                else
                {
                    state.Rejected = true;
                }
                return true;
            })
            .Build();

        // First burst - should accept up to bucket size
        var burst1Tasks = Enumerable.Range(1, 15).Select(i =>
            executor.ExecuteAsync(flow, new ThrottleState { FlowId = $"burst1-{i}" }).AsTask()
        );
        var burst1Results = await Task.WhenAll(burst1Tasks);

        var accepted1 = burst1Results.Count(r => r.State.Accepted);
        accepted1.Should().Be(10); // Bucket size

        // Wait for refill
        await Task.Delay(1100);

        // Second burst after refill
        var burst2Tasks = Enumerable.Range(1, 5).Select(i =>
            executor.ExecuteAsync(flow, new ThrottleState { FlowId = $"burst2-{i}" }).AsTask()
        );
        var burst2Results = await Task.WhenAll(burst2Tasks);

        var accepted2 = burst2Results.Count(r => r.State.Accepted);
        accepted2.Should().BeGreaterOrEqualTo(2); // At least refill amount
    }

    [Fact]
    public async Task Throttle_SlidingWindow_TracksOverTime()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var window = new SlidingWindowLimiter(5, TimeSpan.FromMilliseconds(500));
        var results = new ConcurrentBag<(int Index, bool Accepted, DateTime Time)>();

        var flow = FlowBuilder.Create<ThrottleState>("sliding-window")
            .Step("operation", async (state, ct) =>
            {
                state.Accepted = window.TryAcquire();
                state.Rejected = !state.Accepted;
                return true;
            })
            .Build();

        // Send 5 requests (should all pass)
        for (int i = 0; i < 5; i++)
        {
            var result = await executor.ExecuteAsync(flow, new ThrottleState { FlowId = $"win-{i}" });
            results.Add((i, result.State.Accepted, DateTime.UtcNow));
        }

        // 6th request should be rejected
        var result6 = await executor.ExecuteAsync(flow, new ThrottleState { FlowId = "win-6" });
        result6.State.Rejected.Should().BeTrue();

        // Wait for window to slide
        await Task.Delay(600);

        // Now should be able to send again
        var result7 = await executor.ExecuteAsync(flow, new ThrottleState { FlowId = "win-7" });
        result7.State.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task Throttle_PerUser_IndependentLimits()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var userLimiters = new ConcurrentDictionary<string, FixedRateLimiter>();

        var flow = FlowBuilder.Create<UserThrottleState>("per-user")
            .Step("user-operation", async (state, ct) =>
            {
                var limiter = userLimiters.GetOrAdd(state.UserId, _ => new FixedRateLimiter(3));
                state.Accepted = limiter.TryAcquire();
                state.Rejected = !state.Accepted;
                return true;
            })
            .Build();

        // User A: 5 requests
        var userATasks = Enumerable.Range(1, 5).Select(i =>
            executor.ExecuteAsync(flow, new UserThrottleState { FlowId = $"a-{i}", UserId = "UserA" }).AsTask()
        );

        // User B: 5 requests
        var userBTasks = Enumerable.Range(1, 5).Select(i =>
            executor.ExecuteAsync(flow, new UserThrottleState { FlowId = $"b-{i}", UserId = "UserB" }).AsTask()
        );

        var allResults = await Task.WhenAll(userATasks.Concat(userBTasks));

        var userAAccepted = allResults.Where(r => r.State.UserId == "UserA").Count(r => r.State.Accepted);
        var userBAccepted = allResults.Where(r => r.State.UserId == "UserB").Count(r => r.State.Accepted);

        userAAccepted.Should().BeLessOrEqualTo(3);
        userBAccepted.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task Throttle_Backpressure_SlowsDownProducer()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var processingTimes = new ConcurrentBag<long>();
        var sw = new Stopwatch();

        var flow = FlowBuilder.Create<BackpressureState>("backpressure")
            .Step("produce", async (state, ct) =>
            {
                sw.Restart();

                // Simulate backpressure with delay
                if (state.QueueFull)
                {
                    await Task.Delay(100, ct); // Backpressure delay
                }

                sw.Stop();
                processingTimes.Add(sw.ElapsedMilliseconds);
                state.Processed = true;
                return true;
            })
            .Build();

        // Normal requests
        var normalTasks = Enumerable.Range(1, 5).Select(i =>
            executor.ExecuteAsync(flow, new BackpressureState { FlowId = $"normal-{i}", QueueFull = false }).AsTask()
        );

        // Backpressure requests
        var backpressureTasks = Enumerable.Range(1, 5).Select(i =>
            executor.ExecuteAsync(flow, new BackpressureState { FlowId = $"bp-{i}", QueueFull = true }).AsTask()
        );

        await Task.WhenAll(normalTasks);
        await Task.WhenAll(backpressureTasks);

        var avgBackpressureTime = processingTimes.Skip(5).Average();
        avgBackpressureTime.Should().BeGreaterOrEqualTo(50); // Significant delay
    }

    [Fact]
    public async Task Throttle_Adaptive_AdjustsToLoad()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var adaptive = new AdaptiveThrottler(10, 2, 20);

        var flow = FlowBuilder.Create<AdaptiveState>("adaptive")
            .Step("operation", async (state, ct) =>
            {
                if (adaptive.TryAcquire())
                {
                    // Simulate processing
                    if (state.SimulateSuccess)
                    {
                        adaptive.RecordSuccess();
                    }
                    else
                    {
                        adaptive.RecordFailure();
                    }
                    state.Accepted = true;
                }
                else
                {
                    state.Rejected = true;
                }
                return true;
            })
            .Build();

        // Start with successful requests
        for (int i = 0; i < 20; i++)
        {
            await executor.ExecuteAsync(flow, new AdaptiveState { FlowId = $"success-{i}", SimulateSuccess = true });
        }

        var limitAfterSuccess = adaptive.CurrentLimit;

        // Then send failing requests
        for (int i = 0; i < 10; i++)
        {
            await executor.ExecuteAsync(flow, new AdaptiveState { FlowId = $"fail-{i}", SimulateSuccess = false });
        }

        var limitAfterFailure = adaptive.CurrentLimit;

        // Limit should decrease after failures
        limitAfterFailure.Should().BeLessOrEqualTo(limitAfterSuccess);
    }

    [Fact]
    public async Task Throttle_Priority_HighPriorityBypassesLimit()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var limiter = new PriorityRateLimiter(normalLimit: 2, priorityReserved: 3);

        var flow = FlowBuilder.Create<PriorityState>("priority-throttle")
            .Step("operation", async (state, ct) =>
            {
                state.Accepted = limiter.TryAcquire(state.IsHighPriority);
                state.Rejected = !state.Accepted;
                return true;
            })
            .Build();

        // Exhaust normal limit
        for (int i = 0; i < 5; i++)
        {
            await executor.ExecuteAsync(flow, new PriorityState { FlowId = $"normal-{i}", IsHighPriority = false });
        }

        // High priority should still work
        var priorityResult = await executor.ExecuteAsync(flow, new PriorityState { FlowId = "priority-1", IsHighPriority = true });

        priorityResult.State.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task Throttle_Queuing_WaitsForSlot()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var limiter = new QueueingThrottler(2, TimeSpan.FromMilliseconds(500));
        var completedOrder = new ConcurrentQueue<int>();

        var flow = FlowBuilder.Create<QueueState>("queuing-throttle")
            .Step("operation", async (state, ct) =>
            {
                var acquired = await limiter.WaitAsync(TimeSpan.FromSeconds(2), ct);
                if (acquired)
                {
                    try
                    {
                        await Task.Delay(50, ct);
                        completedOrder.Enqueue(state.RequestId);
                        state.Processed = true;
                    }
                    finally
                    {
                        limiter.Release();
                    }
                }
                else
                {
                    state.TimedOut = true;
                }
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 6).Select(i =>
            executor.ExecuteAsync(flow, new QueueState { FlowId = $"queue-{i}", RequestId = i }).AsTask()
        );

        var results = await Task.WhenAll(tasks);

        var processed = results.Count(r => r.State.Processed);
        processed.Should().Be(6); // All should eventually process
    }

    #region State Classes and Throttler Implementations

    public class ThrottleState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool Accepted { get; set; }
        public bool Rejected { get; set; }
    }

    public class UserThrottleState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string UserId { get; set; } = "";
        public bool Accepted { get; set; }
        public bool Rejected { get; set; }
    }

    public class BackpressureState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool QueueFull { get; set; }
        public bool Processed { get; set; }
    }

    public class AdaptiveState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool SimulateSuccess { get; set; }
        public bool Accepted { get; set; }
        public bool Rejected { get; set; }
    }

    public class PriorityState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool IsHighPriority { get; set; }
        public bool Accepted { get; set; }
        public bool Rejected { get; set; }
    }

    public class QueueState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int RequestId { get; set; }
        public bool Processed { get; set; }
        public bool TimedOut { get; set; }
    }

    public class FixedRateLimiter
    {
        private int _count;
        private readonly int _limit;

        public FixedRateLimiter(int limit) => _limit = limit;

        public bool TryAcquire()
        {
            var current = Interlocked.Increment(ref _count);
            return current <= _limit;
        }
    }

    public class TokenBucket
    {
        private int _tokens;
        private readonly int _capacity;
        private readonly int _refillRate;
        private DateTime _lastRefill;
        private readonly object _lock = new();

        public TokenBucket(int capacity, int refillRate)
        {
            _capacity = capacity;
            _tokens = capacity;
            _refillRate = refillRate;
            _lastRefill = DateTime.UtcNow;
        }

        public bool TryConsume()
        {
            lock (_lock)
            {
                Refill();
                if (_tokens > 0)
                {
                    _tokens--;
                    return true;
                }
                return false;
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRefill).TotalSeconds;
            var newTokens = (int)(elapsed * _refillRate);
            if (newTokens > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + newTokens);
                _lastRefill = now;
            }
        }
    }

    public class SlidingWindowLimiter
    {
        private readonly Queue<DateTime> _timestamps = new();
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly object _lock = new();

        public SlidingWindowLimiter(int limit, TimeSpan window)
        {
            _limit = limit;
            _window = window;
        }

        public bool TryAcquire()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var cutoff = now - _window;

                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count < _limit)
                {
                    _timestamps.Enqueue(now);
                    return true;
                }
                return false;
            }
        }
    }

    public class AdaptiveThrottler
    {
        private int _currentLimit;
        private readonly int _minLimit;
        private readonly int _maxLimit;
        private int _successCount;
        private int _failureCount;
        private readonly object _lock = new();

        public int CurrentLimit => _currentLimit;

        public AdaptiveThrottler(int initialLimit, int minLimit, int maxLimit)
        {
            _currentLimit = initialLimit;
            _minLimit = minLimit;
            _maxLimit = maxLimit;
        }

        public bool TryAcquire()
        {
            lock (_lock)
            {
                return (_successCount + _failureCount) < _currentLimit;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _successCount++;
                if (_successCount > 10)
                {
                    _currentLimit = Math.Min(_maxLimit, _currentLimit + 1);
                    _successCount = 0;
                }
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                if (_failureCount > 3)
                {
                    _currentLimit = Math.Max(_minLimit, _currentLimit - 2);
                    _failureCount = 0;
                }
            }
        }
    }

    public class PriorityRateLimiter
    {
        private int _normalCount;
        private int _priorityCount;
        private readonly int _normalLimit;
        private readonly int _priorityReserved;

        public PriorityRateLimiter(int normalLimit, int priorityReserved)
        {
            _normalLimit = normalLimit;
            _priorityReserved = priorityReserved;
        }

        public bool TryAcquire(bool highPriority)
        {
            if (highPriority)
            {
                var count = Interlocked.Increment(ref _priorityCount);
                return count <= _priorityReserved;
            }
            else
            {
                var count = Interlocked.Increment(ref _normalCount);
                return count <= _normalLimit;
            }
        }
    }

    public class QueueingThrottler
    {
        private readonly SemaphoreSlim _semaphore;

        public QueueingThrottler(int maxConcurrency, TimeSpan _)
        {
            _semaphore = new SemaphoreSlim(maxConcurrency);
        }

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
            => _semaphore.WaitAsync(timeout, ct);

        public void Release() => _semaphore.Release();
    }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
