using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Catga.Distributed.Serialization;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Distributed.Nats;

/// <summary>NATS Pub/Sub node discovery (lock-free)</summary>
public sealed class NatsNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsNodeDiscovery> _logger;
    private readonly string _subjectPrefix;
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();
    private readonly Channel<NodeChangeEvent> _events;
    private readonly CancellationTokenSource _disposeCts;
    private readonly Task _backgroundTask;

    public NatsNodeDiscovery(INatsConnection connection, ILogger<NatsNodeDiscovery> logger, string subjectPrefix = "catga.nodes")
    {
        _connection = connection;
        _logger = logger;
        _subjectPrefix = subjectPrefix;
        _events = Channel.CreateUnbounded<NodeChangeEvent>();
        _disposeCts = new CancellationTokenSource();
        _backgroundTask = SubscribeToNodesAsync(_disposeCts.Token);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);
        await _connection.PublishAsync($"{_subjectPrefix}.join", JsonHelper.SerializeNode(node), cancellationToken: cancellationToken);
        _logger.LogInformation("Registered node {NodeId} at {Endpoint}", node.NodeId, node.Endpoint);
        await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Joined, Node = node }, cancellationToken);
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        _nodes.TryRemove(nodeId, out _);
        await _connection.PublishAsync($"{_subjectPrefix}.leave",
            JsonHelper.SerializeDictionary(new Dictionary<string, string> { ["NodeId"] = nodeId }),
            cancellationToken: cancellationToken);
        _logger.LogInformation("Unregistered node {NodeId}", nodeId);
    }

    public async Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        var updated = false;
        _nodes.AddOrUpdate(nodeId, key =>
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat, creating new entry", nodeId);
            return new NodeInfo { NodeId = nodeId, Endpoint = "unknown", LastSeen = DateTime.UtcNow, Load = load };
        }, (key, existing) =>
        {
            updated = true;
            return existing with { LastSeen = DateTime.UtcNow, Load = load };
        });

        if (!updated) return;

        await _connection.PublishAsync($"{_subjectPrefix}.heartbeat",
            JsonHelper.SerializeHeartbeat(nodeId, load, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        if (_nodes.TryGetValue(nodeId, out var node))
            await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Updated, Node = node }, cancellationToken);
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return Task.FromResult<IReadOnlyList<NodeInfo>>(
            _nodes.Values.Where(n => (now - n.LastSeen).TotalSeconds < 30).ToList());
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
            yield return @event;
    }

    private async Task SubscribeToNodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var joinSubject = $"{_subjectPrefix}.join";
            var leaveSubject = $"{_subjectPrefix}.leave";
            var heartbeatSubject = $"{_subjectPrefix}.heartbeat";
            var joinTask = SubscribeJoinAsync(joinSubject, cancellationToken);
            var leaveTask = SubscribeLeaveAsync(leaveSubject, cancellationToken);
            var heartbeatTask = SubscribeHeartbeatAsync(heartbeatSubject, cancellationToken);
            await Task.WhenAll(joinTask, leaveTask, heartbeatTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Error in NATS subscribe loop"); }
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
                _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);
                await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Joined, Node = node }, cancellationToken);
                _logger.LogDebug("Node {NodeId} joined", node.NodeId);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error processing node join message"); }
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
                if (_nodes.TryRemove(nodeId, out var node))
                {
                    await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Left, Node = node }, cancellationToken);
                    _logger.LogDebug("Node {NodeId} left", nodeId);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error processing node leave message"); }
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
                if (_nodes.TryGetValue(heartbeat.NodeId, out var existing))
                {
                    var updated = existing with { LastSeen = DateTime.UtcNow, Load = heartbeat.Load };
                    _nodes.TryUpdate(heartbeat.NodeId, updated, existing);
                    await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Updated, Node = updated }, cancellationToken);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error processing heartbeat message"); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _events.Writer.Complete();
        try
        {
            await _backgroundTask.ConfigureAwait(false);
            await _events.Reader.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Error during disposal"); }
        _disposeCts.Dispose();
    }
}

