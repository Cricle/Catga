using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Catga.Distributed;
using Catga.Distributed.Serialization;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Distributed.Nats;

/// <summary>
/// 基于 NATS Pub/Sub 的无锁节点发现
/// 完全无锁设计：使用 ConcurrentDictionary + Channel
/// </summary>
public sealed class NatsNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsNodeDiscovery> _logger;
    private readonly string _subjectPrefix;
    
    // 无锁数据结构：ConcurrentDictionary 保证线程安全
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();
    
    // 无锁事件流：Channel 保证无锁通信
    private readonly Channel<NodeChangeEvent> _events;
    
    private readonly CancellationTokenSource _disposeCts;
    private readonly Task _backgroundTask; // 追踪后台任务防止泄漏

    public NatsNodeDiscovery(
        INatsConnection connection,
        ILogger<NatsNodeDiscovery> logger,
        string subjectPrefix = "catga.nodes")
    {
        _connection = connection;
        _logger = logger;
        _subjectPrefix = subjectPrefix;
        _events = Channel.CreateUnbounded<NodeChangeEvent>();
        _disposeCts = new CancellationTokenSource();

        // 启动后台订阅（无锁）- 追踪任务
        _backgroundTask = SubscribeToNodesAsync(_disposeCts.Token);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        // 无锁更新本地缓存
        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

        // 发布节点加入事件（NATS 自动广播，无需锁）
        var subject = $"{_subjectPrefix}.join";
        var json = JsonHelper.SerializeNode(node);
        
        await _connection.PublishAsync(subject, json, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Registered node {NodeId} at {Endpoint}", node.NodeId, node.Endpoint);

        // 无锁发送事件
        await _events.Writer.WriteAsync(new NodeChangeEvent
        {
            Type = NodeChangeType.Joined,
            Node = node
        }, cancellationToken);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // 无锁移除节点
        _nodes.TryRemove(nodeId, out var node);

        // 发布节点离开事件
        var subject = $"{_subjectPrefix}.leave";
        var json = JsonHelper.SerializeDictionary(new Dictionary<string, string> { ["NodeId"] = nodeId });
        
        await _connection.PublishAsync(subject, json, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Unregistered node {NodeId}", nodeId);
    }

    public async Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        // 无锁更新节点信息
        var updated = false;
        _nodes.AddOrUpdate(
            nodeId,
            // 如果不存在，创建新节点（不应该发生）
            key =>
            {
                _logger.LogWarning("Node {NodeId} not found for heartbeat, creating new entry", nodeId);
                return new NodeInfo
                {
                    NodeId = nodeId,
                    Endpoint = "unknown",
                    LastSeen = DateTime.UtcNow,
                    Load = load
                };
            },
            // 如果存在，更新
            (key, existing) =>
            {
                updated = true;
                return existing with
                {
                    LastSeen = DateTime.UtcNow,
                    Load = load
                };
            });

        if (!updated) return;

        // 发布心跳事件
        var subject = $"{_subjectPrefix}.heartbeat";
        var json = JsonHelper.SerializeHeartbeat(nodeId, load, DateTime.UtcNow);
        
        await _connection.PublishAsync(subject, json, cancellationToken: cancellationToken);

        // 无锁发送更新事件
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            await _events.Writer.WriteAsync(new NodeChangeEvent
            {
                Type = NodeChangeType.Updated,
                Node = node
            }, cancellationToken);
        }
    }

    public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return Task.FromResult(node);
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        // 无锁读取：ConcurrentDictionary.Values 是线程安全的
        var now = DateTime.UtcNow;
        var onlineNodes = _nodes.Values
            .Where(n => (now - n.LastSeen).TotalSeconds < 30)  // 30秒超时
            .ToList();

        return Task.FromResult<IReadOnlyList<NodeInfo>>(onlineNodes);
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 无锁读取事件流
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return @event;
        }
    }

    /// <summary>
    /// 后台订阅节点事件（完全无锁）
    /// </summary>
    private async Task SubscribeToNodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 订阅所有节点事件（通配符）
            var joinSubject = $"{_subjectPrefix}.join";
            var leaveSubject = $"{_subjectPrefix}.leave";
            var heartbeatSubject = $"{_subjectPrefix}.heartbeat";

            // 并行订阅三个主题（无锁）
            var joinTask = SubscribeJoinAsync(joinSubject, cancellationToken);
            var leaveTask = SubscribeLeaveAsync(leaveSubject, cancellationToken);
            var heartbeatTask = SubscribeHeartbeatAsync(heartbeatSubject, cancellationToken);

            await Task.WhenAll(joinTask, leaveTask, heartbeatTask);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NATS subscribe loop");
        }
    }

    private async Task SubscribeJoinAsync(string subject, CancellationToken cancellationToken)
    {
        await foreach (var msg in _connection.SubscribeAsync<string>(subject, cancellationToken: cancellationToken))
        {
            try
            {
                if (string.IsNullOrEmpty(msg.Data)) continue;

                var node = JsonHelper.DeserializeNode(msg.Data);
                if (node == null) continue;

                // 无锁更新节点
                _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

                // 无锁发送事件
                await _events.Writer.WriteAsync(new NodeChangeEvent
                {
                    Type = NodeChangeType.Joined,
                    Node = node
                }, cancellationToken);

                _logger.LogDebug("Node {NodeId} joined", node.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing node join message");
            }
        }
    }

    private async Task SubscribeLeaveAsync(string subject, CancellationToken cancellationToken)
    {
        await foreach (var msg in _connection.SubscribeAsync<string>(subject, cancellationToken: cancellationToken))
        {
            try
            {
                if (string.IsNullOrEmpty(msg.Data)) continue;

                var data = JsonHelper.DeserializeDictionary(msg.Data);
                if (data == null || !data.TryGetValue("NodeId", out var nodeId)) continue;

                // 无锁移除节点
                if (_nodes.TryRemove(nodeId, out var node))
                {
                    // 无锁发送事件
                    await _events.Writer.WriteAsync(new NodeChangeEvent
                    {
                        Type = NodeChangeType.Left,
                        Node = node
                    }, cancellationToken);

                    _logger.LogDebug("Node {NodeId} left", nodeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing node leave message");
            }
        }
    }

    private async Task SubscribeHeartbeatAsync(string subject, CancellationToken cancellationToken)
    {
        await foreach (var msg in _connection.SubscribeAsync<string>(subject, cancellationToken: cancellationToken))
        {
            try
            {
                if (string.IsNullOrEmpty(msg.Data)) continue;

                var heartbeat = JsonHelper.DeserializeHeartbeat(msg.Data);
                if (heartbeat == null) continue;

                // 无锁更新节点
                if (_nodes.TryGetValue(heartbeat.NodeId, out var existing))
                {
                    var updated = existing with
                    {
                        LastSeen = DateTime.UtcNow,
                        Load = heartbeat.Load
                    };

                    _nodes.TryUpdate(heartbeat.NodeId, updated, existing);

                    // 无锁发送事件
                    await _events.Writer.WriteAsync(new NodeChangeEvent
                    {
                        Type = NodeChangeType.Updated,
                        Node = updated
                    }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing heartbeat message");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _events.Writer.Complete();
        
        try
        {
            // 等待后台任务完成，防止泄漏
            await _backgroundTask.ConfigureAwait(false);
            
            // 等待事件通道完成
            await _events.Reader.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 预期的取消异常
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during NatsNodeDiscovery disposal");
        }

        _disposeCts.Dispose();
    }

    public Task<NodeInfo?> GetNode(string nodeId, CancellationToken cancellationToken = default)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return Task.FromResult(node);
    }
}

