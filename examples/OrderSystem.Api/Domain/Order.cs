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
    Cancelled

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
    public string CustomerId { get; init; } = string.Empty;

    /// <summary>List of ordered items</summary>
    public List<OrderItem> Items { get; init; } = new();

    /// <summary>Total order amount</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Current order status</summary>
    public OrderStatus Status { get; init; }

    /// <summary>Order creation timestamp</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last update timestamp</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Shipping address</summary>
    public string? ShippingAddress { get; init; }

    /// <summary>Payment method</summary>
    public string? PaymentMethod { get; init; }
}

/// <summary>
/// Order item - represents a single line in the order
/// </summary>
[MemoryPackable]
public partial record OrderItem
{
    /// <summary>Product identifier</summary>
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Product name (denormalized for performance)</summary>
    public string ProductName { get; init; } = string.Empty;

    /// <summary>Quantity ordered</summary>
    public int Quantity { get; init; }

    /// <summary>Unit price at the time of order</summary>
    public decimal UnitPrice { get; init; }

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
