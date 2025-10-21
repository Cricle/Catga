using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Concurrency limiter using SemaphoreSlim for backpressure control.
/// Prevents thread pool starvation by limiting concurrent operations.
/// Zero-allocation design using struct-based releaser.
/// </summary>
public sealed class ConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _warningThreshold; // Pre-calculated to avoid boxing
    private readonly ILogger? _logger;

    public ConcurrencyLimiter(int maxConcurrency, ILogger? logger = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than 0");

        _maxConcurrency = maxConcurrency;
        _warningThreshold = (int)(_maxConcurrency * 0.8);
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
    /// Zero-allocation: returns struct-based releaser.
    /// </summary>
    /// <returns>A struct disposable that releases the slot when disposed</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SemaphoreReleaser> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Only log warning if logger is enabled and threshold exceeded
        if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
        {
            var active = ActiveTasks;
            if (active >= _warningThreshold)
            {
                _logger.LogWarning(
                    "High concurrency: {Active}/{Max} tasks active ({Percent:F1}%)",
                    active, _maxConcurrency, (double)active / _maxConcurrency * 100);
            }
        }

        return new SemaphoreReleaser(_semaphore);
    }

    /// <summary>
    /// Try to acquire a slot immediately without waiting.
    /// Zero-allocation: returns struct-based releaser.
    /// </summary>
    /// <param name="releaser">The releaser if acquisition succeeded</param>
    /// <param name="timeout">Optional timeout (default: zero, immediate)</param>
    /// <returns>True if acquired, false if not available</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire(out SemaphoreReleaser releaser, TimeSpan timeout = default)
    {
        if (_semaphore.Wait(timeout == default ? TimeSpan.Zero : timeout))
        {
            releaser = new SemaphoreReleaser(_semaphore);
            return true;
        }

        releaser = default;
        return false;
    }

    public void Dispose() => _semaphore?.Dispose();

    /// <summary>
    /// Struct-based releaser for zero-allocation pattern.
    /// IMPORTANT: This is a struct, not a class, to avoid heap allocation on every Acquire.
    /// </summary>
    public readonly struct SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim? _semaphore;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            _semaphore?.Release();
        }
    }
}

