namespace Catga.Cluster.Discovery;

/// <summary>
/// 节点发现接口
/// </summary>
public interface INodeDiscovery
{
    /// <summary>
    /// 注册当前节点
    /// </summary>
    Task RegisterAsync(ClusterNode node, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销当前节点
    /// </summary>
    Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送心跳
    /// </summary>
    Task HeartbeatAsync(string nodeId, int load, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有在线节点
    /// </summary>
    Task<IReadOnlyList<ClusterNode>> GetNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听节点变化
    /// </summary>
    Task<IAsyncEnumerable<ClusterEvent>> WatchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 集群事件
/// </summary>
public record ClusterEvent
{
    public ClusterEventType Type { get; init; }
    public ClusterNode? Node { get; init; }
}

/// <summary>
/// 集群事件类型
/// </summary>
public enum ClusterEventType
{
    NodeJoined,
    NodeLeft,
    NodeFaulted
}

