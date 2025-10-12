using System.Security.Cryptography;
using System.Text;

namespace Catga.Distributed.Routing;

/// <summary>Consistent Hash routing (lock-free with virtual nodes)</summary>
public sealed class ConsistentHashRoutingStrategy : IRoutingStrategy
{
    private static readonly MD5 md5 = MD5.Create();
    private readonly int _virtualNodes;
    private readonly Func<string> _keyExtractor;

    public ConsistentHashRoutingStrategy(int virtualNodes = 150, Func<string>? keyExtractor = null)
    {
        _virtualNodes = virtualNodes;
        _keyExtractor = keyExtractor ?? (() => Guid.NewGuid().ToString());
    }

    public Task<NodeInfo?> SelectNodeAsync<TMessage>(IReadOnlyList<NodeInfo> nodes, TMessage message, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return Task.FromResult<NodeInfo?>(null);
        if (nodes.Count == 1) return Task.FromResult<NodeInfo?>(nodes[0]);
        var key = _keyExtractor();
        var messageHash = ComputeHash(key);
        var ring = BuildHashRing(nodes);
        return Task.FromResult(FindNodeOnRing(ring, messageHash));
    }

    private List<(uint Hash, NodeInfo Node)> BuildHashRing(IReadOnlyList<NodeInfo> nodes)
    {
        var ring = new List<(uint Hash, NodeInfo Node)>(nodes.Count * _virtualNodes);
        foreach (var node in nodes)
        {
            for (var i = 0; i < _virtualNodes; i++)
            {
                var hash = ComputeHash($"{node.NodeId}:{i}");
                ring.Add((hash, node));
            }
        }
        ring.Sort((a, b) => a.Hash.CompareTo(b.Hash));
        return ring;
    }

    private NodeInfo? FindNodeOnRing(List<(uint Hash, NodeInfo Node)> ring, uint messageHash)
    {
        int left = 0, right = ring.Count - 1, result = 0;
        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            if (ring[mid].Hash >= messageHash)
            {
                result = mid;
                right = mid - 1;
            }
            else
                left = mid + 1;
        }
        if (result < ring.Count && ring[result].Hash >= messageHash)
            return ring[result].Node;
        return ring.Count > 0 ? ring[0].Node : null;
    }

    private static uint ComputeHash(string key) => BitConverter.ToUInt32(md5.ComputeHash(Encoding.UTF8.GetBytes(key)), 0);
}
