using MemoryPack;

namespace OrderSystem.Api.Domain;

/// <summary>
/// Order lifecycle: Pending → Paid → Processing → Shipped → Delivered
///                     ↓         ↓         ↓          ↓
///                 Cancelled  Cancelled  Cancelled  Returned
/// </summary>
public enum OrderStatus
{
    Pending = 0,      // Order created, awaiting payment
    Paid = 1,         // Payment received
    Processing = 2,   // Order being prepared
    Shipped = 3,      // Order shipped
    Delivered = 4,    // Order delivered
    Cancelled = 5,    // Order cancelled
    Refunded = 6,     // Order refunded
    Failed = 7        // Order failed (payment, stock, etc.)
}

/// <summary>
/// Core order entity with full lifecycle support.
/// </summary>
[MemoryPackable]
public partial record Order
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Shipping { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Timestamps
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Additional info
    public string? CancellationReason { get; set; }
    public string? TrackingNumber { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentTransactionId { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }
    public string? Notes { get; set; }

    // Computed properties
    [MemoryPackIgnore]
    public bool CanCancel => Status is OrderStatus.Pending or OrderStatus.Paid or OrderStatus.Processing;

    [MemoryPackIgnore]
    public bool CanShip => Status == OrderStatus.Processing;

    [MemoryPackIgnore]
    public bool CanDeliver => Status == OrderStatus.Shipped;
}

[MemoryPackable]
public partial record struct OrderItem
{
    public OrderItem() { }
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
}

[MemoryPackable]
public partial record struct ShippingAddress
{
    public ShippingAddress() { }
    public string Name { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
