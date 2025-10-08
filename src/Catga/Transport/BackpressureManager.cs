using System.Diagnostics;
using System.Threading.Channels;

namespace Catga.Transport;

/// <summary>
/// Backpressure manager to prevent transport overload
/// Uses adaptive rate limiting based on queue depth and latency
/// </summary>
public sealed class BackpressureManager
{
    private readonly BackpressureOptions _options;
    private readonly Channel<WorkItem> _channel;
    private readonly SemaphoreSlim _semaphore;

    private long _inFlightCount;
    private long _totalProcessed;
    private long _totalDropped;

    // Adaptive metrics
    private readonly Stopwatch _latencyWatch = Stopwatch.StartNew();
    private double _averageLatencyMs;

    public BackpressureManager(BackpressureOptions? options = null)
    {
        _options = options ?? new BackpressureOptions();

        // Bounded channel for queuing
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(_options.MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        // Semaphore for concurrency control
        _semaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    /// <summary>
    /// Try enqueue work with backpressure check
    /// </summary>
    public async ValueTask<bool> TryEnqueueAsync<T>(
        T item,
        Func<T, CancellationToken, Task> processor,
        CancellationToken cancellationToken = default)
    {
        // Check if we should drop due to overload
        if (ShouldDrop())
        {
            Interlocked.Increment(ref _totalDropped);
            return false;
        }

        var workItem = new WorkItem
        {
            Processor = async ct =>
            {
                var start = Stopwatch.GetTimestamp();

                Interlocked.Increment(ref _inFlightCount);
                try
                {
                    await processor(item, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref _totalProcessed);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlightCount);

                    // Update average latency (exponential moving average)
                    var latencyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    _averageLatencyMs = _averageLatencyMs * 0.9 + latencyMs * 0.1;
                }
            }
        };

        // Try write to channel (non-blocking)
        return _channel.Writer.TryWrite(workItem);
    }

    /// <summary>
    /// Execute work with concurrency control
    /// </summary>
    public async ValueTask<bool> ExecuteAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        // Wait for semaphore (backpressure)
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            // Can't acquire immediately, check if we should drop
            if (ShouldDrop())
            {
                Interlocked.Increment(ref _totalDropped);
                return false;
            }

            // Wait with timeout
            if (!await _semaphore.WaitAsync(_options.WaitTimeout, cancellationToken))
            {
                Interlocked.Increment(ref _totalDropped);
                return false;
            }
        }

        try
        {
            var start = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _inFlightCount);

            await work(cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _totalProcessed);

            // Update latency
            var latencyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _averageLatencyMs = _averageLatencyMs * 0.9 + latencyMs * 0.1;

            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Start background processor
    /// </summary>
    public Task StartProcessorAsync(CancellationToken cancellationToken = default)
    {
        // Use Task.Run for long-running background task (appropriate usage)
        return Task.Run(async () =>
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                await _semaphore.WaitAsync(cancellationToken);

                // Process item asynchronously without Task.Run (already on background thread)
                // Fire-and-forget: process in background but don't block the reader
                _ = ProcessItemSafelyAsync(item, cancellationToken);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Process a work item safely with proper exception handling and semaphore release
    /// </summary>
    private async Task ProcessItemSafelyAsync(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            await item.Processor(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Swallow exceptions in fire-and-forget scenario
            // In production, log the exception:
            // _logger.LogError(ex, "WorkItem processing failed");
        }
        finally
        {
            // Always release semaphore, even if processing fails
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Check if we should drop messages due to overload
    /// </summary>
    private bool ShouldDrop()
    {
        // Drop if queue is over threshold
        var queueUtilization = (double)_channel.Reader.Count / _options.MaxQueueSize;
        if (queueUtilization > _options.DropThreshold)
            return true;

        // Drop if latency is too high (adaptive)
        if (_averageLatencyMs > _options.MaxLatencyMs)
            return true;

        // Drop if too many in-flight
        var inFlight = Interlocked.Read(ref _inFlightCount);
        if (inFlight > _options.MaxConcurrency * 1.2) // 20% overage allowed
            return true;

        return false;
    }

    /// <summary>
    /// Get current metrics
    /// </summary>
    public BackpressureMetrics GetMetrics()
    {
        return new BackpressureMetrics
        {
            InFlightCount = Interlocked.Read(ref _inFlightCount),
            QueuedCount = _channel.Reader.Count,
            TotalProcessed = Interlocked.Read(ref _totalProcessed),
            TotalDropped = Interlocked.Read(ref _totalDropped),
            AverageLatencyMs = _averageLatencyMs,
            QueueUtilization = (double)_channel.Reader.Count / _options.MaxQueueSize
        };
    }

    private class WorkItem
    {
        public required Func<CancellationToken, Task> Processor { get; init; }
    }
}

/// <summary>
/// Backpressure configuration options
/// </summary>
public class BackpressureOptions
{
    /// <summary>
    /// Maximum queue size (default: 1000)
    /// </summary>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>
    /// Maximum concurrent operations (default: 100)
    /// </summary>
    public int MaxConcurrency { get; set; } = 100;

    /// <summary>
    /// Drop threshold (default: 0.9 = 90% full)
    /// </summary>
    public double DropThreshold { get; set; } = 0.9;

    /// <summary>
    /// Maximum allowed latency before dropping (default: 1000ms)
    /// </summary>
    public double MaxLatencyMs { get; set; } = 1000;

    /// <summary>
    /// Wait timeout for semaphore (default: 100ms)
    /// </summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Backpressure metrics
/// </summary>
public class BackpressureMetrics
{
    public long InFlightCount { get; init; }
    public int QueuedCount { get; init; }
    public long TotalProcessed { get; init; }
    public long TotalDropped { get; init; }
    public double AverageLatencyMs { get; init; }
    public double QueueUtilization { get; init; }
}

