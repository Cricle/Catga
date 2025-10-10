using Catga.Results;

namespace Catga.Cluster.Remote;

/// <summary>
/// 远程调用接口
/// </summary>
public interface IRemoteInvoker
{
    /// <summary>
    /// 调用远程节点
    /// </summary>
    Task<CatgaResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ClusterNode targetNode,
        TRequest request,
        CancellationToken cancellationToken = default);
}

