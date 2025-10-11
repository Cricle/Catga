namespace Catga;

/// <summary>
/// Message Quality of Service levels (like MQTT QoS)
/// </summary>
public enum QualityOfService
{
    /// <summary>
    /// QoS 0: At Most Once
    /// - Fire-and-forget, no delivery guarantee
    /// - Highest performance, no confirmation
    /// - Use cases: logs, metrics, non-critical notifications
    /// </summary>
    AtMostOnce = 0,

    /// <summary>
    /// QoS 1: At Least Once
    /// - Guaranteed delivery, may duplicate
    /// - Requires message confirmation
    /// - Use cases: important notifications, order creation, data sync
    /// </summary>
    AtLeastOnce = 1,

    /// <summary>
    /// QoS 2: Exactly Once
    /// - Guaranteed delivery without duplication
    /// - Requires idempotency check (based on MessageId)
    /// - Use cases: payment, inventory deduction, financial transactions
    /// </summary>
    ExactlyOnce = 2
}

