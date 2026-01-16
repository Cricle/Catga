using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Cluster;

/// <summary>
/// Health check for cluster status.
/// Reports healthy if cluster has a leader (regardless of whether this node is leader).
/// </summary>
public sealed class ClusterHealthCheck : IHealthCheck
{
    private readonly IClusterCoordinator _coordinator;

    public ClusterHealthCheck(IClusterCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var hasLeader = _coordinator.LeaderEndpoint != null;
        var isLeader = _coordinator.IsLeader;

        if (!hasLeader)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "No leader elected in cluster",
                data: new Dictionary<string, object>
                {
                    ["NodeId"] = _coordinator.NodeId,
                    ["IsLeader"] = false,
                    ["LeaderEndpoint"] = "none"
                }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            isLeader ? "This node is the leader" : "Cluster has a leader",
            data: new Dictionary<string, object>
            {
                ["NodeId"] = _coordinator.NodeId,
                ["IsLeader"] = isLeader,
                ["LeaderEndpoint"] = _coordinator.LeaderEndpoint ?? "unknown"
            }));
    }
}
