using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Distributed.Nats;

/// <summary>
/// 基于 NATS JetStream KV Store 的持久化节点发现
/// 当前实现：内存 + TTL 清理（占位符）
///
/// TODO: 适配 NATS.Client.JetStream 的原生 KV Store API
/// 参考：https://docs.nats.io/using-nats/developer/develop_jetstream/kv#tab-c-12
///
/// 原生 API 用法（待适配）：
/// - await jsContext.CreateKeyValueAsync(kvConfig)  // 创建 KV Store
/// - await kvStore.PutAsync(key, value)             // 存储键值
/// - await kvStore.GetEntryAsync(key)               // 获取键值
/// - await kvStore.DeleteAsync(key)                 // 删除键
/// - await kvStore.GetKeysAsync()                   // 获取所有键
/// - await kvStore.WatchAsync()                     // 监听变更
///
/// 注意：NATS.Client.JetStream 2.5.2 的类型定义可能与文档不完全匹配
/// 需要根据实际包版本调整 API 调用
/// </summary>
public sealed class NatsJetStreamKVNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsJetStreamKVNodeDiscovery> _logger;
    private readonly string _bucketName;
    private readonly TimeSpan _nodeTtl;

    // 当前使用内存缓存（待替换为原生 KV Store）
    private readonly ConcurrentDictionary<string, NodeInfo> _localCache = new();

    // 无锁事件流：Channel 保证无锁通信
    private readonly Channel<NodeChangeEvent> _events;

    private readonly CancellationTokenSource _disposeCts;
    private INatsJSContext? _jsContext;
    private Task? _watchTask;

    public NatsJetStreamKVNodeDiscovery(
        INatsConnection connection,
        ILogger<NatsJetStreamKVNodeDiscovery> logger,
        string bucketName = "catga_nodes",
        TimeSpan? nodeTtl = null)
    {
        _connection = connection;
        _logger = logger;
        _bucketName = bucketName;
        _nodeTtl = nodeTtl ?? TimeSpan.FromMinutes(5); // 默认 5 分钟 TTL
        _events = Channel.CreateUnbounded<NodeChangeEvent>();
        _disposeCts = new CancellationTokenSource();

        // 初始化 JetStream 和 KV Store
        _ = InitializeAsync(_disposeCts.Token);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 创建 JetStream Context
            _jsContext = new NatsJSContext(_connection);

            // TODO: 实现 JetStream KV Store 原生持久化
            // 当前 NATS.Client.JetStream 2.5.2 的 API 与文档不匹配
            // 暂时使用内存 + TTL 过期清理机制

            _logger.LogWarning(
                "JetStream KV Store '{Bucket}' using in-memory mode with TTL {Ttl}. " +
                "Native KV Store persistence is planned but requires API version alignment. " +
                "See class documentation for native API usage.",
                _bucketName, _nodeTtl);

            // 启动 TTL 清理任务
            _watchTask = StartTtlCleanupAsync(cancellationToken);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JetStream context '{Bucket}'", _bucketName);
            throw;
        }
    }

    /// <summary>
    /// TTL 清理任务（当前内存模式下使用）
    /// TODO: 使用原生 KV Store 的 MaxAge 配置替代
    /// </summary>
    private async Task StartTtlCleanupAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);

                var now = DateTime.UtcNow;
                var expiredNodes = _localCache
                    .Where(kvp => now - kvp.Value.LastSeen > _nodeTtl)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var nodeId in expiredNodes)
                {
                    if (_localCache.TryRemove(nodeId, out var node))
                    {
                        await _events.Writer.WriteAsync(new NodeChangeEvent
                        {
                            Type = NodeChangeType.Left,
                            Node = node
                        }, cancellationToken);

                        _logger.LogDebug("Node {NodeId} expired (TTL cleanup)", nodeId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TTL cleanup");
            }
        }
    }


    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        // 更新本地缓存（无锁）
        _localCache.AddOrUpdate(node.NodeId, node, (_, _) => node);

        // TODO: 持久化到 KV Store
        // await _kvStore.PutAsync(GetNodeKey(node.NodeId), JsonSerializer.Serialize(node));

        _logger.LogInformation("Node {NodeId} registered (in-memory, TTL: {Ttl}) at {Endpoint}",
            node.NodeId, _nodeTtl, node.Endpoint);

        // 发布节点变更事件
        await _events.Writer.WriteAsync(new NodeChangeEvent
        {
            Type = NodeChangeType.Joined,
            Node = node
        }, cancellationToken);
    }

    public Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        // 查找节点信息
        if (!_localCache.TryGetValue(nodeId, out var node))
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            return Task.CompletedTask;
        }

        // 更新时间戳和负载
        node = node with { LastSeen = DateTime.UtcNow, Load = load };

        // 更新本地缓存（自动刷新 TTL）
        _localCache.AddOrUpdate(nodeId, node, (_, _) => node);

        // TODO: 更新 KV Store（自动刷新 TTL）
        // await _kvStore.PutAsync(GetNodeKey(nodeId), JsonSerializer.Serialize(node));

        _logger.LogTrace("Node {NodeId} heartbeat sent with load {Load}, TTL refreshed in memory", nodeId, load);
        return Task.CompletedTask;
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // 从本地缓存删除
        if (_localCache.TryRemove(nodeId, out var node))
        {
            // TODO: 从 KV Store 删除
            // await _kvStore.DeleteAsync(GetNodeKey(nodeId));

            _logger.LogInformation("Node {NodeId} unregistered from memory", nodeId);

            // 发布节点离开事件
            await _events.Writer.WriteAsync(new NodeChangeEvent
            {
                Type = NodeChangeType.Left,
                Node = node
            }, cancellationToken);
        }
    }

    public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // 无锁读取本地缓存（ConcurrentDictionary 保证线程安全）
        _localCache.TryGetValue(nodeId, out var node);
        return Task.FromResult(node);
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        // 无锁读取所有节点（本地缓存，ConcurrentDictionary.Values 是线程安全的）
        var nodes = _localCache.Values.ToList();

        // KV Store 的 TTL 会自动删除过期节点，但本地缓存可能有延迟
        // 过滤明显过期的节点
        var activeNodes = nodes
            .Where(n => DateTime.UtcNow - n.LastSeen < _nodeTtl * 2) // 给 2 倍余量
            .ToList();

        return Task.FromResult<IReadOnlyList<NodeInfo>>(activeNodes);
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 无锁读取事件流（Channel 保证无锁通信）
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return @event;
        }
    }


    private string GetNodeKey(string nodeId) => $"node.{nodeId}";

    private string GetNodeIdFromKey(string key)
    {
        // 从 "node.{nodeId}" 中提取 nodeId
        return key.StartsWith("node.") ? key.Substring(5) : key;
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _events.Writer.Complete();

        // 等待所有异步操作完成
        await Task.Delay(100);

        _disposeCts.Dispose();
    }
}

