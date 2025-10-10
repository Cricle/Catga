using System.Security.Cryptography;
using System.Text;

namespace Catga.Cluster.Routing;

/// <summary>
/// 一致性哈希路由（适用于需要会话亲和性的场景）
/// </summary>
public sealed class ConsistentHashRouter : IMessageRouter
{
    private readonly int _virtualNodeCount;

    /// <summary>
    /// 创建一致性哈希路由器
    /// </summary>
    /// <param name="virtualNodeCount">虚拟节点数量（默认：150，增加虚拟节点可提高分布均匀性）</param>
    public ConsistentHashRouter(int virtualNodeCount = 150)
    {
        _virtualNodeCount = virtualNodeCount;
    }

    public Task<ClusterNode> RouteAsync<TMessage>(
        TMessage message,
        IReadOnlyList<ClusterNode> nodes,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("No nodes available");
        }

        // 构建一致性哈希环
        var ring = BuildHashRing(nodes);

        // 计算消息的哈希值
        var messageHash = ComputeHash(message);

        // 查找顺时针方向最近的节点
        var targetNode = FindNode(ring, messageHash);

        return Task.FromResult(targetNode);
    }

    /// <summary>
    /// 构建哈希环（虚拟节点）
    /// </summary>
    private SortedDictionary<uint, ClusterNode> BuildHashRing(IReadOnlyList<ClusterNode> nodes)
    {
        var ring = new SortedDictionary<uint, ClusterNode>();

        foreach (var node in nodes)
        {
            // 为每个物理节点创建多个虚拟节点
            for (var i = 0; i < _virtualNodeCount; i++)
            {
                var virtualNodeKey = $"{node.NodeId}:{i}";
                var hash = ComputeHash(virtualNodeKey);
                ring[hash] = node;
            }
        }

        return ring;
    }

    /// <summary>
    /// 在哈希环上查找节点（顺时针查找）
    /// </summary>
    private ClusterNode FindNode(SortedDictionary<uint, ClusterNode> ring, uint hash)
    {
        // 查找第一个大于等于 hash 的节点
        foreach (var kvp in ring)
        {
            if (kvp.Key >= hash)
            {
                return kvp.Value;
            }
        }

        // 如果没找到，返回第一个节点（环形）
        return ring.First().Value;
    }

    /// <summary>
    /// 计算哈希值（使用 MurmurHash3 的简化版本）
    /// </summary>
    private uint ComputeHash<T>(T value)
    {
        var key = value?.ToString() ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(key);

        // 使用 MD5 取前 4 字节（简化，生产环境建议使用 MurmurHash3）
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToUInt32(hash, 0);
    }
}

