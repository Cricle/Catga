using System.Collections.Concurrent;

namespace Catga.Cluster.Routing;

/// <summary>
/// 最少连接路由（基于活跃连接数）
/// </summary>
public sealed class LeastConnectionsRouter : IMessageRouter
{
    private readonly ConcurrentDictionary<string, int> _activeConnections = new();

    public Task<ClusterNode> RouteAsync<TMessage>(
        TMessage message,
        IReadOnlyList<ClusterNode> nodes,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("No nodes available");
        }

        // 选择连接数最少的节点
        var selectedNode = nodes
            .OrderBy(n => GetActiveConnections(n.NodeId))
            .ThenBy(n => n.Load)  // 负载作为次要排序条件
            .First();

        // 增加连接计数
        IncrementConnections(selectedNode.NodeId);

        return Task.FromResult(selectedNode);
    }

    /// <summary>
    /// 获取节点的活跃连接数
    /// </summary>
    public int GetActiveConnections(string nodeId)
    {
        return _activeConnections.GetValueOrDefault(nodeId, 0);
    }

    /// <summary>
    /// 增加连接计数
    /// </summary>
    private void IncrementConnections(string nodeId)
    {
        _activeConnections.AddOrUpdate(nodeId, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// 减少连接计数（请求完成后调用）
    /// </summary>
    public void DecrementConnections(string nodeId)
    {
        _activeConnections.AddOrUpdate(nodeId, 0, (_, count) => Math.Max(0, count - 1));
    }

    /// <summary>
    /// 重置连接计数
    /// </summary>
    public void ResetConnections(string nodeId)
    {
        _activeConnections.TryRemove(nodeId, out _);
    }
}

