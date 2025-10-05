using Catga.Messages;

namespace OrderApi.Commands;

public record CreateOrderCommand : MessageBase, ICommand<CreateOrderResult>
{
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

public record CreateOrderResult
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record GetOrderQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

public record OrderDto
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
