using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>
/// 基于 Redis Sorted Set 的节点发现（原生功能，持久化）
/// 移除内存缓存，直接使用 Redis Sorted Set
/// 按时间戳排序，自动淘汰过期节点
/// </summary>
public sealed class RedisSortedSetNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSortedSetNodeDiscovery> _logger;
    private readonly string _sortedSetKey;
    private readonly TimeSpan _nodeTtl;

    public RedisSortedSetNodeDiscovery(
        IConnectionMultiplexer redis,
        ILogger<RedisSortedSetNodeDiscovery> logger,
        string sortedSetKey = "catga:nodes",
        TimeSpan? nodeTtl = null)
    {
        _redis = redis;
        _logger = logger;
        _sortedSetKey = sortedSetKey;
        _nodeTtl = nodeTtl ?? TimeSpan.FromMinutes(2);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // 序列化节点信息
        var json = JsonSerializer.Serialize(node);

        // 使用当前时间戳作为 score（Sorted Set 原生功能）
        var score = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 添加到 Sorted Set（自动去重，按时间戳排序）
        await db.SortedSetAddAsync(_sortedSetKey, json, score);

        // 设置整个 Sorted Set 的 TTL（可选，防止整个集合过期）
        await db.KeyExpireAsync(_sortedSetKey, _nodeTtl * 10); // 10 倍 TTL

        _logger.LogInformation("Registered node {NodeId} at {Endpoint} to Redis Sorted Set",
            node.NodeId, node.Endpoint);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // 查找并删除节点
        var allEntries = await db.SortedSetRangeByScoreAsync(_sortedSetKey);

        foreach (var entry in allEntries)
        {
            var node = JsonSerializer.Deserialize<NodeInfo>(entry.ToString());
            if (node?.NodeId == nodeId)
            {
                // 从 Sorted Set 删除（原生操作）
                await db.SortedSetRemoveAsync(_sortedSetKey, entry);
                _logger.LogInformation("Unregistered node {NodeId} from Redis Sorted Set", nodeId);
                break;
            }
        }
    }

    public async Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        try
        {
            // 1. 查找节点
            var allEntries = await db.SortedSetRangeByScoreAsync(_sortedSetKey);

            foreach (var entry in allEntries)
            {
                var existingNode = JsonSerializer.Deserialize<NodeInfo>(entry.ToString());
                if (existingNode?.NodeId == nodeId)
                {
                    // 2. 更新节点信息
                    var updatedNode = existingNode with
                    {
                        LastSeen = DateTime.UtcNow,
                        Load = load
                    };

                    var updatedJson = JsonSerializer.Serialize(updatedNode);
                    var newScore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // 3. 删除旧条目，添加新条目（原子操作）
                    var batch = db.CreateBatch();
                    var removeTask = batch.SortedSetRemoveAsync(_sortedSetKey, entry);
                    var addTask = batch.SortedSetAddAsync(_sortedSetKey, updatedJson, newScore);
                    batch.Execute();

                    await Task.WhenAll(removeTask, addTask);

                    _logger.LogDebug("Heartbeat for node {NodeId}, load: {Load}", nodeId, load);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat for node {NodeId}", nodeId);
        }
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var nodes = new List<NodeInfo>();
        var now = DateTime.UtcNow;

        try
        {
            // 从 Redis Sorted Set 读取所有节点（原生操作，按时间戳排序）
            var entries = await db.SortedSetRangeByScoreAsync(_sortedSetKey);

            var expiredEntries = new List<RedisValue>();

            foreach (var entry in entries)
            {
                try
                {
                    var node = JsonSerializer.Deserialize<NodeInfo>(entry.ToString());

                    if (node != null)
                    {
                        // 检查节点是否在 TTL 内
                        if ((now - node.LastSeen).TotalSeconds < _nodeTtl.TotalSeconds)
                        {
                            nodes.Add(node);
                        }
                        else
                        {
                            // 记录过期节点，稍后删除
                            expiredEntries.Add(entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize node entry");
                }
            }

            // 批量删除过期节点（清理）
            if (expiredEntries.Count > 0)
            {
                await db.SortedSetRemoveAsync(_sortedSetKey, expiredEntries.ToArray());
                _logger.LogDebug("Removed {Count} expired nodes from Redis Sorted Set", expiredEntries.Count);
            }

            _logger.LogDebug("Retrieved {Count} online nodes from Redis Sorted Set", nodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get nodes from Redis Sorted Set");
        }

        return nodes;
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Redis Sorted Set 不支持原生 Watch，使用轮询模式
        var lastNodes = new Dictionary<string, NodeInfo>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var currentNodes = await GetNodesAsync(cancellationToken);

            // 检测新加入的节点
            foreach (var node in currentNodes)
            {
                if (!lastNodes.ContainsKey(node.NodeId))
                {
                    yield return new NodeChangeEvent
                    {
                        Type = NodeChangeType.Joined,
                        Node = node
                    };
                }
                else if (lastNodes[node.NodeId].LastSeen != node.LastSeen)
                {
                    yield return new NodeChangeEvent
                    {
                        Type = NodeChangeType.Updated,
                        Node = node
                    };
                }
            }

            // 检测离开的节点
            foreach (var oldNode in lastNodes.Values)
            {
                if (!currentNodes.Any(n => n.NodeId == oldNode.NodeId))
                {
                    yield return new NodeChangeEvent
                    {
                        Type = NodeChangeType.Left,
                        Node = oldNode
                    };
                }
            }

            lastNodes = currentNodes.ToDictionary(n => n.NodeId);

            // 等待一段时间再检查
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}

