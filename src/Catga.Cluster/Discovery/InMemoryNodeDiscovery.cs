using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Catga.Cluster.Discovery;

/// <summary>
/// 内存节点发现（用于测试和单机开发）
/// </summary>
public sealed class InMemoryNodeDiscovery : INodeDiscovery
{
    private readonly ConcurrentDictionary<string, ClusterNode> _nodes = new();
    private readonly Channel<ClusterEvent> _events = Channel.CreateUnbounded<ClusterEvent>();
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);

    public async Task RegisterAsync(ClusterNode node, CancellationToken cancellationToken = default)
    {
        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);
        
        await _events.Writer.WriteAsync(new ClusterEvent
        {
            Type = ClusterEventType.NodeJoined,
            Node = node
        }, cancellationToken);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (_nodes.TryRemove(nodeId, out var node))
        {
            await _events.Writer.WriteAsync(new ClusterEvent
            {
                Type = ClusterEventType.NodeLeft,
                Node = node
            }, cancellationToken);
        }
    }

    public Task HeartbeatAsync(string nodeId, int load, CancellationToken cancellationToken = default)
    {
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            var updated = node with
            {
                LastHeartbeat = DateTime.UtcNow,
                Load = load,
                Status = NodeStatus.Online
            };
            _nodes.TryUpdate(nodeId, updated, node);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClusterNode>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        // 过滤掉超时的节点
        var now = DateTime.UtcNow;
        var onlineNodes = _nodes.Values
            .Where(n => now - n.LastHeartbeat < _heartbeatTimeout)
            .ToList();

        return Task.FromResult<IReadOnlyList<ClusterNode>>(onlineNodes);
    }

    public Task<IAsyncEnumerable<ClusterEvent>> WatchAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ReadEventsAsync(cancellationToken));
    }

    private async IAsyncEnumerable<ClusterEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return @event;
        }
    }
}

