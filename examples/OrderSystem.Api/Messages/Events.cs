using Catga.Abstractions;
using MemoryPack;

namespace OrderSystem.Api.Messages;

/// <summary>Published when an order is created.</summary>
[MemoryPackable]
public partial record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal TotalAmount,
    DateTime CreatedAt
) : IEvent;

/// <summary>Published when an order is cancelled.</summary>
[MemoryPackable]
public partial record OrderCancelledEvent(
    string OrderId,
    string? Reason,
    DateTime CancelledAt
) : IEvent;

/// <summary>Published when an order is confirmed.</summary>
[MemoryPackable]
public partial record OrderConfirmedEvent(
    string OrderId,
    DateTime ConfirmedAt
) : IEvent;
