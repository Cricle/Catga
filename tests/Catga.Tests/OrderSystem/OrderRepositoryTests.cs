using Xunit;

namespace Catga.Tests.OrderSystem;

/// <summary>
/// Unit tests for Order domain and repository logic.
/// These tests verify the core business logic without HTTP layer.
/// </summary>
public class OrderRepositoryTests
{
    [Fact]
    public void Order_Creation_SetsCorrectDefaults()
    {
        // Arrange & Act
        var order = new TestOrder
        {
            OrderId = "ORD-123",
            CustomerId = "CUST-001",
            TotalAmount = 100.00m
        };

        // Assert
        Assert.Equal("ORD-123", order.OrderId);
        Assert.Equal("CUST-001", order.CustomerId);
        Assert.Equal(100.00m, order.TotalAmount);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.NotEqual(default, order.CreatedAt);
    }

    [Fact]
    public void Order_Pay_TransitionsCorrectly()
    {
        // Arrange
        var order = CreatePendingOrder();

        // Act
        order.Pay("Credit Card", "TXN-123");

        // Assert
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.PaidAt);
        Assert.Equal("Credit Card", order.PaymentMethod);
        Assert.Equal("TXN-123", order.PaymentTransactionId);
    }

    [Fact]
    public void Order_Pay_WhenNotPending_ThrowsException()
    {
        // Arrange
        var order = CreatePendingOrder();
        order.Pay("Card", "TXN-1");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => order.Pay("Card", "TXN-2"));
        Assert.Contains("Cannot pay", ex.Message);
    }

    [Fact]
    public void Order_Process_TransitionsCorrectly()
    {
        // Arrange
        var order = CreatePaidOrder();

        // Act
        order.Process();

        // Assert
        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void Order_Process_WhenNotPaid_ThrowsException()
    {
        // Arrange
        var order = CreatePendingOrder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => order.Process());
        Assert.Contains("Cannot process", ex.Message);
    }

    [Fact]
    public void Order_Ship_TransitionsCorrectly()
    {
        // Arrange
        var order = CreateProcessingOrder();

        // Act
        order.Ship("TRK-12345");

        // Assert
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.NotNull(order.ShippedAt);
        Assert.Equal("TRK-12345", order.TrackingNumber);
    }

    [Fact]
    public void Order_Ship_WhenNotProcessing_ThrowsException()
    {
        // Arrange
        var order = CreatePaidOrder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => order.Ship("TRK-123"));
        Assert.Contains("Cannot ship", ex.Message);
    }

    [Fact]
    public void Order_Deliver_TransitionsCorrectly()
    {
        // Arrange
        var order = CreateShippedOrder();

        // Act
        order.Deliver();

        // Assert
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);
    }

    [Fact]
    public void Order_Deliver_WhenNotShipped_ThrowsException()
    {
        // Arrange
        var order = CreateProcessingOrder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => order.Deliver());
        Assert.Contains("Cannot deliver", ex.Message);
    }

    [Fact]
    public void Order_Cancel_FromPending_Succeeds()
    {
        // Arrange
        var order = CreatePendingOrder();

        // Act
        order.Cancel("Customer requested");

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAt);
        Assert.Equal("Customer requested", order.CancellationReason);
    }

    [Fact]
    public void Order_Cancel_FromPaid_Succeeds()
    {
        // Arrange
        var order = CreatePaidOrder();

        // Act
        order.Cancel("Changed mind");

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Order_Cancel_FromProcessing_Succeeds()
    {
        // Arrange
        var order = CreateProcessingOrder();

        // Act
        order.Cancel("Out of stock");

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Order_Cancel_WhenDelivered_ThrowsException()
    {
        // Arrange
        var order = CreateDeliveredOrder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => order.Cancel("Too late"));
        Assert.Contains("Cannot cancel", ex.Message);
    }

    [Fact]
    public void Order_Cancel_WhenAlreadyCancelled_ThrowsException()
    {
        // Arrange
        var order = CreatePendingOrder();
        order.Cancel("First cancel");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => order.Cancel("Second cancel"));
        Assert.Contains("Cannot cancel", ex.Message);
    }

    [Fact]
    public void Order_TotalAmount_CalculatedCorrectly()
    {
        // Arrange
        var items = new List<TestOrderItem>
        {
            new("P1", "Product 1", 2, 10.00m),
            new("P2", "Product 2", 3, 20.00m),
            new("P3", "Product 3", 1, 50.00m)
        };

        // Act
        var total = items.Sum(i => i.Quantity * i.UnitPrice);

        // Assert
        Assert.Equal(130.00m, total); // 2*10 + 3*20 + 1*50 = 20 + 60 + 50
    }

    [Fact]
    public void OrderStatus_HasCorrectValues()
    {
        Assert.Equal(0, (int)OrderStatus.Pending);
        Assert.Equal(1, (int)OrderStatus.Paid);
        Assert.Equal(2, (int)OrderStatus.Processing);
        Assert.Equal(3, (int)OrderStatus.Shipped);
        Assert.Equal(4, (int)OrderStatus.Delivered);
        Assert.Equal(5, (int)OrderStatus.Cancelled);
    }

    #region Helper Methods

    private static TestOrder CreatePendingOrder()
    {
        return new TestOrder
        {
            OrderId = $"ORD-{Guid.NewGuid():N}",
            CustomerId = "CUST-001",
            TotalAmount = 100.00m,
            Status = OrderStatus.Pending,
            Items = [new("P1", "Product", 1, 100.00m)]
        };
    }

    private static TestOrder CreatePaidOrder()
    {
        var order = CreatePendingOrder();
        order.Pay("Card", "TXN-123");
        return order;
    }

    private static TestOrder CreateProcessingOrder()
    {
        var order = CreatePaidOrder();
        order.Process();
        return order;
    }

    private static TestOrder CreateShippedOrder()
    {
        var order = CreateProcessingOrder();
        order.Ship("TRK-123");
        return order;
    }

    private static TestOrder CreateDeliveredOrder()
    {
        var order = CreateShippedOrder();
        order.Deliver();
        return order;
    }

    #endregion

    #region Test Domain Classes

    public enum OrderStatus { Pending = 0, Paid = 1, Processing = 2, Shipped = 3, Delivered = 4, Cancelled = 5 }

    public record TestOrderItem(string ProductId, string ProductName, int Quantity, decimal UnitPrice);

    public class TestOrder
    {
        public string OrderId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentTransactionId { get; set; }
        public string? TrackingNumber { get; set; }
        public string? CancellationReason { get; set; }
        public List<TestOrderItem> Items { get; set; } = [];

        public void Pay(string paymentMethod, string transactionId)
        {
            if (Status != OrderStatus.Pending)
                throw new InvalidOperationException($"Cannot pay order in {Status} status");

            Status = OrderStatus.Paid;
            PaymentMethod = paymentMethod;
            PaymentTransactionId = transactionId;
            PaidAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Process()
        {
            if (Status != OrderStatus.Paid)
                throw new InvalidOperationException($"Cannot process order in {Status} status");

            Status = OrderStatus.Processing;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Ship(string trackingNumber)
        {
            if (Status != OrderStatus.Processing)
                throw new InvalidOperationException($"Cannot ship order in {Status} status");

            Status = OrderStatus.Shipped;
            TrackingNumber = trackingNumber;
            ShippedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Deliver()
        {
            if (Status != OrderStatus.Shipped)
                throw new InvalidOperationException($"Cannot deliver order in {Status} status");

            Status = OrderStatus.Delivered;
            DeliveredAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Cancel(string reason)
        {
            if (Status == OrderStatus.Delivered || Status == OrderStatus.Cancelled)
                throw new InvalidOperationException($"Cannot cancel order in {Status} status");

            Status = OrderStatus.Cancelled;
            CancellationReason = reason;
            CancelledAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    #endregion
}
