using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster;

/// <summary>
/// Runs a task only on the leader node.
/// Automatically stops when leadership is lost and restarts when regained.
/// </summary>
public abstract class SingletonTaskRunner : BackgroundService
{
    private readonly IClusterCoordinator _coordinator;
    private readonly ILogger _logger;
    private readonly TimeSpan _checkInterval;

    protected SingletonTaskRunner(
        IClusterCoordinator coordinator,
        ILogger logger,
        TimeSpan? checkInterval = null)
    {
        _coordinator = coordinator;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Task name for logging.
    /// </summary>
    protected abstract string TaskName { get; }

    /// <summary>
    /// Execute the singleton task. Called only when this node is leader.
    /// Should be cancellation-aware and exit promptly when token is cancelled.
    /// </summary>
    protected abstract Task ExecuteLeaderTaskAsync(CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[{TaskName}] Singleton task runner started on node {NodeId}",
            TaskName, _coordinator.NodeId);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_coordinator.IsLeader)
            {
                _logger.LogInformation("[{TaskName}] This node is leader, starting task", TaskName);

                try
                {
                    await _coordinator.ExecuteIfLeaderAsync(ExecuteLeaderTaskAsync, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{TaskName}] Task execution failed", TaskName);
                }

                _logger.LogInformation("[{TaskName}] Task stopped (leadership lost or completed)", TaskName);
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[{TaskName}] Singleton task runner stopped", TaskName);
    }
}
