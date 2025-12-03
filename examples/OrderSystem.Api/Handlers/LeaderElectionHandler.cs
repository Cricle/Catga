using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Background service demonstrating leader election for singleton tasks.
/// Only the leader node processes scheduled jobs.
/// </summary>
public sealed partial class LeaderElectionBackgroundService : BackgroundService
{
    private readonly ILeaderElection? _leaderElection;
    private readonly ILogger<LeaderElectionBackgroundService> _logger;
    private const string ElectionId = "order-system:scheduler";

    public LeaderElectionBackgroundService(
        ILogger<LeaderElectionBackgroundService> logger,
        ILeaderElection? leaderElection = null)
    {
        _logger = logger;
        _leaderElection = leaderElection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_leaderElection == null)
        {
            LogNoLeaderElection(_logger);
            return;
        }

        LogStarting(_logger, ElectionId);

        ILeadershipHandle? handle = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Try to acquire leadership
                handle = await _leaderElection.TryAcquireLeadershipAsync(ElectionId, stoppingToken);

                if (handle != null)
                {
                    LogBecameLeader(_logger, ElectionId);

                    // Run leader tasks while we hold leadership
                    await RunLeaderTasksAsync(handle, stoppingToken);
                }
                else
                {
                    // Check who is the current leader
                    var leaderInfo = await _leaderElection.GetLeaderAsync(ElectionId, stoppingToken);
                    if (leaderInfo != null)
                    {
                        LogFollowing(_logger, ElectionId, leaderInfo.Value.NodeId);
                    }

                    // Wait before retrying
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogError(_logger, ElectionId, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        // Release leadership on shutdown
        if (handle != null)
        {
            await handle.DisposeAsync();
            LogReleasedLeadership(_logger, ElectionId);
        }
    }

    private async Task RunLeaderTasksAsync(ILeadershipHandle handle, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && handle.IsLeader)
        {
            // Perform leader-only tasks
            LogLeaderTask(_logger, DateTime.UtcNow);

            // Extend lease periodically
            await handle.ExtendAsync(ct);

            // Simulate scheduled job processing
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }

        if (!handle.IsLeader)
        {
            LogLostLeadership(_logger, ElectionId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Leader election not configured, skipping background service")]
    private static partial void LogNoLeaderElection(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting leader election for {ElectionId}")]
    private static partial void LogStarting(ILogger logger, string electionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Became leader for {ElectionId}")]
    private static partial void LogBecameLeader(ILogger logger, string electionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Following leader for {ElectionId}: {LeaderId}")]
    private static partial void LogFollowing(ILogger logger, string electionId, string leaderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost leadership for {ElectionId}")]
    private static partial void LogLostLeadership(ILogger logger, string electionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released leadership for {ElectionId}")]
    private static partial void LogReleasedLeadership(ILogger logger, string electionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Running leader task at {Time}")]
    private static partial void LogLeaderTask(ILogger logger, DateTime time);

    [LoggerMessage(Level = LogLevel.Error, Message = "Leader election error for {ElectionId}: {Error}")]
    private static partial void LogError(ILogger logger, string electionId, string error);
}
