using Catga.Debugger.Core;
using Catga.Messages;
using Catga.Results;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

/// <summary>
/// Create order command (with auto-generated debug capture via Source Generator)
/// </summary>
[MemoryPackable]
[GenerateDebugCapture] // Source Generator automatically implements IDebugCapture
public partial record CreateOrderCommand(
    string CustomerId,
    List<OrderItem> Items,
    string ShippingAddress,
    string PaymentMethod
) : IRequest<OrderCreatedResult>;

[MemoryPackable]
public partial record OrderCreatedResult(
    string OrderId,
    decimal TotalAmount,
    DateTime CreatedAt
);

/// <summary>
/// Confirm order command
/// </summary>
[MemoryPackable]
public partial record ConfirmOrderCommand(
    string OrderId
) : IRequest;

/// <summary>
/// Pay order command
/// </summary>
[MemoryPackable]
public partial record PayOrderCommand(
    string OrderId,
    string PaymentMethod,
    decimal Amount
) : IRequest;

/// <summary>
/// Ship order command
/// </summary>
[MemoryPackable]
public partial record ShipOrderCommand(
    string OrderId,
    string TrackingNumber,
    string Carrier
) : IRequest;

/// <summary>
/// Cancel order command
/// </summary>
[MemoryPackable]
public partial record CancelOrderCommand(
    string OrderId,
    string Reason
) : IRequest;

/// <summary>
/// Get order query
/// </summary>
[MemoryPackable]
public partial record GetOrderQuery(
    string OrderId
) : IRequest<Order?>;

/// <summary>
/// Get customer orders query
/// </summary>
[MemoryPackable]
public partial record GetCustomerOrdersQuery(
    string CustomerId,
    int PageIndex = 0,
    int PageSize = 20
) : IRequest<List<Order>>;

