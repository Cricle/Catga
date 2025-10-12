namespace Catga.Distributed.Routing;

/// <summary>Load-based routing (selects lowest load node)</summary>
public sealed class LoadBasedRoutingStrategy : IRoutingStrategy
{
    public Task<NodeInfo?> SelectNodeAsync<TMessage>(IReadOnlyList<NodeInfo> nodes, TMessage message, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return Task.FromResult<NodeInfo?>(null);
        if (nodes.Count == 1) return Task.FromResult<NodeInfo?>(nodes[0]);
        var selectedNode = nodes.OrderBy(n => n.Load).ThenBy(n => n.NodeId).FirstOrDefault();
        return Task.FromResult(selectedNode);
    }
}

