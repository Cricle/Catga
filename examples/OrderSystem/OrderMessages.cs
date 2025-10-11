using Catga.Messages;

namespace OrderSystem;

// ==================== Commands ====================

/// <summary>
/// Create new order command
/// </summary>
public record CreateOrderCommand : IRequest<CreateOrderResult>
{
    public string CustomerName { get; init; } = string.Empty;
    public List<OrderItemDto> Items { get; init; } = new();
}

/// <summary>
/// Process order command
/// </summary>
public record ProcessOrderCommand : IRequest<bool>
{
    public long OrderId { get; init; }
}

/// <summary>
/// Complete order command
/// </summary>
public record CompleteOrderCommand : IRequest<bool>
{
    public long OrderId { get; init; }
}

/// <summary>
/// Cancel order command
/// </summary>
public record CancelOrderCommand : IRequest<bool>
{
    public long OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

// ==================== Queries ====================

/// <summary>
/// Get order by ID query
/// </summary>
public record GetOrderQuery : IRequest<OrderDto?>
{
    public long OrderId { get; init; }
}

/// <summary>
/// Get orders by customer query
/// </summary>
public record GetOrdersByCustomerQuery : IRequest<List<OrderDto>>
{
    public string CustomerName { get; init; } = string.Empty;
}

/// <summary>
/// Get pending orders query
/// </summary>
public record GetPendingOrdersQuery : IRequest<List<OrderDto>>;

// ==================== Events ====================

/// <summary>
/// Order created event
/// </summary>
public record OrderCreatedEvent : IEvent
{
    public long OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}

/// <summary>
/// Order processing event
/// </summary>
public record OrderProcessingEvent : IEvent
{
    public long OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
}

/// <summary>
/// Order completed event
/// </summary>
public record OrderCompletedEvent : IEvent
{
    public long OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public DateTime CompletedAt { get; init; }
}

/// <summary>
/// Order cancelled event
/// </summary>
public record OrderCancelledEvent : IEvent
{
    public long OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

// ==================== DTOs ====================

/// <summary>
/// Order DTO
/// </summary>
public record OrderDto
{
    public long Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>
/// Order item DTO
/// </summary>
public record OrderItemDto
{
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Price { get; init; }
}

/// <summary>
/// Create order result
/// </summary>
public record CreateOrderResult
{
    public long OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
}

