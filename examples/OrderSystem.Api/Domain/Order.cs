using MemoryPack;

namespace OrderSystem.Api.Domain;

/// <summary>
/// Order status
/// </summary>
public enum OrderStatus
{
    Pending,      // Awaiting confirmation
    Confirmed,    // Confirmed by customer
    Paid,         // Payment completed
    Shipped,      // Shipped to customer
    Delivered,    // Delivered successfully
    Cancelled     // Cancelled by customer
}

/// <summary>
/// Order entity
/// </summary>
[MemoryPackable]
public partial record Order
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public List<OrderItem> Items { get; init; } = new();
    public decimal TotalAmount { get; init; }
    public OrderStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? ShippingAddress { get; init; }
    public string? PaymentMethod { get; init; }
}

/// <summary>
/// Order item
/// </summary>
[MemoryPackable]
public partial record OrderItem
{
    public string ProductId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Subtotal => Quantity * UnitPrice;
}

/// <summary>
/// Customer information
/// </summary>
[MemoryPackable]
public partial record Customer
{
    public string CustomerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
}

/// <summary>
/// Product information
/// </summary>
[MemoryPackable]
public partial record Product
{
    public string ProductId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Stock { get; init; }
    public string Category { get; init; } = string.Empty;
}

