using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful shutdown coordinator - integrates with IHostApplicationLifetime
/// Simpler than tracking every operation, relies on framework shutdown
/// </summary>
public sealed partial class GracefulShutdownCoordinator : IDisposable
{
    private readonly ILogger<GracefulShutdownCoordinator> _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _isShuttingDown;
    private IDisposable? _lifetimeRegistration;

    public GracefulShutdownCoordinator(
        ILogger<GracefulShutdownCoordinator> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _logger = logger;
        _lifetime = lifetime;

        // Register with host lifetime if available
        if (_lifetime != null)
        {
            _lifetimeRegistration = _lifetime.ApplicationStopping.Register(OnShutdownRequested);
        }
    }

    /// <summary>
    /// Shutdown token - triggered when shutdown starts
    /// </summary>
    public CancellationToken ShutdownToken => _shutdownCts.Token;

    /// <summary>
    /// Is shutdown in progress
    /// </summary>
    public bool IsShuttingDown => _isShuttingDown;

    private void OnShutdownRequested()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        LogShutdownRequested();

#if NET8_0_OR_GREATER
        _shutdownCts.CancelAsync().GetAwaiter().GetResult();
#else
        _shutdownCts.Cancel();
#endif
    }

    /// <summary>
    /// Manually trigger shutdown (if not using IHostApplicationLifetime)
    /// </summary>
    public void RequestShutdown()
    {
        OnShutdownRequested();
    }

    public void Dispose()
    {
        _lifetimeRegistration?.Dispose();
        _shutdownCts.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful shutdown requested")]
    partial void LogShutdownRequested();
}
