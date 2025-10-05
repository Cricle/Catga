using Catga.Messages;

namespace OrderService.Commands;

// 创建订单命令
public record CreateOrderCommand : MessageBase, ICommand<OrderResult>
{
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

// 订单结果
public record OrderResult
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

// 获取订单查询
public record GetOrderQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

// 订单DTO
public record OrderDto
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

// 订单创建事件
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal TotalAmount { get; init; }
}
