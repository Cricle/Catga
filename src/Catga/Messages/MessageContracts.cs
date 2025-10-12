using System.ComponentModel;

namespace Catga.Messages;

/// <summary>Base message interface (framework use only)</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMessage
{
    public string MessageId => Guid.NewGuid().ToString();
    public DateTime CreatedAt => DateTime.UtcNow;
    public string? CorrelationId => null;
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

