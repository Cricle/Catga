using Catga.Cluster.Discovery;
using Catga.Cluster.Remote;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.Resilience;

/// <summary>
/// 带重试和故障转移的远程调用器
/// </summary>
public sealed class RetryRemoteInvoker : IRemoteInvoker
{
    private readonly IRemoteInvoker _innerInvoker;
    private readonly INodeDiscovery _discovery;
    private readonly ILogger<RetryRemoteInvoker> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;

    public RetryRemoteInvoker(
        IRemoteInvoker innerInvoker,
        INodeDiscovery discovery,
        ILogger<RetryRemoteInvoker> logger,
        int maxRetries = 2,
        TimeSpan? retryDelay = null)
    {
        _innerInvoker = innerInvoker;
        _discovery = discovery;
        _logger = logger;
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task<CatgaResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ClusterNode targetNode,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var currentNode = targetNode;

        while (attempt <= _maxRetries)
        {
            try
            {
                _logger.LogDebug("Invoking remote node {NodeId}, attempt {Attempt}/{MaxAttempts}",
                    currentNode.NodeId, attempt + 1, _maxRetries + 1);

                var result = await _innerInvoker.InvokeAsync<TRequest, TResponse>(
                    currentNode, request, cancellationToken);

                if (result.IsSuccess)
                {
                    if (attempt > 0)
                    {
                        _logger.LogInformation("Request succeeded after {Attempts} retries to node {NodeId}",
                            attempt, currentNode.NodeId);
                    }
                    return result;
                }

                // 业务错误，不重试
                _logger.LogWarning("Request failed with business error: {Error}", result.Error);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Request to node {NodeId} failed, attempt {Attempt}/{MaxAttempts}",
                    currentNode.NodeId, attempt + 1, _maxRetries + 1);

                attempt++;

                if (attempt > _maxRetries)
                {
                    _logger.LogError("Request failed after {MaxAttempts} attempts", _maxRetries + 1);
                    return CatgaResult<TResponse>.Failure(
                        $"Request failed after {_maxRetries + 1} attempts: {ex.Message}");
                }

                // 尝试故障转移到其他节点
                var alternativeNode = await TryGetAlternativeNodeAsync(currentNode, cancellationToken);
                if (alternativeNode != null)
                {
                    _logger.LogInformation("Failing over from {OldNode} to {NewNode}",
                        currentNode.NodeId, alternativeNode.NodeId);
                    currentNode = alternativeNode;
                }
                else
                {
                    _logger.LogWarning("No alternative node available for failover");
                }

                // 延迟后重试
                if (attempt <= _maxRetries)
                {
                    await Task.Delay(_retryDelay, cancellationToken);
                }
            }
        }

        return CatgaResult<TResponse>.Failure("Request failed after all retry attempts");
    }

    /// <summary>
    /// 尝试获取备用节点（排除故障节点）
    /// </summary>
    private async Task<ClusterNode?> TryGetAlternativeNodeAsync(
        ClusterNode failedNode,
        CancellationToken cancellationToken)
    {
        try
        {
            var nodes = await _discovery.GetNodesAsync(cancellationToken);
            
            // 选择第一个不是当前故障节点的在线节点
            return nodes.FirstOrDefault(n => n.NodeId != failedNode.NodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get alternative node");
            return null;
        }
    }
}

