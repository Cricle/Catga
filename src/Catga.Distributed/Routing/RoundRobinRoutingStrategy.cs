using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>Round-robin routing (lock-free)</summary>
public sealed class RoundRobinRoutingStrategy : IRoutingStrategy
{
    private int _counter;

    public Task<NodeInfo?> SelectNodeAsync<TMessage>(IReadOnlyList<NodeInfo> nodes, TMessage message, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return Task.FromResult<NodeInfo?>(null);
        var index = Interlocked.Increment(ref _counter) % nodes.Count;
        return Task.FromResult<NodeInfo?>(nodes[index]);
    }
}

