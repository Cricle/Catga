using Catga.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Transport.Nats;

/// <summary>
/// NATS recoverable transport - auto-reconnect and state recovery
/// </summary>
public sealed class NatsRecoverableTransport : IRecoverableComponent, IDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsRecoverableTransport> _logger;
    private readonly System.Threading.Timer _monitorTimer;
    private volatile bool _isHealthy = true;
    private bool _disposed;

    public NatsRecoverableTransport(
        INatsConnection connection,
        ILogger<NatsRecoverableTransport> logger)
    {
        _connection = connection;
        _logger = logger;

        // Monitor connection status using Timer (lighter than Task.Run)
        _monitorTimer = new System.Threading.Timer(
            callback: MonitorConnectionStatus,
            state: null,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(5)
        );
    }

    public bool IsHealthy => _isHealthy;

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting NATS connection recovery");

        try
        {
            // NATS client auto-reconnects, we just wait for connection to recover
            var timeout = TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;

            while (_connection.ConnectionState != NatsConnectionState.Open &&
                   DateTime.UtcNow - startTime < timeout)
            {
                await Task.Delay(100, cancellationToken);
            }

            if (_connection.ConnectionState == NatsConnectionState.Open)
            {
                _isHealthy = true;
                _logger.LogInformation("NATS connection recovered successfully");
            }
            else
            {
                _logger.LogWarning("NATS connection recovery timeout");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS connection recovery failed");
            throw;
        }
    }

    private void MonitorConnectionStatus(object? state)
    {
        if (_disposed) return;

        try
        {
            var wasHealthy = _isHealthy;
            _isHealthy = _connection.ConnectionState == NatsConnectionState.Open;

            if (wasHealthy && !_isHealthy)
            {
                _logger.LogWarning("NATS connection lost");
            }
            else if (!wasHealthy && _isHealthy)
            {
                _logger.LogInformation("NATS connection recovered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring NATS connection status");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitorTimer?.Dispose();
    }
}

