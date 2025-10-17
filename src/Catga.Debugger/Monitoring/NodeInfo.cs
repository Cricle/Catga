using System;
using System.Collections.Generic;

namespace Catga.Debugger.Monitoring;

/// <summary>
/// Represents information about a single node (service instance)
/// </summary>
public sealed class NodeInfo
{
    /// <summary>
    /// Unique node identifier (hostname + process ID)
    /// </summary>
    public string NodeId { get; set; } = "";

    /// <summary>
    /// Service name
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// Node hostname
    /// </summary>
    public string HostName { get; set; } = "";

    /// <summary>
    /// Process ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Node base URL (if available)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// When the node started
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Last heartbeat time
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// Node status
    /// </summary>
    public NodeStatus Status { get; set; }

    /// <summary>
    /// CPU usage percentage
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// Memory usage in bytes
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Active message flows count
    /// </summary>
    public int ActiveFlows { get; set; }

    /// <summary>
    /// Total messages processed
    /// </summary>
    public long TotalMessages { get; set; }

    /// <summary>
    /// Success rate (0.0 - 1.0)
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AvgResponseTimeMs { get; set; }

    /// <summary>
    /// Active breakpoints count
    /// </summary>
    public int ActiveBreakpoints { get; set; }

    /// <summary>
    /// Debugger mode
    /// </summary>
    public string DebuggerMode { get; set; } = "Disabled";

    /// <summary>
    /// Custom metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Uptime duration
    /// </summary>
    public TimeSpan Uptime => DateTime.UtcNow - StartTime;

    /// <summary>
    /// Is node healthy (heartbeat within 30 seconds)
    /// </summary>
    public bool IsHealthy => (DateTime.UtcNow - LastHeartbeat).TotalSeconds < 30;
}

/// <summary>
/// Node status
/// </summary>
public enum NodeStatus
{
    /// <summary>Unknown status</summary>
    Unknown,
    
    /// <summary>Node is healthy</summary>
    Healthy,
    
    /// <summary>Node is degraded</summary>
    Degraded,
    
    /// <summary>Node is unhealthy</summary>
    Unhealthy,
    
    /// <summary>Node is offline</summary>
    Offline
}

