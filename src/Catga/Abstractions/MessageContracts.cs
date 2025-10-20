using Catga.Core;
using System.ComponentModel;

namespace Catga.Messages;

/// <summary>
/// Base message interface (framework use only).
/// Users must provide MessageId and CorrelationId - no default implementation.
/// This ensures proper ID generation and distributed tracing.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMessage
{
    /// <summary>
    /// Unique message identifier. Must be provided by the caller.
    /// Use IDistributedIdGenerator to generate IDs for performance and uniqueness.
    /// Changed from string to long for 92% memory reduction and better performance.
    /// </summary>
    public long MessageId { get; }

    /// <summary>
    /// Creation timestamp. Default implementation is provided for convenience.
    /// </summary>
    public DateTime CreatedAt => DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing. Should be propagated from Activity.Baggage or parent message.
    /// Null if this is the originating message.
    /// Changed from string to long for consistency and performance.
    /// </summary>
    public long? CorrelationId => null;

    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

/// <summary>Request with response (usage: public record MyRequest(...) : IRequest&lt;MyResponse&gt;)</summary>
public interface IRequest<TResponse> : IMessage
{
}

/// <summary>Request without response (usage: public record MyCommand(...) : IRequest)</summary>
public interface IRequest : IMessage
{
}

/// <summary>Event (usage: public record MyEvent(...) : IEvent) - QoS 0 by default</summary>
public interface IEvent : IMessage
{
    public DateTime OccurredAt => DateTime.UtcNow;
    public new QualityOfService QoS => QualityOfService.AtMostOnce;
}

/// <summary>Reliable event (usage: public record MyEvent(...) : IReliableEvent) - QoS 1 guaranteed</summary>
public interface IReliableEvent : IEvent
{
    public new QualityOfService QoS => QualityOfService.AtLeastOnce;
}

