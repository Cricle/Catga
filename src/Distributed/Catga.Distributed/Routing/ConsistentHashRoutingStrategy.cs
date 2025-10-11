using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>
/// 一致性哈希路由策略（Consistent Hash）
/// 适用场景：分片、会话保持、缓存路由
/// 无锁实现，使用不可变哈希环
/// </summary>
public sealed class ConsistentHashRoutingStrategy : IRoutingStrategy
{
    private readonly int _virtualNodes;
    private readonly Func<string> _keyExtractor;

    /// <summary>
    /// 创建一致性哈希路由策略
    /// </summary>
    /// <param name="virtualNodes">每个节点的虚拟节点数（默认150，提高均匀性）</param>
    /// <param name="keyExtractor">提取路由键的函数（例如从消息上下文中）</param>
    public ConsistentHashRoutingStrategy(
        int virtualNodes = 150,
        Func<string>? keyExtractor = null)
    {
        _virtualNodes = virtualNodes;
        _keyExtractor = keyExtractor ?? (() => Guid.NewGuid().ToString());
    }

    public Task<NodeInfo?> SelectNodeAsync<TMessage>(
        IReadOnlyList<NodeInfo> nodes,
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
            return Task.FromResult<NodeInfo?>(null);

        if (nodes.Count == 1)
            return Task.FromResult<NodeInfo?>(nodes[0]);

        // 1. 提取消息的路由键
        var key = _keyExtractor();

        // 2. 计算消息的哈希值
        var messageHash = ComputeHash(key);

        // 3. 构建哈希环（虚拟节点）
        var ring = BuildHashRing(nodes);

        // 4. 在哈希环上查找节点（顺时针查找第一个节点）
        var selectedNode = FindNodeOnRing(ring, messageHash);

        return Task.FromResult<NodeInfo?>(selectedNode);
    }

    /// <summary>
    /// 构建哈希环
    /// </summary>
    private List<(uint Hash, NodeInfo Node)> BuildHashRing(IReadOnlyList<NodeInfo> nodes)
    {
        var ring = new List<(uint Hash, NodeInfo Node)>(nodes.Count * _virtualNodes);

        foreach (var node in nodes)
        {
            // 为每个节点创建虚拟节点
            for (int i = 0; i < _virtualNodes; i++)
            {
                var virtualKey = $"{node.NodeId}:{i}";
                var hash = ComputeHash(virtualKey);
                ring.Add((hash, node));
            }
        }

        // 按哈希值排序（构建环）
        ring.Sort((a, b) => a.Hash.CompareTo(b.Hash));

        return ring;
    }

    /// <summary>
    /// 在哈希环上查找节点（顺时针查找）
    /// </summary>
    private NodeInfo? FindNodeOnRing(List<(uint Hash, NodeInfo Node)> ring, uint messageHash)
    {
        // 二分查找：找到第一个 hash >= messageHash 的节点
        int left = 0, right = ring.Count - 1;
        int result = 0;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (ring[mid].Hash >= messageHash)
            {
                result = mid;
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }

        // 如果找到了，返回该节点
        if (result < ring.Count && ring[result].Hash >= messageHash)
            return ring[result].Node;

        // 否则，返回环上的第一个节点（环形）
        return ring.Count > 0 ? ring[0].Node : null;
    }

    /// <summary>
    /// 计算字符串的哈希值（使用 MD5，快速且均匀）
    /// </summary>
    private static uint ComputeHash(string key)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));

        // 取前 4 个字节作为 uint
        return BitConverter.ToUInt32(hash, 0);
    }
}

