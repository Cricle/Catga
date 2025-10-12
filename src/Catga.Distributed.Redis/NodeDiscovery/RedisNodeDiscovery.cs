using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Catga.Distributed.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>Redis node discovery with keyspace notifications</summary>
public sealed class RedisNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisNodeDiscovery> _logger;
    private readonly string _keyPrefix;
    private readonly TimeSpan _nodeExpiry;
    private readonly Channel<NodeChangeEvent> _events;
    private readonly CancellationTokenSource _watchCts;
    private readonly Task _backgroundTask;

    public RedisNodeDiscovery(IConnectionMultiplexer redis, ILogger<RedisNodeDiscovery> logger, string keyPrefix = "catga:nodes:", TimeSpan? nodeExpiry = null)
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = keyPrefix;
        _nodeExpiry = nodeExpiry ?? TimeSpan.FromMinutes(2);
        _events = Channel.CreateUnbounded<NodeChangeEvent>();
        _watchCts = new CancellationTokenSource();
        _backgroundTask = WatchRedisKeysAsync(_watchCts.Token);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_keyPrefix}{node.NodeId}";
        var json = JsonHelper.SerializeNode(node);
        await db.StringSetAsync(key, json, _nodeExpiry);
        _logger.LogInformation("Registered node {NodeId} at {Endpoint}", node.NodeId, node.Endpoint);
        await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Joined, Node = node }, cancellationToken);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_keyPrefix}{nodeId}";
        await db.KeyDeleteAsync(key);
        _logger.LogInformation("Unregistered node {NodeId}", nodeId);
    }

    public async Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_keyPrefix}{nodeId}";
        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            return;
        }
        var node = JsonHelper.DeserializeNode(json.ToString()!);
        if (node == null) return;
        var updatedNode = node with { LastSeen = DateTime.UtcNow, Load = load };
        var updatedJson = JsonHelper.SerializeNode(updatedNode);
        await db.StringSetAsync(key, updatedJson, _nodeExpiry);
        await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Updated, Node = updatedNode }, cancellationToken);
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var nodes = new List<NodeInfo>();
        try
        {
            await foreach (var key in server.KeysAsync(pattern: $"{_keyPrefix}*"))
            {
                var json = await db.StringGetAsync(key);
                if (json.HasValue)
                {
                    var node = JsonHelper.DeserializeNode(json.ToString()!);
                    if (node != null && node.IsOnline)
                        nodes.Add(node);
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get nodes from Redis"); }
        return nodes;
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
            yield return @event;
    }

    private async Task WatchRedisKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            var subscriber = _redis.GetSubscriber();
            var channel = RedisChannel.Pattern($"__keyspace@0__:{_keyPrefix}*");
            await subscriber.SubscribeAsync(channel, async (ch, message) =>
            {
                try
                {
                    var operation = message.ToString();
                    var key = ch.ToString().Replace($"__keyspace@0__:", "");
                    if (operation == "set")
                    {
                        var db = _redis.GetDatabase();
                        var json = await db.StringGetAsync(key);
                        if (json.HasValue)
                        {
                            var node = JsonHelper.DeserializeNode(json.ToString()!);
                            if (node != null)
                                await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Joined, Node = node }, cancellationToken);
                        }
                    }
                    else if (operation == "del" || operation == "expired")
                    {
                        var nodeId = key.Replace(_keyPrefix, "");
                        _logger.LogInformation("Node {NodeId} left (operation: {Operation})", nodeId, operation);
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Error processing Redis keyspace notification"); }
            });
            _logger.LogInformation("Started watching Redis keyspace notifications for pattern: {Pattern}", channel);
            while (!cancellationToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Error in Redis watch loop"); }
    }

    public async ValueTask DisposeAsync()
    {
        _watchCts.Cancel();
        _events.Writer.Complete();
        try
        {
            await _backgroundTask.ConfigureAwait(false);
            await _events.Reader.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Error during disposal"); }
        _watchCts.Dispose();
    }
}
