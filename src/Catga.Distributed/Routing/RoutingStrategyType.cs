namespace Catga.Distributed.Routing;

/// <summary>
/// 路由策略类型
/// </summary>
public enum RoutingStrategyType
{
    /// <summary>
    /// 轮询（Round-Robin）- 按顺序轮流分配
    /// </summary>
    RoundRobin = 0,

    /// <summary>
    /// 一致性哈希（Consistent Hash）- 基于消息键的哈希路由
    /// </summary>
    ConsistentHash = 1,

    /// <summary>
    /// 基于负载（Load-Based）- 选择负载最低的节点
    /// </summary>
    LoadBased = 2,

    /// <summary>
    /// 随机（Random）- 随机选择节点
    /// </summary>
    Random = 3,

    /// <summary>
    /// 本地优先（Local-First）- 优先选择本地节点
    /// </summary>
    LocalFirst = 4
}

