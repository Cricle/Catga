using DotNext.Net.Cluster.Consensus.Raft;
using Microsoft.Extensions.Configuration;

namespace Catga.Cluster;

/// <summary>
/// Helper for configuring DotNext Raft cluster from configuration.
/// </summary>
public sealed class RaftClusterConfiguration
{
    /// <summary>
    /// Local node endpoint (e.g., "http://localhost:5000").
    /// </summary>
    public required string LocalNodeEndpoint { get; init; }

    /// <summary>
    /// Other cluster member endpoints.
    /// </summary>
    public required IReadOnlyList<string> Members { get; init; }

    /// <summary>
    /// Election timeout (default: 150ms).
    /// </summary>
    public TimeSpan ElectionTimeout { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Heartbeat interval (default: 50ms).
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Path to persistent state storage (optional, uses in-memory if null).
    /// </summary>
    public string? PersistentStatePath { get; init; }

    /// <summary>
    /// Load configuration from IConfiguration.
    /// Expected format:
    /// {
    ///   "Cluster": {
    ///     "LocalNodeEndpoint": "http://localhost:5000",
    ///     "Members": ["http://localhost:5001", "http://localhost:5002"],
    ///     "ElectionTimeout": "00:00:00.150",
    ///     "HeartbeatInterval": "00:00:00.050",
    ///     "PersistentStatePath": "./raft-state"
    ///   }
    /// }
    /// </summary>
    public static RaftClusterConfiguration FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Cluster");
        
        var localEndpoint = section["LocalNodeEndpoint"] 
            ?? throw new InvalidOperationException("Cluster:LocalNodeEndpoint is required");
        
        // Manually parse members array
        var membersList = new List<string>();
        var membersSection = section.GetSection("Members");
        foreach (var child in membersSection.GetChildren())
        {
            var value = child.Value;
            if (!string.IsNullOrEmpty(value))
            {
                membersList.Add(value);
            }
        }
        
        if (membersList.Count == 0)
        {
            throw new InvalidOperationException("Cluster:Members is required");
        }

        // Parse timespan values
        var electionTimeoutStr = section["ElectionTimeout"];
        var electionTimeout = !string.IsNullOrEmpty(electionTimeoutStr) && TimeSpan.TryParse(electionTimeoutStr, out var et)
            ? et
            : TimeSpan.FromMilliseconds(150);

        var heartbeatIntervalStr = section["HeartbeatInterval"];
        var heartbeatInterval = !string.IsNullOrEmpty(heartbeatIntervalStr) && TimeSpan.TryParse(heartbeatIntervalStr, out var hi)
            ? hi
            : TimeSpan.FromMilliseconds(50);

        return new RaftClusterConfiguration
        {
            LocalNodeEndpoint = localEndpoint,
            Members = membersList,
            ElectionTimeout = electionTimeout,
            HeartbeatInterval = heartbeatInterval,
            PersistentStatePath = section["PersistentStatePath"]
        };
    }

    /// <summary>
    /// Create configuration for local testing with multiple nodes.
    /// </summary>
    public static RaftClusterConfiguration CreateLocalCluster(int nodeId, int totalNodes, int basePort = 5000)
    {
        var localPort = basePort + nodeId;
        var members = Enumerable.Range(0, totalNodes)
            .Where(i => i != nodeId)
            .Select(i => $"http://localhost:{basePort + i}")
            .ToList();

        return new RaftClusterConfiguration
        {
            LocalNodeEndpoint = $"http://localhost:{localPort}",
            Members = members,
            PersistentStatePath = $"./raft-state-node{nodeId}"
        };
    }
}
