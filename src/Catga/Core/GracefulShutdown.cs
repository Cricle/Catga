using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful shutdown manager - tracks active operations, ensures safe shutdown
/// Users don't need to worry about complex shutdown logic
/// </summary>
public sealed class GracefulShutdownManager : IAsyncDisposable
{
    private readonly ILogger<GracefulShutdownManager> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private int _activeOperations;
    private readonly SemaphoreSlim _shutdownSignal = new(0, 1);
    private volatile bool _isShuttingDown;

    public GracefulShutdownManager(ILogger<GracefulShutdownManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Shutdown token - triggered when shutdown starts
    /// </summary>
    public CancellationToken ShutdownToken => _shutdownCts.Token;

    /// <summary>
    /// Is shutdown in progress
    /// </summary>
    public bool IsShuttingDown => _isShuttingDown;

    /// <summary>
    /// Current active operations count
    /// </summary>
    public int ActiveOperations => Volatile.Read(ref _activeOperations);

    /// <summary>
    /// Begin operation - auto-tracked
    /// </summary>
    public OperationScope BeginOperation()
    {
        // Atomically increment then check to avoid race condition
        var count = Interlocked.Increment(ref _activeOperations);

        if (_isShuttingDown)
        {
            // Rollback if shutting down
            Interlocked.Decrement(ref _activeOperations);
            throw new InvalidOperationException("System shutting down");
        }

        return new OperationScope(this);
    }

    /// <summary>
    /// Start graceful shutdown
    /// </summary>
    public async Task ShutdownAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        timeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;

        _logger.LogInformation("Shutdown started, active operations: {ActiveOperations}", ActiveOperations);

        // Notify all components to start shutdown
        await _shutdownCts.CancelAsync();

        var sw = Stopwatch.StartNew();

        // Wait for all active operations to complete
        while (ActiveOperations > 0 && sw.Elapsed < timeout)
        {
            _logger.LogInformation("Waiting for {ActiveOperations} operations... ({Elapsed:F1}s / {Timeout:F1}s)",
                ActiveOperations, sw.Elapsed.TotalSeconds, timeout.TotalSeconds);

            await Task.Delay(100, cancellationToken);
        }

        if (ActiveOperations > 0)
        {
            _logger.LogWarning("Shutdown timeout, {ActiveOperations} operations incomplete", ActiveOperations);
        }
        else
        {
            _logger.LogInformation("Shutdown complete, duration: {Elapsed:F1}s", sw.Elapsed.TotalSeconds);
        }

        _shutdownSignal.Release();
    }

    /// <summary>
    /// Wait for shutdown to complete
    /// </summary>
    public async Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _shutdownSignal.WaitAsync(cancellationToken);
    }

    internal void EndOperation()
    {
        var remaining = Interlocked.Decrement(ref _activeOperations);
        if (_isShuttingDown && remaining == 0)
        {
            _logger.LogDebug("Last operation complete, safe to shutdown");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isShuttingDown)
            await ShutdownAsync();

        _shutdownCts.Dispose();
        _shutdownSignal.Dispose();
    }

    /// <summary>
    /// Operation scope - auto-tracks operation lifecycle
    /// </summary>
    public readonly struct OperationScope : IDisposable
    {
        private readonly GracefulShutdownManager _manager;

        internal OperationScope(GracefulShutdownManager manager)
        {
            _manager = manager;
        }

        public void Dispose()
        {
            _manager.EndOperation();
        }
    }
}

