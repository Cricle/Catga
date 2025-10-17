using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful shutdown manager - tracks active operations, ensures safe shutdown
/// Users don't need to worry about complex shutdown logic
/// </summary>
public sealed partial class GracefulShutdownManager : IAsyncDisposable
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

        LogShutdownStarted(ActiveOperations);

        // Notify all components to start shutdown
#if NET8_0_OR_GREATER
        await _shutdownCts.CancelAsync();
#else
        _shutdownCts.Cancel();
        await Task.CompletedTask;
#endif

        var sw = Stopwatch.StartNew();

        // Wait for all active operations to complete
        while (ActiveOperations > 0 && sw.Elapsed < timeout)
        {
            LogWaitingForOperations(ActiveOperations, sw.Elapsed.TotalSeconds, timeout.TotalSeconds);
            await Task.Delay(100, cancellationToken);
        }

        if (ActiveOperations > 0)
        {
            LogShutdownTimeout(ActiveOperations);
        }
        else
        {
            LogShutdownComplete(sw.Elapsed.TotalSeconds);
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
            LogLastOperationComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isShuttingDown)
            await ShutdownAsync();

        _shutdownCts.Dispose();
        _shutdownSignal.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown started, active operations: {ActiveOperations}")]
    partial void LogShutdownStarted(int activeOperations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for {ActiveOperations} operations... ({Elapsed:F1}s / {Timeout:F1}s)")]
    partial void LogWaitingForOperations(int activeOperations, double elapsed, double timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shutdown timeout, {ActiveOperations} operations incomplete")]
    partial void LogShutdownTimeout(int activeOperations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown complete, duration: {Elapsed:F1}s")]
    partial void LogShutdownComplete(double elapsed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Last operation complete, safe to shutdown")]
    partial void LogLastOperationComplete();

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

