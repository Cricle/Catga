using MemoryPack;

namespace OrderSystem.Api.Domain;

public enum OrderStatus { Pending, Confirmed, Cancelled }

[MemoryPackable]
public partial record Order
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; set; } = null!;
    public List<OrderItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}

[MemoryPackable]
public partial record struct OrderItem
{
    public OrderItem() { }
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
}
