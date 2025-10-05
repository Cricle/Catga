using Catga.Messages;

namespace NotificationService.Events;

// 订单创建事件 - 与订单服务中的事件定义相同
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal TotalAmount { get; init; }
}
