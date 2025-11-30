using MemoryPack;

namespace OrderSystem.Api.Domain;

/// <summary>
/// Order status - simplified for core demonstration
/// </summary>
public enum OrderStatus
{
    /// <summary>Order created, awaiting processing</summary>
    Pending,

    /// <summary>Order cancelled by user or system</summary>
    Cancelled,

    /// <summary>Order confirmed and ready</summary>
    Confirmed,

    /// <summary>Order failed during processing</summary>
    Failed

    // 💡 扩展指南：添加更多状态
    // Confirmed,    // 订单已确认
    // Paid,         // 已支付
    // Shipped,      // 已发货
    // Delivered,    // 已送达
}

/// <summary>
/// Order entity - the aggregate root
/// Represents a customer order with items
/// </summary>
[MemoryPackable]
public partial record Order
{
    /// <summary>Unique order identifier</summary>
    public string OrderId { get; init; } = string.Empty;

    /// <summary>Customer who placed the order</summary>
    public string CustomerId { get; set; } = null!;

    /// <summary>List of ordered items</summary>
    public List<OrderItem> Items { get; set; } = new();

    /// <summary>Total order amount</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Current order status</summary>
    public OrderStatus Status { get; set; }

    /// <summary>Order creation timestamp</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last update timestamp</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Timestamp when order was cancelled</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>Reason for cancellation</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Reason for failure</summary>
    public string? FailureReason { get; set; }

    /// <summary>Shipping address</summary>
    /// <summary>Shipping address</summary>
    public string ShippingAddress { get; set; } = null!;

    /// <summary>Payment method</summary>
    public string PaymentMethod { get; set; } = null!;
}

/// <summary>
/// Order item - represents a single line in the order
/// </summary>
[MemoryPackable]
public partial record struct OrderItem
{
    // Explicit parameterless constructor required for structs with initializers (CS8983)
    public OrderItem()
    {
        ProductId = string.Empty;
        ProductName = string.Empty;
        Quantity = 0;
        UnitPrice = 0m;
    }

    /// <summary>Product identifier</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Product name (denormalized for performance)</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Quantity ordered</summary>
    public int Quantity { get; set; }

    /// <summary>Unit price at the time of order</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Calculated subtotal (Quantity * UnitPrice)</summary>
    public decimal Subtotal => Quantity * UnitPrice;
}

// ===== 扩展指南 =====
// 💡 如何扩展 Domain 模型？
//
// 1. 添加更多订单字段：
// public string? TrackingNumber { get; init; }
// public string? CouponCode { get; init; }
// public decimal DiscountAmount { get; init; }
//
// 2. 添加业务方法（Rich Domain Model）：
// public partial record Order
// {
//     public bool CanBeCancelled() => Status == OrderStatus.Pending;
//     public Order Confirm() => this with { Status = OrderStatus.Confirmed, UpdatedAt = DateTime.UtcNow };
//     public Order Cancel(string reason) => this with { Status = OrderStatus.Cancelled, UpdatedAt = DateTime.UtcNow };
// }
//
// 3. 添加验证：
// public partial record Order
// {
//     public void Validate()
//     {
//         if (Items.Count == 0)
//             throw new CatgaException("Order must have at least one item");
//         if (TotalAmount <= 0)
//             throw new CatgaException("Order total must be positive");
//     }
// }
//
// 4. 添加新的实体（例如：Customer, Product）：
// [MemoryPackable]
// public partial record Customer
// {
//     public string CustomerId { get; init; } = string.Empty;
//     public string Name { get; init; } = string.Empty;
//     public string Email { get; init; } = string.Empty;
// }
