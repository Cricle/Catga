namespace Catga;

/// <summary>
/// 消息服务质量等级（类似 MQTT QoS）
/// </summary>
public enum QualityOfService
{
    /// <summary>
    /// QoS 0: 最多一次（At Most Once）
    /// - 发送即忘，不保证送达
    /// - 性能最高，无需确认
    /// - 适用场景：日志、监控指标、非关键通知
    /// </summary>
    AtMostOnce = 0,

    /// <summary>
    /// QoS 1: 至少一次（At Least Once）
    /// - 保证送达，但可能重复
    /// - 需要消息确认
    /// - 适用场景：重要通知、订单创建、数据同步
    /// </summary>
    AtLeastOnce = 1,

    /// <summary>
    /// QoS 2: 恰好一次（Exactly Once）
    /// - 保证送达且不重复
    /// - 需要幂等性检查（基于 MessageId）
    /// - 适用场景：支付、库存扣减、金融交易
    /// </summary>
    ExactlyOnce = 2
}

