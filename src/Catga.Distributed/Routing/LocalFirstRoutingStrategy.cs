using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>
/// 本地优先路由策略（Local-First）
/// 优先选择本地节点，如果本地不可用则使用轮询策略
/// </summary>
public sealed class LocalFirstRoutingStrategy : IRoutingStrategy
{
    private readonly string _currentNodeId;
    private readonly RoundRobinRoutingStrategy _fallbackStrategy = new();

    public LocalFirstRoutingStrategy(string currentNodeId)
    {
        _currentNodeId = currentNodeId;
    }

    public Task<NodeInfo?> SelectNodeAsync(
        IReadOnlyList<NodeInfo> nodes,
        object message,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
            return Task.FromResult<NodeInfo?>(null);

        // 优先选择本地节点
        var localNode = nodes.FirstOrDefault(n => n.NodeId == _currentNodeId);
        if (localNode != null)
            return Task.FromResult<NodeInfo?>(localNode);

        // 本地节点不可用，使用轮询策略
        return _fallbackStrategy.SelectNodeAsync(nodes, message, cancellationToken);
    }
}

