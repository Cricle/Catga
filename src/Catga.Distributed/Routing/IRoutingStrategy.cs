using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Distributed.Routing;

/// <summary>
/// 路由策略接口，用于在多个节点之间选择目标节点
/// </summary>
public interface IRoutingStrategy
{
    /// <summary>
    /// 选择目标节点
    /// </summary>
    /// <param name="nodes">可用节点列表</param>
    /// <param name="message">待路由的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>选中的节点，如果没有合适的节点则返回 null</returns>
    Task<NodeInfo?> SelectNodeAsync(
        IReadOnlyList<NodeInfo> nodes,
        object message,
        CancellationToken cancellationToken = default);
}

