namespace Catga.Distributed.Routing;

/// <summary>Routing strategy for node selection</summary>
public interface IRoutingStrategy
{
    public Task<NodeInfo?> SelectNodeAsync<TMessage>(IReadOnlyList<NodeInfo> nodes, TMessage message, CancellationToken cancellationToken = default);
}

