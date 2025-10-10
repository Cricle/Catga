using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Distributed;

/// <summary>
/// 分布式 Mediator 实现（完全无锁设计）
/// 使用 Round-Robin 负载均衡，无需任何锁
/// </summary>
public sealed class DistributedMediator : IDistributedMediator
{
    private readonly ICatgaMediator _localMediator;
    private readonly IMessageTransport _transport;
    private readonly INodeDiscovery _discovery;
    private readonly ILogger<DistributedMediator> _logger;
    private readonly NodeInfo _currentNode;
    
    // 无锁计数器：用于 Round-Robin
    private int _roundRobinCounter = 0;

    public DistributedMediator(
        ICatgaMediator localMediator,
        IMessageTransport transport,
        INodeDiscovery discovery,
        ILogger<DistributedMediator> logger,
        NodeInfo currentNode)
    {
        _localMediator = localMediator;
        _transport = transport;
        _discovery = discovery;
        _logger = logger;
        _currentNode = currentNode;
    }

    public Task<NodeInfo> GetCurrentNodeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_currentNode);
    }

    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        return _discovery.GetNodesAsync(cancellationToken);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 策略：优先本地处理，失败则路由到其他节点
        try
        {
            // 先尝试本地处理
            return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
        }
        catch
        {
            // 本地失败，路由到其他节点（Round-Robin，无锁）
            var nodes = await GetNodesAsync(cancellationToken);
            var remoteNodes = nodes.Where(n => n.NodeId != _currentNode.NodeId).ToList();

            if (remoteNodes.Count == 0)
            {
                // 没有其他节点，返回失败
                return CatgaResult<TResponse>.Failure("No available nodes for routing");
            }

            // 无锁 Round-Robin：使用 Interlocked.Increment
            var index = Interlocked.Increment(ref _roundRobinCounter) % remoteNodes.Count;
            var targetNode = remoteNodes[index];

            return await SendToNodeAsync<TRequest, TResponse>(request, targetNode.NodeId, cancellationToken);
        }
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        return await _localMediator.SendAsync(request, cancellationToken);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        return await _localMediator.SendBatchAsync<TRequest, TResponse>(requests, cancellationToken);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        return _localMediator.SendStreamAsync<TRequest, TResponse>(requests, cancellationToken);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        await _localMediator.PublishBatchAsync(events, cancellationToken);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async Task<CatgaResult<TResponse>> SendToNodeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TRequest, TResponse>(
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

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 本地发布
        await _localMediator.PublishAsync(@event, cancellationToken);

        // 广播到所有远程节点（无锁）
        await BroadcastAsync(@event, cancellationToken);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async Task BroadcastAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TEvent>(
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

