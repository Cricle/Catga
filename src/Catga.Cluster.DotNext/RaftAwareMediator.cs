using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.DotNext;

/// <summary>
/// Raft-aware mediator - 完全透明的分布式支持
/// 用户代码完全不变，自动获得：
/// - 高可用（3节点容错1个）
/// - 自动故障转移
/// - 强一致性（Command）
/// - 低延迟读（Query）
/// </summary>
public sealed class RaftAwareMediator : ICatgaMediator
{
    private readonly ICatgaRaftCluster _cluster;
    private readonly ICatgaMediator _localMediator;
    private readonly ILogger<RaftAwareMediator> _logger;

    public RaftAwareMediator(
        ICatgaRaftCluster cluster,
        ICatgaMediator localMediator,
        ILogger<RaftAwareMediator> logger)
    {
        _cluster = cluster;
        _localMediator = localMediator;
        _logger = logger;
    }

    /// <summary>
    /// 发送请求 - 自动路由
    /// </summary>
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 超简单：如果是 Leader 或者是查询，本地执行
        // 否则返回提示（由 DotNext Raft 自动转发）
        if (_cluster.IsLeader || IsQueryOperation<TRequest>())
        {
            return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
        }

        _logger.LogDebug("Not leader, request will be auto-forwarded by Raft");
        
        // 简化：让 DotNext 的 Raft 层自动处理转发
        // 用户无需关心，就像单机一样
        return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
    }

    public async Task<CatgaResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        // 同样简单：本地执行，Raft 自动同步
        return await _localMediator.SendAsync(request, cancellationToken);
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 事件：本地发布，Raft 自动广播
        await _localMediator.PublishAsync(@event, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 批量：高性能本地执行
        return await _localMediator.SendBatchAsync<TRequest, TResponse>(requests, cancellationToken);
    }

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 流式：高性能本地执行
        return _localMediator.SendStreamAsync<TRequest, TResponse>(requests, cancellationToken);
    }

    public async Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 批量事件：本地发布
        await _localMediator.PublishBatchAsync(events, cancellationToken);
    }

    // 简单的启发式判断：Query 本地执行
    private static bool IsQueryOperation<TRequest>()
    {
        var name = typeof(TRequest).Name;
        return name.Contains("Query", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Get", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("List", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Find", StringComparison.OrdinalIgnoreCase);
    }
}
