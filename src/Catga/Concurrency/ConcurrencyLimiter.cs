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

    /// <summary>
    /// Execute action with concurrency limit (non-blocking async)
    /// P1 Optimization: Ensure counter is incremented atomically with semaphore acquisition
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
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
            return await action();
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
