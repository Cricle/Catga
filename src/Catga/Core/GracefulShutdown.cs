using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful shutdown coordinator - integrates with IHostApplicationLifetime.
/// </summary>
public sealed partial class GracefulShutdownCoordinator : IDisposable
{
    private readonly ILogger<GracefulShutdownCoordinator> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly IDisposable? _lifetimeRegistration;
    private volatile bool _isShuttingDown;

    public GracefulShutdownCoordinator(
        ILogger<GracefulShutdownCoordinator> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _logger = logger;
        _lifetimeRegistration = lifetime?.ApplicationStopping.Register(OnShutdownRequested);
    }

    public CancellationToken ShutdownToken => _shutdownCts.Token;
    public bool IsShuttingDown => _isShuttingDown;

    private void OnShutdownRequested()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        LogShutdownRequested();
#if NET8_0_OR_GREATER
        _shutdownCts.CancelAsync().GetAwaiter().GetResult();
#else
        _shutdownCts.Cancel();
#endif
    }

    public void RequestShutdown() => OnShutdownRequested();

    public void Dispose()
    {
        _lifetimeRegistration?.Dispose();
        _shutdownCts.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful shutdown requested")]
    partial void LogShutdownRequested();
}
