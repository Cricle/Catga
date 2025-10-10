namespace Catga.Cluster.Routing;

/// <summary>
/// 消息路由接口
/// </summary>
public interface IMessageRouter
{
    /// <summary>
    /// 路由消息到目标节点
    /// </summary>
    Task<ClusterNode> RouteAsync<TMessage>(
        TMessage message,
        IReadOnlyList<ClusterNode> nodes,
        CancellationToken cancellationToken = default);
}

