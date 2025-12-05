using Catga.Abstractions;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

/// <summary>Simple create order command.</summary>
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId,
    List<OrderItem> Items
) : IRequest<OrderCreatedResult>;

/// <summary>Create order using Flow pattern with automatic compensation.</summary>
[MemoryPackable]
public partial record CreateOrderFlowCommand(
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

