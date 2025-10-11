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
/// 完全无锁设计：使用 ConcurrentDictionary + Channel + KV Store
/// 特性：持久化、历史记录、自动过期
/// </summary>
public sealed class NatsJetStreamKVNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsJetStreamKVNodeDiscovery> _logger;
    private readonly string _bucketName;
    private readonly TimeSpan _nodeTtl;

    // 无锁数据结构：ConcurrentDictionary 保证线程安全
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();

    // 无锁事件流：Channel 保证无锁通信
    private readonly Channel<NodeChangeEvent> _events;

    private readonly CancellationTokenSource _disposeCts;
    private INatsJSContext? _jsContext;
    // 注意：NATS KV Store API 在不同版本有差异
    // 当前暂时使用内存 + TTL 过期清理，生产环境建议使用 JetStream KV Store
    // TODO: 根据实际 NATS.Client.JetStream 版本适配 API

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

            // TODO: 实现 JetStream KV Store 持久化
            // 当前 NATS.Client.JetStream API 需要根据实际版本适配
            // 暂时使用内存 + Pub/Sub + TTL 过期清理机制
            
            _logger.LogWarning("JetStream KV Store '{Bucket}' using in-memory mode with TTL {Ttl}. " +
                             "For production, please implement native KV Store persistence based on your NATS.Client version", 
                _bucketName, _nodeTtl);

            // 启动 TTL 清理任务
            _ = StartTtlCleanupAsync(cancellationToken);
            
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
    /// </summary>
    private async Task StartTtlCleanupAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);

                var now = DateTime.UtcNow;
                var expiredNodes = _nodes.Where(kvp => now - kvp.Value.LastSeen > _nodeTtl)
                                        .Select(kvp => kvp.Key)
                                        .ToList();

                foreach (var nodeId in expiredNodes)
                {
                    if (_nodes.TryRemove(nodeId, out var node))
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
        // 无锁更新本地缓存
        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

        // TODO: 持久化到 KV Store (需要适配 NATS API)
        // 当前使用内存模式，节点重启后需要重新注册
        
        _logger.LogDebug("Node {NodeId} registered (in-memory mode, TTL: {Ttl})", node.NodeId, _nodeTtl);

        // 发布节点变更事件
        await _events.Writer.WriteAsync(new NodeChangeEvent
        {
            Type = NodeChangeType.Joined,
            Node = node
        }, cancellationToken);
    }

    public Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        // 查找或创建节点信息
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            return Task.CompletedTask;
        }

        // 更新时间戳和负载
        node = node with { LastSeen = DateTime.UtcNow, Load = load };

        // 无锁更新本地缓存（自动刷新 TTL）
        _nodes.AddOrUpdate(nodeId, node, (_, _) => node);

        // TODO: 更新 KV Store（需要适配 NATS API）
        
        _logger.LogTrace("Node {NodeId} heartbeat sent with load {Load}, TTL refreshed in memory", nodeId, load);
        return Task.CompletedTask;
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // 无锁删除本地缓存
        if (_nodes.TryRemove(nodeId, out var node))
        {
            // TODO: 删除 KV Store 中的节点 (需要适配 NATS API)
            
            _logger.LogDebug("Node {NodeId} unregistered from memory", nodeId);

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
        // 无锁读取（ConcurrentDictionary 保证线程安全）
        _nodes.TryGetValue(nodeId, out var node);
        return Task.FromResult(node);
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        // 无锁读取所有节点（ConcurrentDictionary.Values 是线程安全的）
        var nodes = _nodes.Values.ToList();

        // 过滤过期节点（基于 LastSeen）
        var activeNodes = nodes
            .Where(n => DateTime.UtcNow - n.LastSeen < _nodeTtl)
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

    /// <summary>
    /// 从 KV Store 加载现有节点（TODO: 需要适配 NATS API）
    /// </summary>
    private Task LoadExistingNodesAsync(CancellationToken cancellationToken)
    {
        // TODO: 实现 KV Store 加载逻辑
        _logger.LogDebug("LoadExistingNodesAsync - using in-memory mode, no persistence to load");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 监听 KV Store 变更（TODO: 需要适配 NATS API）
    /// </summary>
    private Task WatchNodesAsync(CancellationToken cancellationToken)
    {
        // TODO: 实现 KV Store Watch 逻辑
        _logger.LogDebug("WatchNodesAsync - using in-memory mode, no KV Watch available");
        return Task.CompletedTask;
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

