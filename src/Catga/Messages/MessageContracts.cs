using System.ComponentModel;

namespace Catga.Messages;

/// <summary>
/// Marker interface for all messages (framework use only - users don't need to implement this directly)
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMessage
{
    /// <summary>
    /// Unique message identifier (auto-generated)
    /// </summary>
    public string MessageId => Guid.NewGuid().ToString();

    /// <summary>
    /// Message creation timestamp (auto-generated)
    /// </summary>
    public DateTime CreatedAt => DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for tracking related messages
    /// </summary>
    public string? CorrelationId => null;

    /// <summary>
    /// 消息服务质量等级（QoS）
    /// - QoS 0 (AtMostOnce): 最快，不保证送达
    /// - QoS 1 (AtLeastOnce): 默认，保证送达但可能重复
    /// - QoS 2 (ExactlyOnce): 最慢，保证送达且不重复
    /// </summary>
    public QualityOfService QoS => QualityOfService.AtLeastOnce;

    /// <summary>
    /// 投递模式（仅对 QoS 1/2 有效）
    /// - WaitForResult (默认): 等待结果，同步确认
    /// - AsyncRetry: 不等结果，异步重试保证送达
    /// </summary>
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

/// <summary>
/// Request message that expects a response
/// Simple usage: public record MyRequest(...) : IRequest&lt;MyResponse&gt;;
/// </summary>
public interface IRequest<TResponse> : IMessage
{
}

/// <summary>
/// Request message without response
/// Simple usage: public record MyCommand(...) : IRequest;
/// </summary>
public interface IRequest : IMessage
{
}

/// <summary>
/// Event message - something that has happened
/// Simple usage: public record MyEvent(...) : IEvent;
/// 
/// Note: Events 默认使用 QoS 0 (Fire-and-Forget)，不保证送达
/// 如需保证送达，使用 IReliableEvent
/// </summary>
public interface IEvent : IMessage
{
    /// <summary>
    /// Event occurred timestamp (auto-generated)
    /// </summary>
    public DateTime OccurredAt => DateTime.UtcNow;

    /// <summary>
    /// Events 默认 QoS 0 (Fire-and-Forget)
    /// </summary>
    public new QualityOfService QoS => QualityOfService.AtMostOnce;
}

/// <summary>
/// Reliable event - guarantees at-least-once delivery
/// Simple usage: public record MyEvent(...) : IReliableEvent;
/// 
/// 可靠事件保证至少一次送达，但可能重复，需要幂等性处理
/// </summary>
public interface IReliableEvent : IEvent
{
    /// <summary>
    /// 可靠事件使用 QoS 1 (At-Least-Once)
    /// </summary>
    public new QualityOfService QoS => QualityOfService.AtLeastOnce;
}

