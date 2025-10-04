using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Catga.Concurrency;
using Catga.Idempotency;
using Catga.RateLimiting;
using Catga.Resilience;

namespace CatCat.Benchmarks;

/// <summary>
/// 并发控制组件性能基准测试
/// 测试 ConcurrencyLimiter, IdempotencyStore, RateLimiter, CircuitBreaker 的性能
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, RuntimeMoniker.Net90, warmupCount: 3, iterationCount: 10)]
public class ConcurrencyBenchmarks
{
    private ConcurrencyLimiter _limiter = null!;
    private ShardedIdempotencyStore _idempotencyStore = null!;
    private TokenBucketRateLimiter _rateLimiter = null!;
    private CircuitBreaker _circuitBreaker = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limiter = new ConcurrencyLimiter(maxConcurrency: 100);
        _idempotencyStore = new ShardedIdempotencyStore(shardCount: 32, retentionPeriod: TimeSpan.FromHours(1));
        _rateLimiter = new TokenBucketRateLimiter(capacity: 1000, refillRatePerSecond: 100);
        _circuitBreaker = new CircuitBreaker(failureThreshold: 5, resetTimeout: TimeSpan.FromSeconds(30));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _limiter?.Dispose();
        // ShardedIdempotencyStore 不需要 Dispose（使用延迟清理）
    }

    /// <summary>
    /// ConcurrencyLimiter - 单次操作
    /// </summary>
    [Benchmark(Description = "ConcurrencyLimiter - 单次")]
    public async Task<int> ConcurrencyLimiter_Single()
    {
        return await _limiter.ExecuteAsync(
            () => Task.FromResult(42),
            timeout: TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// ConcurrencyLimiter - 批量操作 (100)
    /// </summary>
    [Benchmark(Description = "ConcurrencyLimiter - 批量 (100)")]
    public async Task ConcurrencyLimiter_Batch100()
    {
        var tasks = new Task<int>[100];
        for (int i = 0; i < 100; i++)
        {
            int value = i;
            tasks[i] = _limiter.ExecuteAsync(
                () => Task.FromResult(value),
                timeout: TimeSpan.FromSeconds(1));
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// IdempotencyStore - 写入操作
    /// </summary>
    [Benchmark(Description = "IdempotencyStore - 写入")]
    public async Task IdempotencyStore_Write()
    {
        await _idempotencyStore.MarkAsProcessedAsync($"key-{Guid.NewGuid()}", "value");
    }

    /// <summary>
    /// IdempotencyStore - 读取操作
    /// </summary>
    [Benchmark(Description = "IdempotencyStore - 读取")]
    public async Task<bool> IdempotencyStore_Read()
    {
        return await _idempotencyStore.HasBeenProcessedAsync("key-1");
    }

    /// <summary>
    /// IdempotencyStore - 批量写入 (100)
    /// </summary>
    [Benchmark(Description = "IdempotencyStore - 批量写入 (100)")]
    public async Task IdempotencyStore_BatchWrite100()
    {
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = _idempotencyStore.MarkAsProcessedAsync($"key-{i}", $"value-{i}");
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// IdempotencyStore - 批量读取 (100)
    /// </summary>
    [Benchmark(Description = "IdempotencyStore - 批量读取 (100)")]
    public async Task IdempotencyStore_BatchRead100()
    {
        var tasks = new Task<bool>[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = _idempotencyStore.HasBeenProcessedAsync($"key-{i}");
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// RateLimiter - 获取令牌
    /// </summary>
    [Benchmark(Description = "RateLimiter - 获取令牌")]
    public bool RateLimiter_TryAcquire()
    {
        return _rateLimiter.TryAcquire();
    }

    /// <summary>
    /// RateLimiter - 批量获取令牌 (100)
    /// </summary>
    [Benchmark(Description = "RateLimiter - 批量获取 (100)")]
    public void RateLimiter_BatchAcquire100()
    {
        for (int i = 0; i < 100; i++)
        {
            _rateLimiter.TryAcquire();
        }
    }

    /// <summary>
    /// CircuitBreaker - 成功操作
    /// </summary>
    [Benchmark(Description = "CircuitBreaker - 成功操作")]
    public async Task<int> CircuitBreaker_Success()
    {
        return await _circuitBreaker.ExecuteAsync(() => Task.FromResult(42));
    }

    /// <summary>
    /// CircuitBreaker - 批量操作 (100)
    /// </summary>
    [Benchmark(Description = "CircuitBreaker - 批量 (100)")]
    public async Task CircuitBreaker_Batch100()
    {
        var tasks = new Task<int>[100];
        for (int i = 0; i < 100; i++)
        {
            int value = i;
            tasks[i] = _circuitBreaker.ExecuteAsync(() => Task.FromResult(value));
        }
        await Task.WhenAll(tasks);
    }
}

