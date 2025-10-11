using Catga.Messages;

namespace OrderSystem;

// ==================== Commands ====================
public record CreateOrderCommand(string CustomerName,List<OrderItemDto> Items) : IRequest<CreateOrderResult>;

public record ProcessOrderCommand(long OrderId) : IRequest<bool>;
public record CompleteOrderCommand(long OrderId) : IRequest<bool>;
public record CancelOrderCommand(long OrderId,string Reason) : IRequest<bool>;

// ==================== Queries ====================
public record GetOrderQuery(long OrderId) : IRequest<OrderDto?>;
public record GetOrdersByCustomerQuery(string CustomerName) : IRequest<List<OrderDto>>;
public record GetPendingOrdersQuery : IRequest<List<OrderDto>>;


// ==================== Events ====================
public record OrderCreatedEvent(long OrderId, string OrderNumber, string CustomerName, decimal TotalAmount) : IEvent;
public record OrderProcessingEvent(long OrderId, string OrderNumber) : IEvent;
public record OrderCompletedEvent(long OrderId, string OrderNumber, DateTime CompletedAt) : IEvent;
public record OrderCancelledEvent(long OrderId, string OrderNumber, string Reason) : IEvent;


// ==================== DTOs ====================
public record OrderDto(long Id, string OrderNumber, string CustomerName, decimal TotalAmount, string Status, DateTime CreatedAt, DateTime? CompletedAt);
public record OrderItemDto(string ProductName, int Quantity, decimal Price);
public record CreateOrderResult(long OrderId, string OrderNumber);
