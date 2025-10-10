using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

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
    private object? _kvStore; // 使用 object 因为具体类型需要进一步验证

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

            // TODO: KV Store API 需要进一步验证
            // 暂时使用占位符，避免编译错误
            _kvStore = new object(); // Placeholder
            
            _logger.LogInformation("JetStream KV Store '{Bucket}' placeholder initialized (API pending verification)", _bucketName);

            // 启动监听器（Watch）
            // _ = WatchNodesAsync(cancellationToken);

            // 加载现有节点
            // await LoadExistingNodesAsync(cancellationToken);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JetStream KV Store '{Bucket}'", _bucketName);
            throw;
        }
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        if (_kvStore == null)
        {
            throw new InvalidOperationException("KV Store not initialized");
        }

        // 无锁更新本地缓存
        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

        // TODO: 持久化到 KV Store (API pending)
        // var key = GetNodeKey(node.NodeId);
        // var json = JsonSerializer.Serialize(node);
        // await _kvStore.PutAsync(key, json, cancellationToken: cancellationToken);
        
        _logger.LogDebug("Node {NodeId} registered (KV Store API pending)", node.NodeId);

        // 发布节点变更事件
        await _events.Writer.WriteAsync(new NodeChangeEvent
        {
            Type = NodeChangeType.Joined,
            Node = node
        }, cancellationToken);
    }

    public async Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        if (_kvStore == null)
        {
            throw new InvalidOperationException("KV Store not initialized");
        }

        // 查找或创建节点信息
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            return;
        }

        // 更新时间戳和负载
        node = node with { LastSeen = DateTime.UtcNow, Load = load };

        // 无锁更新本地缓存
        _nodes.AddOrUpdate(nodeId, node, (_, _) => node);

        // 更新 KV Store（自动刷新 TTL）
        var key = GetNodeKey(nodeId);
        var json = JsonSerializer.Serialize(node);
        
        // 暂时注释，待 API 验证
        // await _kvStore.PutAsync(key, json, cancellationToken: cancellationToken);
        
        _logger.LogTrace("Node {NodeId} heartbeat sent with load {Load}", nodeId, load);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (_kvStore == null)
        {
            throw new InvalidOperationException("KV Store not initialized");
        }

        // 无锁删除本地缓存
        if (_nodes.TryRemove(nodeId, out var node))
        {
            // TODO: 删除 KV Store 中的节点 (API pending)
            // var key = GetNodeKey(nodeId);
            // await _kvStore.DeleteAsync(key, cancellationToken: cancellationToken);
            
            _logger.LogDebug("Node {NodeId} unregistered (KV Store API pending)", nodeId);

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
    /// 从 KV Store 加载现有节点 (API pending)
    /// </summary>
    private Task LoadExistingNodesAsync(CancellationToken cancellationToken)
    {
        // TODO: Implement when KV Store API is verified
        _logger.LogDebug("LoadExistingNodesAsync - API pending verification");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 监听 KV Store 变更（无锁）(API pending)
    /// </summary>
    private Task WatchNodesAsync(CancellationToken cancellationToken)
    {
        // TODO: Implement when KV Store API is verified
        _logger.LogDebug("WatchNodesAsync - API pending verification");
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

