using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Distributed.Routing;
using Catga.Messages;
using Catga.Results;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Distributed;

/// <summary>Distributed Mediator with lock-free design and configurable routing</summary>
public sealed class DistributedMediator : IDistributedMediator
{
    private readonly ICatgaMediator _localMediator;
    private readonly IMessageTransport _transport;
    private readonly INodeDiscovery _discovery;
    private readonly ILogger<DistributedMediator> _logger;
    private readonly NodeInfo _currentNode;
    private readonly IRoutingStrategy _routingStrategy;

    public DistributedMediator(ICatgaMediator localMediator, IMessageTransport transport, INodeDiscovery discovery, ILogger<DistributedMediator> logger, NodeInfo currentNode, IRoutingStrategy? routingStrategy = null)
    {
        _localMediator = localMediator;
        _transport = transport;
        _discovery = discovery;
        _logger = logger;
        _currentNode = currentNode;
        _routingStrategy = routingStrategy ?? new RoundRobinRoutingStrategy();
    }

    public Task<NodeInfo> GetCurrentNodeAsync(CancellationToken cancellationToken = default) => Task.FromResult(_currentNode);
    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default) => _discovery.GetNodesAsync(cancellationToken);

    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        try
        {
            return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
        }
        catch
        {
            var nodes = await GetNodesAsync(cancellationToken);
            var remoteNodes = nodes.Where(n => n.NodeId != _currentNode.NodeId).ToList();
            if (remoteNodes.Count == 0)
                return CatgaResult<TResponse>.Failure("No available nodes for routing");
            var targetNode = await _routingStrategy.SelectNodeAsync(remoteNodes, request, cancellationToken);
            if (targetNode == null)
                return CatgaResult<TResponse>.Failure("No suitable node found by routing strategy");
            return await SendToNodeAsync<TRequest, TResponse>(request, targetNode.NodeId, cancellationToken);
        }
    }

    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        => await _localMediator.SendAsync(request, cancellationToken);

    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => await _localMediator.SendBatchAsync<TRequest, TResponse>(requests, cancellationToken);

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => _localMediator.SendStreamAsync<TRequest, TResponse>(requests, cancellationToken);

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent
        => await _localMediator.PublishBatchAsync(events, cancellationToken);

    public async Task<CatgaResult<TResponse>> SendToNodeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, string nodeId, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        var nodes = await GetNodesAsync(cancellationToken);
        var targetNode = nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (targetNode == null)
            return CatgaResult<TResponse>.Failure($"Node {nodeId} not found");
        var destination = $"{targetNode.Endpoint}/catga/messages/{TypeNameCache<TRequest>.Name}";
        try
        {
            await _transport.SendAsync((object)request!, destination, cancellationToken: cancellationToken);
            return CatgaResult<TResponse>.Success(default(TResponse)!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to node {NodeId}", nodeId);
            return CatgaResult<TResponse>.Failure($"Failed to send to node {nodeId}: {ex.Message}");
        }
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        await _localMediator.PublishAsync(@event, cancellationToken);
        await BroadcastAsync(@event, cancellationToken);
    }

    public async Task BroadcastAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        var nodes = await GetNodesAsync(cancellationToken);
        var remoteNodes = nodes.Where(n => n.NodeId != _currentNode.NodeId).ToList();
        if (remoteNodes.Count == 0)
        {
            _logger.LogDebug("No remote nodes for broadcast");
            return;
        }
        var tasks = remoteNodes.Select(async node =>
        {
            try
            {
                var destination = $"{node.Endpoint}/catga/events/{TypeNameCache<TEvent>.Name}";
                await _transport.SendAsync((object)@event!, destination, cancellationToken: cancellationToken);
                _logger.LogDebug("Broadcasted event to node {NodeId}", node.NodeId);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to broadcast to node {NodeId}", node.NodeId); }
        });
        await Task.WhenAll(tasks);
    }
}
