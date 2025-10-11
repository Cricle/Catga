namespace Catga;

/// <summary>
/// 消息投递模式
/// 控制"至少一次"的具体行为
/// </summary>
public enum DeliveryMode
{
    /// <summary>
    /// 等待结果（Wait for Result）
    /// - 同步等待消息处理完成
    /// - 等待 ACK 确认
    /// - 阻塞调用直到成功或失败
    /// - 适用场景：需要立即知道结果的操作（支付、订单确认）
    /// </summary>
    WaitForResult = 0,

    /// <summary>
    /// 异步重试（Async Retry）
    /// - 不等待结果，立即返回
    /// - 后台重试机制保证至少一次送达
    /// - 使用持久化队列（Outbox、JetStream、Redis Streams）
    /// - 适用场景：高吞吐场景，不需要立即反馈（通知、日志、数据同步）
    /// </summary>
    AsyncRetry = 1
}

