namespace Catga.Distributed;

/// <summary>Node discovery service</summary>
public interface INodeDiscovery
{
    public Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default);
    public Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default);
    public Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default);
    public IAsyncEnumerable<NodeChangeEvent> WatchAsync(CancellationToken cancellationToken = default);
}

/// <summary>Node change event</summary>
public record NodeChangeEvent
{
    public required NodeChangeType Type { get; init; }
    public required NodeInfo Node { get; init; }
}

/// <summary>Node change type</summary>
public enum NodeChangeType
{
    Joined,
    Left,
    Updated
}

