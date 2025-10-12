using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>Random routing (thread-safe)</summary>
public sealed class RandomRoutingStrategy : IRoutingStrategy
{
    public Task<NodeInfo?> SelectNodeAsync<TMessage>(IReadOnlyList<NodeInfo> nodes, TMessage message, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return Task.FromResult<NodeInfo?>(null);
        if (nodes.Count == 1) return Task.FromResult<NodeInfo?>(nodes[0]);
        var index = Random.Shared.Next(nodes.Count);
        return Task.FromResult<NodeInfo?>(nodes[index]);
    }
}

