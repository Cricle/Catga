using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Concurrency limiter using SemaphoreSlim for backpressure control.
/// Prevents thread pool starvation by limiting concurrent operations.
/// </summary>
public sealed class ConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly ILogger? _logger;

    public ConcurrencyLimiter(int maxConcurrency, ILogger? logger = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than 0");

        _maxConcurrency = maxConcurrency;
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _logger = logger;
    }

    /// <summary>Current available slots</summary>
    public int CurrentCount => _semaphore.CurrentCount;

    /// <summary>Maximum concurrency allowed</summary>
    public int MaxConcurrency => _maxConcurrency;

    /// <summary>Number of active tasks</summary>
    public int ActiveTasks => _maxConcurrency - _semaphore.CurrentCount;

    /// <summary>
    /// Acquire a slot asynchronously. Will wait until a slot is available.
    /// </summary>
    /// <returns>A disposable that releases the slot when disposed</returns>
    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        var active = ActiveTasks;
        if (active >= _maxConcurrency * 0.8)
        {
            _logger?.LogWarning(
                "High concurrency: {Active}/{Max} tasks active ({Percent:F1}%)",
                active, _maxConcurrency, (double)active / _maxConcurrency * 100);
        }

        return new SemaphoreReleaser(_semaphore);
    }

    /// <summary>
    /// Try to acquire a slot immediately without waiting.
    /// </summary>
    /// <param name="releaser">The releaser if acquisition succeeded</param>
    /// <param name="timeout">Optional timeout (default: zero, immediate)</param>
    /// <returns>True if acquired, false if not available</returns>
    public bool TryAcquire([NotNullWhen(true)] out IDisposable? releaser, TimeSpan timeout = default)
    {
        if (_semaphore.Wait(timeout == default ? TimeSpan.Zero : timeout))
        {
            releaser = new SemaphoreReleaser(_semaphore);
            return true;
        }

        releaser = null;
        return false;
    }

    public void Dispose() => _semaphore?.Dispose();

    private sealed class SemaphoreReleaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}

