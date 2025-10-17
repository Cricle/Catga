using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Monitoring;

/// <summary>
/// Registry for tracking all nodes in the distributed system.
/// Thread-safe and production-safe.
/// </summary>
public sealed class NodeRegistry
{
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();
    private readonly ILogger<NodeRegistry> _logger;
    private readonly string _currentNodeId;
    private readonly NodeInfo _currentNode;

    public NodeRegistry(ILogger<NodeRegistry> logger, string? serviceName = null)
    {
        _logger = logger;
        
        // Generate current node ID
        var hostName = Dns.GetHostName();
        var processId = Environment.ProcessId;
        _currentNodeId = $"{hostName}_{processId}";

        // Initialize current node
        _currentNode = new NodeInfo
        {
            NodeId = _currentNodeId,
            ServiceName = serviceName ?? "Unknown",
            HostName = hostName,
            ProcessId = processId,
            StartTime = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow,
            Status = NodeStatus.Healthy
        };

        _nodes[_currentNodeId] = _currentNode;
        _logger.LogInformation("Node registry initialized: {NodeId}", _currentNodeId);
    }

    /// <summary>
    /// Gets the current node ID
    /// </summary>
    public string CurrentNodeId => _currentNodeId;

    /// <summary>
    /// Registers a node or updates its heartbeat
    /// </summary>
    public void RegisterOrUpdateNode(NodeInfo nodeInfo)
    {
        nodeInfo.LastHeartbeat = DateTime.UtcNow;
        
        _nodes.AddOrUpdate(
            nodeInfo.NodeId,
            nodeInfo,
            (_, existing) =>
            {
                // Update existing node
                existing.LastHeartbeat = nodeInfo.LastHeartbeat;
                existing.Status = nodeInfo.Status;
                existing.CpuUsage = nodeInfo.CpuUsage;
                existing.MemoryUsage = nodeInfo.MemoryUsage;
                existing.ActiveFlows = nodeInfo.ActiveFlows;
                existing.TotalMessages = nodeInfo.TotalMessages;
                existing.SuccessRate = nodeInfo.SuccessRate;
                existing.AvgResponseTimeMs = nodeInfo.AvgResponseTimeMs;
                existing.ActiveBreakpoints = nodeInfo.ActiveBreakpoints;
                existing.DebuggerMode = nodeInfo.DebuggerMode;
                return existing;
            }
        );

        _logger.LogDebug("Node registered/updated: {NodeId}", nodeInfo.NodeId);
    }

    /// <summary>
    /// Updates current node status
    /// </summary>
    public void UpdateCurrentNode(Action<NodeInfo> updateAction)
    {
        updateAction(_currentNode);
        _currentNode.LastHeartbeat = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets all registered nodes
    /// </summary>
    public IReadOnlyList<NodeInfo> GetAllNodes()
    {
        return _nodes.Values.OrderBy(n => n.ServiceName).ThenBy(n => n.NodeId).ToList();
    }

    /// <summary>
    /// Gets healthy nodes only
    /// </summary>
    public IReadOnlyList<NodeInfo> GetHealthyNodes()
    {
        return _nodes.Values.Where(n => n.IsHealthy).ToList();
    }

    /// <summary>
    /// Gets a specific node by ID
    /// </summary>
    public NodeInfo? GetNode(string nodeId)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return node;
    }

    /// <summary>
    /// Removes offline nodes (no heartbeat for > 60 seconds)
    /// </summary>
    public int RemoveOfflineNodes()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        var offlineNodes = _nodes.Values
            .Where(n => n.LastHeartbeat < cutoff && n.NodeId != _currentNodeId)
            .Select(n => n.NodeId)
            .ToList();

        int removed = 0;
        foreach (var nodeId in offlineNodes)
        {
            if (_nodes.TryRemove(nodeId, out _))
            {
                removed++;
                _logger.LogInformation("Removed offline node: {NodeId}", nodeId);
            }
        }

        return removed;
    }

    /// <summary>
    /// Gets cluster statistics
    /// </summary>
    public ClusterStats GetClusterStats()
    {
        var allNodes = _nodes.Values.ToList();
        var healthyNodes = allNodes.Where(n => n.IsHealthy).ToList();

        return new ClusterStats
        {
            TotalNodes = allNodes.Count,
            HealthyNodes = healthyNodes.Count,
            DegradedNodes = allNodes.Count(n => n.Status == NodeStatus.Degraded),
            UnhealthyNodes = allNodes.Count(n => n.Status == NodeStatus.Unhealthy),
            TotalActiveFlows = allNodes.Sum(n => n.ActiveFlows),
            TotalMessages = allNodes.Sum(n => n.TotalMessages),
            AvgSuccessRate = healthyNodes.Any() ? healthyNodes.Average(n => n.SuccessRate) : 0,
            AvgResponseTimeMs = healthyNodes.Any() ? healthyNodes.Average(n => n.AvgResponseTimeMs) : 0,
            TotalMemoryUsage = allNodes.Sum(n => n.MemoryUsage),
            AvgCpuUsage = healthyNodes.Any() ? healthyNodes.Average(n => n.CpuUsage) : 0
        };
    }

    /// <summary>
    /// Clears all nodes except current
    /// </summary>
    public void Clear()
    {
        var toRemove = _nodes.Keys.Where(k => k != _currentNodeId).ToList();
        foreach (var key in toRemove)
        {
            _nodes.TryRemove(key, out _);
        }
        _logger.LogInformation("Node registry cleared (kept current node only)");
    }
}

/// <summary>
/// Cluster-wide statistics
/// </summary>
public sealed class ClusterStats
{
    public int TotalNodes { get; set; }
    public int HealthyNodes { get; set; }
    public int DegradedNodes { get; set; }
    public int UnhealthyNodes { get; set; }
    public int TotalActiveFlows { get; set; }
    public long TotalMessages { get; set; }
    public double AvgSuccessRate { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public long TotalMemoryUsage { get; set; }
    public double AvgCpuUsage { get; set; }
}

