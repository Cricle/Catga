using System.Runtime.CompilerServices;
using Catga.Distributed.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>Redis Sorted Set node discovery (native persistence with TTL)</summary>
public sealed class RedisSortedSetNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSortedSetNodeDiscovery> _logger;
    private readonly string _sortedSetKey;
    private readonly TimeSpan _nodeTtl;

    public RedisSortedSetNodeDiscovery(IConnectionMultiplexer redis, ILogger<RedisSortedSetNodeDiscovery> logger, string sortedSetKey = "catga:nodes", TimeSpan? nodeTtl = null)
    {
        _redis = redis;
        _logger = logger;
        _sortedSetKey = sortedSetKey;
        _nodeTtl = nodeTtl ?? TimeSpan.FromMinutes(2);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var json = JsonHelper.SerializeNode(node);
        var score = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync(_sortedSetKey, json, score);
        await db.KeyExpireAsync(_sortedSetKey, _nodeTtl * 10);
        _logger.LogInformation("Registered node {NodeId} at {Endpoint} to Redis Sorted Set", node.NodeId, node.Endpoint);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var allEntries = await db.SortedSetRangeByScoreAsync(_sortedSetKey);
        foreach (var entry in allEntries)
        {
            var node = JsonHelper.DeserializeNode(entry.ToString()!);
            if (node?.NodeId == nodeId)
            {
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
            var allEntries = await db.SortedSetRangeByScoreAsync(_sortedSetKey);
            foreach (var entry in allEntries)
            {
                var existingNode = JsonHelper.DeserializeNode(entry.ToString()!);
                if (existingNode?.NodeId == nodeId)
                {
                    var updatedNode = existingNode with { LastSeen = DateTime.UtcNow, Load = load };
                    var updatedJson = JsonHelper.SerializeNode(updatedNode);
                    var newScore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
        catch (Exception ex) { _logger.LogError(ex, "Failed to send heartbeat for node {NodeId}", nodeId); }
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var nodes = new List<NodeInfo>();
        var now = DateTime.UtcNow;
        try
        {
            var entries = await db.SortedSetRangeByScoreAsync(_sortedSetKey);
            var expiredEntries = new List<RedisValue>();
            foreach (var entry in entries)
            {
                try
                {
                    var node = JsonHelper.DeserializeNode(entry.ToString()!);
                    if (node != null)
                    {
                        if ((now - node.LastSeen).TotalSeconds < _nodeTtl.TotalSeconds)
                            nodes.Add(node);
                        else
                            expiredEntries.Add(entry);
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to deserialize node entry"); }
            }
            if (expiredEntries.Count > 0)
            {
                await db.SortedSetRemoveAsync(_sortedSetKey, expiredEntries.ToArray());
                _logger.LogDebug("Removed {Count} expired nodes from Redis Sorted Set", expiredEntries.Count);
            }
            _logger.LogDebug("Retrieved {Count} online nodes from Redis Sorted Set", nodes.Count);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get nodes from Redis Sorted Set"); }
        return nodes;
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastNodes = new Dictionary<string, NodeInfo>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var currentNodes = await GetNodesAsync(cancellationToken);
            foreach (var node in currentNodes)
            {
                if (!lastNodes.ContainsKey(node.NodeId))
                    yield return new NodeChangeEvent { Type = NodeChangeType.Joined, Node = node };
                else if (lastNodes[node.NodeId].LastSeen != node.LastSeen)
                    yield return new NodeChangeEvent { Type = NodeChangeType.Updated, Node = node };
            }
            foreach (var oldNode in lastNodes.Values)
            {
                if (!currentNodes.Any(n => n.NodeId == oldNode.NodeId))
                    yield return new NodeChangeEvent { Type = NodeChangeType.Left, Node = oldNode };
            }
            lastNodes = currentNodes.ToDictionary(n => n.NodeId);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync() => await Task.CompletedTask;
}
