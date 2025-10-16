using Catga.Messages;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

/// <summary>
/// Order created event
/// </summary>
[MemoryPackable]
public partial record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    List<OrderItem> Items,
    decimal TotalAmount,
    DateTime CreatedAt
) : IEvent;

/// <summary>
/// Order confirmed event
/// </summary>
[MemoryPackable]
public partial record OrderConfirmedEvent(
    string OrderId,
    DateTime ConfirmedAt
) : IEvent;

/// <summary>
/// Order paid event
/// </summary>
[MemoryPackable]
public partial record OrderPaidEvent(
    string OrderId,
    string PaymentMethod,
    decimal Amount,
    DateTime PaidAt
) : IEvent;

/// <summary>
/// Order shipped event
/// </summary>
[MemoryPackable]
public partial record OrderShippedEvent(
    string OrderId,
    string TrackingNumber,
    string Carrier,
    DateTime ShippedAt
) : IEvent;

/// <summary>
/// Order cancelled event
/// </summary>
[MemoryPackable]
public partial record OrderCancelledEvent(
    string OrderId,
    string Reason,
    DateTime CancelledAt
) : IEvent;

/// <summary>
/// Inventory reserved event
/// </summary>
[MemoryPackable]
public partial record InventoryReservedEvent(
    string OrderId,
    List<OrderItem> Items,
    DateTime ReservedAt
) : IEvent;

/// <summary>
/// Inventory released event
/// </summary>
[MemoryPackable]
public partial record InventoryReleasedEvent(
    string OrderId,
    List<OrderItem> Items,
    DateTime ReleasedAt
) : IEvent;

/// <summary>
/// Order failed event (for rollback scenarios)
/// </summary>
[MemoryPackable]
public partial record OrderFailedEvent(
    string OrderId,
    string CustomerId,
    string Reason,
    DateTime FailedAt
) : IEvent;

