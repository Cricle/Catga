using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Distributed.Nats;

/// <summary>
/// 基于 NATS JetStream KV Store 的节点发现（原生功能，完全持久化）
/// 移除内存 ConcurrentDictionary，直接使用 NATS 分布式 KV Store
/// </summary>
public sealed class NatsJetStreamNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsJetStreamNodeDiscovery> _logger;
    private readonly string _bucketName;
    private readonly TimeSpan _nodeTtl;
    
    private INatsJSContext? _jetStreamContext;
    private INatsKVStore? _kvStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsJetStreamNodeDiscovery(
        INatsConnection _connection,
        ILogger<NatsJetStreamNodeDiscovery> logger,
        string bucketName = "catga-nodes",
        TimeSpan? nodeTtl = null)
    {
        _connection = connection;
        _logger = logger;
        _bucketName = bucketName;
        _nodeTtl = nodeTtl ?? TimeSpan.FromMinutes(2); // 节点 TTL 默认 2 分钟
    }

    /// <summary>
    /// 初始化 JetStream KV Store（懒加载）
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_kvStore != null) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_kvStore != null) return;

            // 1. 创建 JetStream 上下文
            _jetStreamContext = new NatsJSContext(_connection);

            // 2. 尝试获取或创建 KV Store
            try
            {
                _kvStore = await _jetStreamContext.GetKeyValueAsync(_bucketName, cancellationToken: cancellationToken);
                _logger.LogInformation("Connected to existing KV bucket: {Bucket}", _bucketName);
            }
            catch
            {
                // KV Store 不存在，创建新的
                var config = new KvConfig
                {
                    Bucket = _bucketName,
                    History = 5,                           // 保留 5 个历史版本
                    Ttl = _nodeTtl,                        // 节点 TTL（自动过期）
                    Storage = StreamConfigStorage.File,     // 持久化到文件
                    Replicas = 1,                           // 副本数（单节点）
                    MaxValueSize = 1024 * 64                // 最大值 64KB
                };

                _kvStore = await _jetStreamContext.CreateKeyValueAsync(config, cancellationToken: cancellationToken);
                _logger.LogInformation("Created new KV bucket: {Bucket} with TTL {Ttl}", _bucketName, _nodeTtl);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // 序列化节点信息
        var json = JsonSerializer.Serialize(node);

        // 直接使用 NATS KV Store（原生持久化，无需内存缓存）
        await _kvStore!.PutAsync(node.NodeId, json, cancellationToken: cancellationToken);

        _logger.LogInformation("Registered node {NodeId} at {Endpoint} to NATS KV Store", 
            node.NodeId, node.Endpoint);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // 从 NATS KV Store 删除节点（原生操作）
        await _kvStore!.DeleteAsync(nodeId, cancellationToken: cancellationToken);

        _logger.LogInformation("Unregistered node {NodeId} from NATS KV Store", nodeId);
    }

    public async Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            // 1. 从 NATS KV Store 读取节点信息
            var entry = await _kvStore!.GetEntryAsync<string>(nodeId, cancellationToken: cancellationToken);

            if (entry?.Value != null)
            {
                // 2. 反序列化
                var existingNode = JsonSerializer.Deserialize<NodeInfo>(entry.Value);

                if (existingNode != null)
                {
                    // 3. 更新节点信息
                    var updatedNode = existingNode with
                    {
                        LastSeen = DateTime.UtcNow,
                        Load = load
                    };

                    // 4. 写回 NATS KV Store（原生更新，自动续期 TTL）
                    var updatedJson = JsonSerializer.Serialize(updatedNode);
                    await _kvStore.PutAsync(nodeId, updatedJson, cancellationToken: cancellationToken);

                    _logger.LogDebug("Heartbeat for node {NodeId}, load: {Load}", nodeId, load);
                }
            }
            else
            {
                _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat for node {NodeId}", nodeId);
        }
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var nodes = new List<NodeInfo>();
        var now = DateTime.UtcNow;

        try
        {
            // 从 NATS KV Store 读取所有节点（原生操作，无需内存缓存）
            await foreach (var key in _kvStore!.GetKeysAsync(cancellationToken: cancellationToken))
            {
                try
                {
                    // 读取节点数据
                    var entry = await _kvStore.GetEntryAsync<string>(key, cancellationToken: cancellationToken);

                    if (entry?.Value != null)
                    {
                        var node = JsonSerializer.Deserialize<NodeInfo>(entry.Value);

                        // 过滤在线节点（TTL 内）
                        if (node != null && (now - node.LastSeen).TotalSeconds < _nodeTtl.TotalSeconds)
                        {
                            nodes.Add(node);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get node {Key}", key);
                }
            }

            _logger.LogDebug("Retrieved {Count} online nodes from NATS KV Store", nodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get nodes from NATS KV Store");
        }

        return nodes;
    }

    public async ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        await Task.CompletedTask;
    }
}

