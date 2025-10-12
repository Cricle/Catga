using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>Local-first routing with round-robin fallback</summary>
public sealed class LocalFirstRoutingStrategy : IRoutingStrategy
{
    private readonly string _currentNodeId;
    private readonly RoundRobinRoutingStrategy _fallbackStrategy = new();

    public LocalFirstRoutingStrategy(string currentNodeId) => _currentNodeId = currentNodeId;

    public Task<NodeInfo?> SelectNodeAsync<TMessage>(IReadOnlyList<NodeInfo> nodes, TMessage message, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return Task.FromResult<NodeInfo?>(null);
        var localNode = nodes.FirstOrDefault(n => n.NodeId == _currentNodeId);
        if (localNode != null) return Task.FromResult<NodeInfo?>(localNode);
        return _fallbackStrategy.SelectNodeAsync(nodes, message, cancellationToken);
    }
}

