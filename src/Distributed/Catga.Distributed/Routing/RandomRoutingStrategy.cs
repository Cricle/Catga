using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>
/// 随机路由策略（Random）
/// 随机选择一个节点
/// 线程安全，使用 Random.Shared（.NET 6+）
/// </summary>
public sealed class RandomRoutingStrategy : IRoutingStrategy
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

        // 使用 Random.Shared（线程安全，.NET 6+）
        var index = Random.Shared.Next(nodes.Count);
        return Task.FromResult<NodeInfo?>(nodes[index]);
    }
}

