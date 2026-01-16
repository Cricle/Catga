using Catga.Abstractions;
using MemoryPack;

namespace OrderSystem.Events;

[MemoryPackable]
public partial record OrderCreatedEvent(string OrderId, string CustomerId, decimal Total, DateTime CreatedAt) : IEvent
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OrderPaidEvent(string OrderId, string PaymentMethod, DateTime PaidAt) : IEvent
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OrderShippedEvent(string OrderId, string TrackingNumber, DateTime ShippedAt) : IEvent
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OrderCancelledEvent(string OrderId, DateTime CancelledAt) : IEvent
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OrderCompletedEvent(string OrderId, decimal Total) : IEvent
{
    public long MessageId { get; init; }
}
