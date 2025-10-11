namespace Catga.Distributed.Routing;

/// <summary>
/// 基于负载的路由策略（Load-Based）
/// 选择负载最低的节点
/// 无锁实现，读取节点负载信息
/// </summary>
public sealed class LoadBasedRoutingStrategy : IRoutingStrategy
{
    public Task<NodeInfo?> SelectNodeAsync<TMessage>(
        IReadOnlyList<NodeInfo> nodes,
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
            return Task.FromResult<NodeInfo?>(null);

        if (nodes.Count == 1)
            return Task.FromResult<NodeInfo?>(nodes[0]);

        // 选择负载最低的节点（无锁读取）
        var selectedNode = nodes
            .OrderBy(n => n.Load)
            .ThenBy(n => n.NodeId) // 负载相同时，按 NodeId 排序（保证确定性）
            .FirstOrDefault();

        return Task.FromResult(selectedNode);
    }
}

