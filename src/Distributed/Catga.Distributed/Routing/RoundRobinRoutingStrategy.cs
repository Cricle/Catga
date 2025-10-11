using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>
/// 轮询路由策略（Round-Robin）
/// 无锁实现，使用 Interlocked.Increment
/// </summary>
public sealed class RoundRobinRoutingStrategy : IRoutingStrategy
{
    private int _counter;

    public Task<NodeInfo?> SelectNodeAsync<TMessage>(
        IReadOnlyList<NodeInfo> nodes,
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
            return Task.FromResult<NodeInfo?>(null);

        // 无锁计数器递增
        var index = Interlocked.Increment(ref _counter) % nodes.Count;
        return Task.FromResult<NodeInfo?>(nodes[index]);
    }
}

