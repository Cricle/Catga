using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>
/// 基于 Redis 的节点发现
/// </summary>
public sealed class RedisNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisNodeDiscovery> _logger;
    private readonly string _keyPrefix;
    private readonly TimeSpan _nodeExpiry;
    private readonly Channel<NodeChangeEvent> _events;
    private readonly CancellationTokenSource _watchCts;

    public RedisNodeDiscovery(
        IConnectionMultiplexer redis,
        ILogger<RedisNodeDiscovery> logger,
        string keyPrefix = "catga:nodes:",
        TimeSpan? nodeExpiry = null)
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = keyPrefix;
        _nodeExpiry = nodeExpiry ?? TimeSpan.FromMinutes(2);
        _events = Channel.CreateUnbounded<NodeChangeEvent>();
        _watchCts = new CancellationTokenSource();

        // 启动后台监听
        _ = WatchRedisKeysAsync(_watchCts.Token);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_keyPrefix}{node.NodeId}";
        var json = JsonSerializer.Serialize(node);

        await db.StringSetAsync(key, json, _nodeExpiry);

        _logger.LogInformation("Registered node {NodeId} at {Endpoint}", node.NodeId, node.Endpoint);

        await _events.Writer.WriteAsync(new NodeChangeEvent
        {
            Type = NodeChangeType.Joined,
            Node = node
        }, cancellationToken);
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

        // 获取现有节点信息
        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            return;
        }

        var node = JsonSerializer.Deserialize<NodeInfo>(json.ToString());
        if (node == null) return;

        // 更新节点信息
        var updatedNode = node with
        {
            LastSeen = DateTime.UtcNow,
            Load = load
        };

        var updatedJson = JsonSerializer.Serialize(updatedNode);
        await db.StringSetAsync(key, updatedJson, _nodeExpiry);

        await _events.Writer.WriteAsync(new NodeChangeEvent
        {
            Type = NodeChangeType.Updated,
            Node = updatedNode
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        
        var nodes = new List<NodeInfo>();

        try
        {
            // 扫描所有节点键
            await foreach (var key in server.KeysAsync(pattern: $"{_keyPrefix}*"))
            {
                var json = await db.StringGetAsync(key);
                if (json.HasValue)
                {
                    var node = JsonSerializer.Deserialize<NodeInfo>(json.ToString());
                    if (node != null && node.IsOnline)
                    {
                        nodes.Add(node);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get nodes from Redis");
        }

        return nodes;
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return @event;
        }
    }

    /// <summary>
    /// 后台监听 Redis 键变化（使用 Keyspace Notifications）
    /// </summary>
    private async Task WatchRedisKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            var subscriber = _redis.GetSubscriber();
            
            // 订阅键空间通知
            var channel = $"__keyspace@0__:{_keyPrefix}*";
            await subscriber.SubscribeAsync(channel, async (ch, message) =>
            {
                try
                {
                    // 消息格式: <操作类型> 例如: "set", "del", "expired"
                    var operation = message.ToString();
                    var key = ch.ToString().Replace($"__keyspace@0__:", "");

                    if (operation == "set")
                    {
                        // 节点加入或更新
                        var db = _redis.GetDatabase();
                        var json = await db.StringGetAsync(key);
                        if (json.HasValue)
                        {
                            var node = JsonSerializer.Deserialize<NodeInfo>(json.ToString());
                            if (node != null)
                            {
                                await _events.Writer.WriteAsync(new NodeChangeEvent
                                {
                                    Type = NodeChangeType.Joined,
                                    Node = node
                                }, cancellationToken);
                            }
                        }
                    }
                    else if (operation == "del" || operation == "expired")
                    {
                        // 节点离开（键删除或过期）
                        var nodeId = key.Replace(_keyPrefix, "");
                        _logger.LogInformation("Node {NodeId} left (operation: {Operation})", nodeId, operation);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Redis keyspace notification");
                }
            });

            _logger.LogInformation("Started watching Redis keyspace notifications for pattern: {Pattern}", channel);

            // 保持订阅活跃
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Redis watch loop");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watchCts.Cancel();
        _events.Writer.Complete();
        
        try
        {
            await _events.Reader.Completion;
        }
        catch
        {
            // Ignore
        }

        _watchCts.Dispose();
    }
}

