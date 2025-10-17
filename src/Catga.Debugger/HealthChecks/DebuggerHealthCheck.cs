using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Catga.Debugger.Replay;
using Catga.Debugger.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Debugger.HealthChecks;

/// <summary>
/// Health check for Catga Debugger - integrates with Aspire Dashboard
/// </summary>
public sealed class DebuggerHealthCheck : IHealthCheck
{
    private readonly IEventStore _eventStore;
    private readonly ReplaySessionManager _sessionManager;

    public DebuggerHealthCheck(
        IEventStore eventStore,
        ReplaySessionManager sessionManager)
    {
        _eventStore = eventStore;
        _sessionManager = sessionManager;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _eventStore.GetStatsAsync();
            var (flowSessions, systemSessions) = _sessionManager.GetSessionCounts();
            var totalSessions = flowSessions + systemSessions;

            var data = new Dictionary<string, object>
            {
                ["event_count"] = stats.TotalEvents,
                ["total_flows"] = stats.TotalFlows,
                ["active_replay_sessions"] = totalSessions,
                ["storage_size_bytes"] = stats.StorageSizeBytes
            };

            // Check storage size (warn if > 1M events)
            if (stats.TotalEvents > 1_000_000)
            {
                return HealthCheckResult.Degraded(
                    "Event store size exceeds 1M events. Consider cleanup.",
                    data: data
                );
            }

            // Check active sessions (warn if > 100)
            if (totalSessions > 100)
            {
                return HealthCheckResult.Degraded(
                    "Too many active replay sessions. May impact performance.",
                    data: data
                );
            }

            return HealthCheckResult.Healthy("Catga Debugger is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Catga Debugger encountered an error",
                exception: ex
            );
        }
    }
}

