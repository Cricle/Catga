using Catga.Abstractions;
using MemoryPack;
using OrderSystem.Models;

namespace OrderSystem.Commands;

[MemoryPackable]
public partial record CreateOrderCommand(string CustomerId, List<OrderItem> Items) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record PayOrderCommand(string OrderId, string PaymentMethod) : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record ShipOrderCommand(string OrderId, string TrackingNumber) : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record CancelOrderCommand(string OrderId) : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OrderCreatedResult(string OrderId, string CustomerId, decimal Total, DateTime CreatedAt);
