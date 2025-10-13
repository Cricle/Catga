using Catga.Messages;

namespace OrderSystem;

// ==========================================
// 分布式长事务示例：订单处理流程
// ==========================================
// 流程：CreateOrder → ReserveInventory → ProcessPayment → CreateShipment
// 补偿：失败时自动回滚（ReleaseInventory → RefundPayment → CancelOrder）
// 特性：零编排、自动补偿、自动幂等、自动重试
// ==========================================

// ==================== Commands ====================

/// <summary>创建订单 - 事务起点</summary>
public record CreateOrderCommand(string CustomerName, List<OrderItemDto> Items) : IRequest<CreateOrderResult>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; } = Guid.NewGuid().ToString(); // 事务追踪ID
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>预留库存 - 步骤1</summary>
public record ReserveInventoryCommand(long OrderId, List<OrderItemDto> Items) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>释放库存 - 补偿操作</summary>
public record ReleaseInventoryCommand(long OrderId, string ReservationId) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>处理支付 - 步骤2</summary>
public record ProcessPaymentCommand(long OrderId, decimal Amount) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>退款 - 补偿操作</summary>
public record RefundPaymentCommand(long OrderId, string PaymentId) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>创建发货 - 步骤3</summary>
public record CreateShipmentCommand(long OrderId) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>取消发货 - 补偿操作</summary>
public record CancelShipmentCommand(long OrderId, string ShipmentId) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

/// <summary>取消订单 - 最终补偿</summary>
public record CancelOrderCommand(long OrderId, string Reason) : IRequest<bool>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

// ==================== Queries ====================
public record GetOrderQuery(long OrderId) : IRequest<OrderDto?>;
public record GetOrdersByCustomerQuery(string CustomerName) : IRequest<List<OrderDto>>;
public record GetPendingOrdersQuery : IRequest<List<OrderDto>>;

// ==================== Events ====================

/// <summary>订单已创建 - 触发库存预留</summary>
public record OrderCreatedEvent(long OrderId, string OrderNumber, string CustomerName, decimal TotalAmount, List<OrderItemDto> Items) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>库存已预留 - 触发支付处理</summary>
public record InventoryReservedEvent(long OrderId, string ReservationId) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>库存预留失败 - 触发订单取消</summary>
public record InventoryReservationFailedEvent(long OrderId, string Reason) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>支付已处理 - 触发发货创建</summary>
public record PaymentProcessedEvent(long OrderId, string PaymentId) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>支付处理失败 - 触发库存释放</summary>
public record PaymentFailedEvent(long OrderId, string Reason, string? ReservationId) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>发货已创建 - 订单完成</summary>
public record ShipmentCreatedEvent(long OrderId, string ShipmentId) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>发货创建失败 - 触发支付退款</summary>
public record ShipmentFailedEvent(long OrderId, string Reason, string? PaymentId, string? ReservationId) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>订单已完成</summary>
public record OrderCompletedEvent(long OrderId, string OrderNumber, DateTime CompletedAt) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

/// <summary>订单已取消</summary>
public record OrderCancelledEvent(long OrderId, string OrderNumber, string Reason) : IEvent, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

// ==================== DTOs ====================
public record OrderDto(long Id, string OrderNumber, string CustomerName, decimal TotalAmount, string Status, DateTime CreatedAt, DateTime? CompletedAt);
public record OrderItemDto(string ProductName, int Quantity, decimal Price);
public record CreateOrderResult(long OrderId, string OrderNumber);
