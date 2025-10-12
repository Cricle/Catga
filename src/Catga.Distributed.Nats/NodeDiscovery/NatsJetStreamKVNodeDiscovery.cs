using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Catga.Distributed.Nats;

/// <summary>NATS JetStream KV Store node discovery with TTL cleanup</summary>
public sealed class NatsJetStreamKVNodeDiscovery : INodeDiscovery, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsJetStreamKVNodeDiscovery> _logger;
    private readonly string _bucketName;
    private readonly TimeSpan _nodeTtl;
    private readonly ConcurrentDictionary<string, NodeInfo> _localCache = new();
    private readonly Channel<NodeChangeEvent> _events;
    private readonly CancellationTokenSource _disposeCts;
    private readonly Task _initializationTask;
    private INatsJSContext? _jsContext;
    private Task? _watchTask;

    public NatsJetStreamKVNodeDiscovery(INatsConnection connection, ILogger<NatsJetStreamKVNodeDiscovery> logger, string bucketName = "catga_nodes", TimeSpan? nodeTtl = null)
    {
        _connection = connection;
        _logger = logger;
        _bucketName = bucketName;
        _nodeTtl = nodeTtl ?? TimeSpan.FromMinutes(5);
        _events = Channel.CreateUnbounded<NodeChangeEvent>();
        _disposeCts = new CancellationTokenSource();
        _initializationTask = InitializeAsync(_disposeCts.Token);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _jsContext = new NatsJSContext(_connection);
            _logger.LogWarning("JetStream KV Store '{Bucket}' using in-memory mode with TTL {Ttl}", _bucketName, _nodeTtl);
            _watchTask = StartTtlCleanupAsync(cancellationToken);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JetStream context '{Bucket}'", _bucketName);
            throw;
        }
    }

    private async Task StartTtlCleanupAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                var now = DateTime.UtcNow;
                var expiredNodes = _localCache.Where(kvp => now - kvp.Value.LastSeen > _nodeTtl).Select(kvp => kvp.Key).ToList();
                foreach (var nodeId in expiredNodes)
                {
                    if (_localCache.TryRemove(nodeId, out var node))
                    {
                        await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Left, Node = node }, cancellationToken);
                        _logger.LogDebug("Node {NodeId} expired (TTL cleanup)", nodeId);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error in TTL cleanup"); }
        }
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        _localCache.AddOrUpdate(node.NodeId, node, (_, _) => node);
        _logger.LogInformation("Node {NodeId} registered (in-memory, TTL: {Ttl}) at {Endpoint}", node.NodeId, _nodeTtl, node.Endpoint);
        await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Joined, Node = node }, cancellationToken);
    }

    public Task HeartbeatAsync(string nodeId, double load = 0, CancellationToken cancellationToken = default)
    {
        if (!_localCache.TryGetValue(nodeId, out var node))
        {
            _logger.LogWarning("Node {NodeId} not found for heartbeat", nodeId);
            return Task.CompletedTask;
        }
        node = node with { LastSeen = DateTime.UtcNow, Load = load };
        _localCache.AddOrUpdate(nodeId, node, (_, _) => node);
        _logger.LogTrace("Node {NodeId} heartbeat sent with load {Load}", nodeId, load);
        return Task.CompletedTask;
    }

    public async Task UnregisterAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (_localCache.TryRemove(nodeId, out var node))
        {
            _logger.LogInformation("Node {NodeId} unregistered from memory", nodeId);
            await _events.Writer.WriteAsync(new NodeChangeEvent { Type = NodeChangeType.Left, Node = node }, cancellationToken);
        }
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var activeNodes = _localCache.Values.Where(n => DateTime.UtcNow - n.LastSeen < _nodeTtl * 2).ToList();
        return Task.FromResult<IReadOnlyList<NodeInfo>>(activeNodes);
    }

    public async IAsyncEnumerable<NodeChangeEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var @event in _events.Reader.ReadAllAsync(cancellationToken))
            yield return @event;
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _events.Writer.Complete();
        try
        {
            await _initializationTask.ConfigureAwait(false);
            if (_watchTask != null)
                await _watchTask.ConfigureAwait(false);
            await _events.Reader.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Error during disposal"); }
        _disposeCts.Dispose();
    }
}

