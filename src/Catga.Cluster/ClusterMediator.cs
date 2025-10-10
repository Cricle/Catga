using Catga;
using Catga.Cluster.Discovery;
using Catga.Cluster.Routing;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster;

/// <summary>
/// 集群 Mediator - 自动路由到正确的节点
/// </summary>
public sealed class ClusterMediator : ICatgaMediator
{
    private readonly INodeDiscovery _discovery;
    private readonly IMessageRouter _router;
    private readonly ICatgaMediator _localMediator;
    private readonly ILogger<ClusterMediator> _logger;
    private readonly string _localNodeId;

    public ClusterMediator(
        INodeDiscovery discovery,
        IMessageRouter router,
        ICatgaMediator localMediator,
        ILogger<ClusterMediator> logger,
        ClusterOptions options)
    {
        _discovery = discovery;
        _router = router;
        _localMediator = localMediator;
        _logger = logger;
        _localNodeId = options.NodeId;
    }

    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 获取所有在线节点
        var nodes = await _discovery.GetNodesAsync(cancellationToken);
        
        // 路由到目标节点
        var targetNode = await _router.RouteAsync(request, nodes, cancellationToken);

        // 如果是本地节点，直接执行
        if (targetNode.NodeId == _localNodeId)
        {
            _logger.LogDebug("Executing request locally: {RequestType}", typeof(TRequest).Name);
            return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
        }

        // 转发到远程节点
        _logger.LogDebug("Forwarding request to node {NodeId}: {RequestType}", 
            targetNode.NodeId, typeof(TRequest).Name);
        
        return await ForwardToNodeAsync<TRequest, TResponse>(targetNode, request, cancellationToken);
    }

    public Task<CatgaResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        // 简化：直接本地执行
        return _localMediator.SendAsync(request, cancellationToken);
    }

    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 简化：直接本地发布
        return _localMediator.PublishAsync(@event, cancellationToken);
    }

    public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 简化：直接本地执行
        return _localMediator.SendBatchAsync<TRequest, TResponse>(requests, cancellationToken);
    }

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 简化：直接本地执行
        return _localMediator.SendStreamAsync<TRequest, TResponse>(requests, cancellationToken);
    }

    public Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 简化：直接本地发布
        return _localMediator.PublishBatchAsync(events, cancellationToken);
    }

    // TODO: 实现远程转发
    private Task<CatgaResult<TResponse>> ForwardToNodeAsync<TRequest, TResponse>(
        ClusterNode targetNode,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // 暂时返回错误，后续实现 HTTP/gRPC 转发
        _logger.LogWarning("Remote forwarding not yet implemented");
        return Task.FromResult(CatgaResult<TResponse>.Failure("Remote forwarding not yet implemented"));
    }
}

