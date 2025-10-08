using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.RateLimiting;
using Catga.Resilience;
using Catga.Concurrency;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark for P0-3: TokenBucketRateLimiter integer optimization
/// Compares old double-based vs new integer-based implementation
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class RateLimiterBenchmark
{
    private TokenBucketRateLimiter _rateLimiter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _rateLimiter = new TokenBucketRateLimiter(capacity: 1000, refillRatePerSecond: 100);
    }

    [Benchmark(Baseline = true)]
    public bool TryAcquire_Single()
    {
        return _rateLimiter.TryAcquire(1);
    }

    [Benchmark]
    [Arguments(10)]
    public bool TryAcquire_Batch(int count)
    {
        return _rateLimiter.TryAcquire(count);
    }

    [Benchmark]
    public long AvailableTokens()
    {
        return _rateLimiter.AvailableTokens;
    }

    /// <summary>
    /// Concurrent access test
    /// </summary>
    [Benchmark]
    [Arguments(4)]
    public int Concurrent_TryAcquire(int threads)
    {
        int successful = 0;
        Parallel.For(0, threads, _ =>
        {
            if (_rateLimiter.TryAcquire(1))
            {
                Interlocked.Increment(ref successful);
            }
        });
        return successful;
    }
}

/// <summary>
/// Benchmark for P1-1: CircuitBreaker Volatile.Read optimization
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class CircuitBreakerBenchmark
{
    private CircuitBreaker _circuitBreaker = null!;

    [GlobalSetup]
    public void Setup()
    {
        _circuitBreaker = new CircuitBreaker(failureThreshold: 5, resetTimeout: TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Read state - benefits from Volatile.Read vs CAS
    /// </summary>
    [Benchmark(Baseline = true)]
    public CircuitState GetState()
    {
        return _circuitBreaker.State;
    }

    /// <summary>
    /// Execute successful operation
    /// </summary>
    [Benchmark]
    public async Task<int> ExecuteAsync_Success()
    {
        return await _circuitBreaker.ExecuteAsync(() => Task.FromResult(42));
    }

    /// <summary>
    /// High frequency state checking
    /// </summary>
    [Benchmark]
    [Arguments(100)]
    public CircuitState[] GetState_Multiple(int count)
    {
        var states = new CircuitState[count];
        for (int i = 0; i < count; i++)
        {
            states[i] = _circuitBreaker.State;
        }
        return states;
    }
}

/// <summary>
/// Benchmark for P1-2: ConcurrencyLimiter counter synchronization
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class ConcurrencyLimiterBenchmark
{
    private ConcurrencyLimiter _limiter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _limiter = new ConcurrencyLimiter(maxConcurrency: 10);
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ExecuteAsync_Single()
    {
        return await _limiter.ExecuteAsync(
            () => Task.FromResult(42),
            timeout: TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Concurrent execution test
    /// </summary>
    [Benchmark]
    [Arguments(5)]
    public async Task<int[]> ExecuteAsync_Concurrent(int parallelism)
    {
        var tasks = new Task<int>[parallelism];
        for (int i = 0; i < parallelism; i++)
        {
            tasks[i] = _limiter.ExecuteAsync(
                () => Task.FromResult(i),
                timeout: TimeSpan.FromSeconds(1));
        }
        return await Task.WhenAll(tasks);
    }

    [Benchmark]
    public long CurrentCount()
    {
        return _limiter.CurrentCount;
    }

    [Benchmark]
    public int AvailableSlots()
    {
        return _limiter.AvailableSlots;
    }
}

