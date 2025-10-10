namespace Catga.Cluster;

/// <summary>
/// 集群节点信息
/// </summary>
public sealed record ClusterNode
{
    /// <summary>
    /// 节点 ID（唯一标识）
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// 节点端点（http://ip:port）
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// 节点状态
    /// </summary>
    public NodeStatus Status { get; init; } = NodeStatus.Online;

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime LastHeartbeat { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 节点负载（0-100）
    /// </summary>
    public int Load { get; init; }

    /// <summary>
    /// 节点元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// 节点状态
/// </summary>
public enum NodeStatus
{
    /// <summary>
    /// 在线
    /// </summary>
    Online,

    /// <summary>
    /// 离线
    /// </summary>
    Offline,

    /// <summary>
    /// 故障
    /// </summary>
    Faulted
}

