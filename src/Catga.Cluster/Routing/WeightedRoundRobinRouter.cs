namespace Catga.Cluster.Routing;

/// <summary>
/// 加权轮询路由（基于节点负载）
/// </summary>
public sealed class WeightedRoundRobinRouter : IMessageRouter
{
    public Task<ClusterNode> RouteAsync<TMessage>(
        TMessage message,
        IReadOnlyList<ClusterNode> nodes,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("No nodes available");
        }

        // 计算权重（负载越低，权重越高）
        // 权重 = 100 - Load（0-100）
        var weightedNodes = nodes
            .Select(n => new { Node = n, Weight = Math.Max(1, 100 - n.Load) })
            .ToList();

        var totalWeight = weightedNodes.Sum(n => n.Weight);
        
        // 使用线程安全的随机数生成
        var random = Random.Shared.Next(0, totalWeight);
        
        // 加权选择
        var currentWeight = 0;
        foreach (var item in weightedNodes)
        {
            currentWeight += item.Weight;
            if (random < currentWeight)
            {
                return Task.FromResult(item.Node);
            }
        }

        // 兜底：返回第一个节点
        return Task.FromResult(nodes[0]);
    }
}

