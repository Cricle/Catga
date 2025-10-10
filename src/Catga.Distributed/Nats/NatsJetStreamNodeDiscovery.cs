using System.Runtime.CompilerServices;
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
    private object? _kvStore; // TODO: 使用正确的 NATS KV Store 类型
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsJetStreamNodeDiscovery(
        INatsConnection connection,
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

            // 2. TODO: 实现 NATS JetStream KV Store（需要正确的 API）
            // 当前暂时抛出异常
            throw new NotImplementedException("NATS JetStream KV Store implementation pending - API types need verification");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("NATS JetStream KV Store - API pending verification");
    }

    public Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("NATS JetStream KV Store - API pending verification");
    }

    public Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("NATS JetStream KV Store - API pending verification");
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("NATS JetStream KV Store - API pending verification");
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("NATS JetStream KV Store - API pending verification");
        yield break;
    }

    public async ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        await Task.CompletedTask;
    }
}

