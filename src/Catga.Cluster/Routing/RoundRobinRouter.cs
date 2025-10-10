namespace Catga.Cluster.Routing;

/// <summary>
/// 轮询路由（默认策略）
/// </summary>
public sealed class RoundRobinRouter : IMessageRouter
{
    private int _counter;

    public Task<ClusterNode> RouteAsync<TMessage>(
        TMessage message,
        IReadOnlyList<ClusterNode> nodes,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("No nodes available");
        }

        // 轮询选择节点
        var index = Interlocked.Increment(ref _counter) % nodes.Count;
        return Task.FromResult(nodes[index]);
    }
}

