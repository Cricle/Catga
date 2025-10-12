using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Distributed;

/// <summary>Node heartbeat background service (lock-free)</summary>
public sealed class HeartbeatBackgroundService : BackgroundService
{
    private readonly INodeDiscovery _discovery;
    private readonly NodeInfo _currentNode;
    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly TimeSpan _heartbeatInterval;

    public HeartbeatBackgroundService(INodeDiscovery discovery, NodeInfo currentNode, ILogger<HeartbeatBackgroundService> logger, TimeSpan? heartbeatInterval = null)
    {
        _discovery = discovery;
        _currentNode = currentNode;
        _logger = logger;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _discovery.RegisterAsync(_currentNode, stoppingToken);
            _logger.LogInformation("Node {NodeId} registered, heartbeat every {Interval}s", _currentNode.NodeId, _heartbeatInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_heartbeatInterval, stoppingToken);
                    await _discovery.HeartbeatAsync(_currentNode.NodeId, _currentNode.Load, stoppingToken);
                    _logger.LogTrace("Heartbeat sent for node {NodeId}", _currentNode.NodeId);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send heartbeat for node {NodeId}", _currentNode.NodeId);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in heartbeat service for node {NodeId}", _currentNode.NodeId);
        }
        finally
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _discovery.UnregisterAsync(_currentNode.NodeId, cts.Token);
                _logger.LogInformation("Node {NodeId} unregistered successfully", _currentNode.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister node {NodeId} during shutdown", _currentNode.NodeId);
            }
        }
    }
}

