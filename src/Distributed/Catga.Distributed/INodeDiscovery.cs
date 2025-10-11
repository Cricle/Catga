namespace Catga.Distributed;

/// <summary>
/// 节点发现服务
/// </summary>
public interface INodeDiscovery
{
    /// <summary>
    /// 注册当前节点
    /// </summary>
    public Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销当前节点
    /// </summary>
    public Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送心跳
    /// </summary>
    public Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有在线节点
    /// </summary>
    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听节点变化
    /// </summary>
    public IAsyncEnumerable<NodeChangeEvent> WatchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 节点变化事件
/// </summary>
public record NodeChangeEvent
{
    public required NodeChangeType Type { get; init; }
    public required NodeInfo Node { get; init; }
}

/// <summary>
/// 节点变化类型
/// </summary>
public enum NodeChangeType
{
    Joined,
    Left,
    Updated
}

