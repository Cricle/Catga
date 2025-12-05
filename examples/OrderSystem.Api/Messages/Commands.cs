using Catga.Abstractions;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId,
    List<OrderItem> Items
) : IRequest<OrderCreatedResult>;

[MemoryPackable]
public partial record OrderCreatedResult(
    string OrderId,
    decimal TotalAmount,
    DateTime CreatedAt
);

[MemoryPackable]
public partial record CancelOrderCommand(
    string OrderId,
    string? Reason = null
) : IRequest;

[MemoryPackable]
public partial record GetOrderQuery(string OrderId) : IRequest<Order?>;

[MemoryPackable]
public partial record GetUserOrdersQuery(string CustomerId) : IRequest<List<Order>>;

