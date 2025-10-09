namespace Catga.Concurrency;

/// <summary>
/// Lock-free concurrency limiter using SemaphoreSlim (non-blocking, AOT-compatible)
/// </summary>
public sealed class ConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private long _currentCount;
    private long _rejectedCount;

    // Observability: Track metrics
    private long _totalExecutions;
    private long _successfulExecutions;
    private long _failedExecutions;

    public ConcurrencyLimiter(int maxConcurrency)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        _maxConcurrency = maxConcurrency;
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public long CurrentCount => Interlocked.Read(ref _currentCount);
    public long RejectedCount => Interlocked.Read(ref _rejectedCount);
    public int MaxConcurrency => _maxConcurrency;
    public int AvailableSlots => _semaphore.CurrentCount;

    // Observability: Monitoring properties
    /// <summary>
    /// Total number of executions attempted
    /// </summary>
    public long TotalExecutions => Interlocked.Read(ref _totalExecutions);

    /// <summary>
    /// Number of successful executions
    /// </summary>
    public long SuccessfulExecutions => Interlocked.Read(ref _successfulExecutions);

    /// <summary>
    /// Number of failed executions
    /// </summary>
    public long FailedExecutions => Interlocked.Read(ref _failedExecutions);

    /// <summary>
    /// Success rate (0.0 to 1.0)
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var total = TotalExecutions - RejectedCount; // Exclude rejected
            return total > 0 ? SuccessfulExecutions / (double)total : 0.0;
        }
    }

    /// <summary>
    /// Utilization rate (0.0 to 1.0)
    /// </summary>
    public double UtilizationRate => 1.0 - (AvailableSlots / (double)_maxConcurrency);

    /// <summary>
    /// Execute action with concurrency limit (non-blocking async)
    /// P1 Optimization: Ensure counter is incremented atomically with semaphore acquisition
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // Observability: Track total executions
        Interlocked.Increment(ref _totalExecutions);

        // P1: Track active count accurately using semaphore state
        // CurrentCount = MaxConcurrency - AvailableSlots
        var activeBefore = _maxConcurrency - _semaphore.CurrentCount;
        
        // Non-blocking async wait
        var acquired = await _semaphore.WaitAsync(timeout, cancellationToken);

        if (!acquired)
        {
            Interlocked.Increment(ref _rejectedCount);
            throw new ConcurrencyLimitException(
                $"Concurrency limit reached ({_maxConcurrency}). Request rejected.");
        }

        // P1: Atomically update current count after successful acquisition
        Interlocked.Increment(ref _currentCount);

        try
        {
            var result = await action();
            // Observability: Track success
            Interlocked.Increment(ref _successfulExecutions);
            return result;
        }
        catch
        {
            // Observability: Track failure
            Interlocked.Increment(ref _failedExecutions);
            throw;
        }
        finally
        {
            // P1: Decrement BEFORE releasing semaphore to maintain accurate count
            Interlocked.Decrement(ref _currentCount);
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}

public class ConcurrencyLimitException(string message) : Exception(message);
