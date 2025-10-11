using System.Diagnostics.CodeAnalysis;
using Catga.Distributed.Routing;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Distributed;

/// <summary>
/// 分布式 Mediator 实现（完全无锁设计）
/// 支持可配置的路由策略（Round-Robin, Consistent Hash, Load-Based 等）
/// </summary>
public sealed class DistributedMediator : IDistributedMediator
{
    private readonly ICatgaMediator _localMediator;
    private readonly IMessageTransport _transport;
    private readonly INodeDiscovery _discovery;
    private readonly ILogger<DistributedMediator> _logger;
    private readonly NodeInfo _currentNode;
    private readonly IRoutingStrategy _routingStrategy;

    public DistributedMediator(
        ICatgaMediator localMediator,
        IMessageTransport transport,
        INodeDiscovery discovery,
        ILogger<DistributedMediator> logger,
        NodeInfo currentNode,
        IRoutingStrategy? routingStrategy = null)
    {
        _localMediator = localMediator;
        _transport = transport;
        _discovery = discovery;
        _logger = logger;
        _currentNode = currentNode;
        _routingStrategy = routingStrategy ?? new RoundRobinRoutingStrategy(); // 默认 Round-Robin
    }

    public Task<NodeInfo> GetCurrentNodeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_currentNode);
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        return _discovery.GetNodesAsync(cancellationToken);
    }
    public async ValueTask<CatgaResult<TResponse>> SendAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 策略：优先本地处理，失败则使用路由策略路由到其他节点
        try
        {
            // 先尝试本地处理
            return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
        }
        catch
        {
            // 本地失败，使用路由策略选择目标节点（无锁）
            var nodes = await GetNodesAsync(cancellationToken);
            var remoteNodes = nodes.Where(n => n.NodeId != _currentNode.NodeId).ToList();

            if (remoteNodes.Count == 0)
            {
                // 没有其他节点，返回失败
                return CatgaResult<TResponse>.Failure("No available nodes for routing");
            }

            // 使用可配置的路由策略（无锁）
            var targetNode = await _routingStrategy.SelectNodeAsync(remoteNodes, request, cancellationToken);

            if (targetNode == null)
            {
                return CatgaResult<TResponse>.Failure("No suitable node found by routing strategy");
            }

            return await SendToNodeAsync<TRequest, TResponse>(request, targetNode.NodeId, cancellationToken);
        }
    }
    public async Task<CatgaResult> SendAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        return await _localMediator.SendAsync(request, cancellationToken);
    }
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        return await _localMediator.SendBatchAsync<TRequest, TResponse>(requests, cancellationToken);
    }
    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        IAsyncEnumerable<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        return _localMediator.SendStreamAsync<TRequest, TResponse>(requests, cancellationToken);
    }
    public async Task PublishBatchAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await _localMediator.PublishBatchAsync(events, cancellationToken);
    }
    public async Task<CatgaResult<TResponse>> SendToNodeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        TRequest request,
        string nodeId,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 发送到指定节点
        var nodes = await GetNodesAsync(cancellationToken);
        var targetNode = nodes.FirstOrDefault(n => n.NodeId == nodeId);

        if (targetNode == null)
        {
            return CatgaResult<TResponse>.Failure($"Node {nodeId} not found");
        }

        // 使用传输层发送到目标节点
        var destination = $"{targetNode.Endpoint}/catga/messages/{typeof(TRequest).Name}";

        try
        {
            // 转换为 object 以满足 IMessageTransport.SendAsync<TMessage> 的 class 约束
            await _transport.SendAsync((object)request!, destination, cancellationToken: cancellationToken);

            // TODO: 实现 Request/Response 模式
            // 当前简化版本：假设成功
            return CatgaResult<TResponse>.Success(default(TResponse)!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to node {NodeId}", nodeId);
            return CatgaResult<TResponse>.Failure($"Failed to send to node {nodeId}: {ex.Message}");
        }
    }
    public async Task PublishAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 本地发布
        await _localMediator.PublishAsync(@event, cancellationToken);

        // 广播到所有远程节点（无锁）
        await BroadcastAsync(@event, cancellationToken);
    }
    public async Task BroadcastAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 广播到所有远程节点（并行，无锁）
        var nodes = await GetNodesAsync(cancellationToken);
        var remoteNodes = nodes.Where(n => n.NodeId != _currentNode.NodeId).ToList();

        if (remoteNodes.Count == 0)
        {
            _logger.LogDebug("No remote nodes for broadcast");
            return;
        }

        // 并行广播（无锁）
        var tasks = remoteNodes.Select(async node =>
        {
            try
            {
                var destination = $"{node.Endpoint}/catga/events/{typeof(TEvent).Name}";
                // 转换为 object 以满足 IMessageTransport.SendAsync<TMessage> 的 class 约束
                await _transport.SendAsync((object)@event!, destination, cancellationToken: cancellationToken);
                _logger.LogDebug("Broadcasted event to node {NodeId}", node.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast to node {NodeId}", node.NodeId);
            }
        });

        await Task.WhenAll(tasks);
    }
}

