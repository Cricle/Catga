using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// E2E tests for complete order lifecycle: Create → Pay → Process → Ship → Deliver
/// </summary>
public class OrderLifecycleE2ETests : IClassFixture<OrderSystemWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderLifecycleE2ETests(OrderSystemWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CompleteOrderLifecycle_FromCreateToDelivery_Succeeds()
    {
        // Step 1: Create order
        var createRequest = new
        {
            CustomerId = $"lifecycle-{Guid.NewGuid():N}",
            Items = new[]
            {
                new { ProductId = "PROD-001", ProductName = "Laptop", Quantity = 1, UnitPrice = 999.99m },
                new { ProductId = "PROD-002", ProductName = "Mouse", Quantity = 2, UnitPrice = 29.99m }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/orders", createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.NotNull(created);
        Assert.StartsWith("ORD-", created.OrderId);
        Assert.Equal(1059.97m, created.TotalAmount); // 999.99 + 2*29.99

        // Verify initial status is Pending (0)
        var order = await GetOrder(created.OrderId);
        Assert.Equal(0, order!.Status); // Pending

        // Step 2: Pay order
        var payRequest = new { PaymentMethod = "Credit Card", TransactionId = $"TXN-{Guid.NewGuid():N}" };
        var payResponse = await _client.PostAsJsonAsync($"/api/orders/{created.OrderId}/pay", payRequest);
        Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);

        order = await GetOrder(created.OrderId);
        Assert.Equal(1, order!.Status); // Paid
        Assert.NotNull(order.PaidAt);

        // Step 3: Process order
        var processResponse = await _client.PostAsJsonAsync($"/api/orders/{created.OrderId}/process", new { });
        Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);

        order = await GetOrder(created.OrderId);
        Assert.Equal(2, order!.Status); // Processing

        // Step 4: Ship order
        var shipRequest = new { TrackingNumber = $"TRK-{Guid.NewGuid():N}" };
        var shipResponse = await _client.PostAsJsonAsync($"/api/orders/{created.OrderId}/ship", shipRequest);
        Assert.Equal(HttpStatusCode.OK, shipResponse.StatusCode);

        order = await GetOrder(created.OrderId);
        Assert.Equal(3, order!.Status); // Shipped
        Assert.NotNull(order.ShippedAt);
        Assert.NotNull(order.TrackingNumber);

        // Step 5: Deliver order
        var deliverResponse = await _client.PostAsJsonAsync($"/api/orders/{created.OrderId}/deliver", new { });
        Assert.Equal(HttpStatusCode.OK, deliverResponse.StatusCode);

        order = await GetOrder(created.OrderId);
        Assert.Equal(4, order!.Status); // Delivered
        Assert.NotNull(order.DeliveredAt);
    }

    [Fact]
    public async Task CancelOrder_FromPendingStatus_Succeeds()
    {
        // Create order
        var order = await CreateTestOrder();

        // Cancel immediately
        var cancelRequest = new { Reason = "Changed my mind" };
        var cancelResponse = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/cancel", cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var cancelled = await GetOrder(order.OrderId);
        Assert.Equal(5, cancelled!.Status); // Cancelled
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal("Changed my mind", cancelled.CancellationReason);
    }

    [Fact]
    public async Task CancelOrder_AfterPayment_Succeeds()
    {
        // Create and pay order
        var order = await CreateTestOrder();
        await PayOrder(order.OrderId);

        // Cancel after payment
        var cancelRequest = new { Reason = "Found better price elsewhere" };
        var cancelResponse = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/cancel", cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var cancelled = await GetOrder(order.OrderId);
        Assert.Equal(5, cancelled!.Status); // Cancelled
    }

    [Fact]
    public async Task CancelOrder_AfterDelivery_Fails()
    {
        // Complete full lifecycle
        var order = await CreateTestOrder();
        await PayOrder(order.OrderId);
        await ProcessOrder(order.OrderId);
        await ShipOrder(order.OrderId);
        await DeliverOrder(order.OrderId);

        // Try to cancel delivered order
        var cancelRequest = new { Reason = "Too late" };
        var cancelResponse = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/cancel", cancelRequest);

        // Should fail or return error
        var delivered = await GetOrder(order.OrderId);
        Assert.Equal(4, delivered!.Status); // Still Delivered
    }

    [Fact]
    public async Task PayOrder_WithDifferentPaymentMethods_Succeeds()
    {
        var paymentMethods = new[] { "Credit Card", "PayPal", "Bank Transfer", "Apple Pay", "Google Pay" };

        foreach (var method in paymentMethods)
        {
            var order = await CreateTestOrder();
            var payRequest = new { PaymentMethod = method, TransactionId = $"TXN-{Guid.NewGuid():N}" };
            var payResponse = await _client.PostAsJsonAsync($"/api/orders/{order.OrderId}/pay", payRequest);

            Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);

            var paid = await GetOrder(order.OrderId);
            Assert.Equal(1, paid!.Status);
            Assert.Equal(method, paid.PaymentMethod);
        }
    }

    [Fact]
    public async Task CreateOrderWithFlow_Succeeds()
    {
        var request = new
        {
            CustomerId = $"flow-{Guid.NewGuid():N}",
            Items = new[]
            {
                new { ProductId = "FLOW-001", ProductName = "Flow Product", Quantity = 1, UnitPrice = 100.00m }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders/flow", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.NotNull(created);
        Assert.StartsWith("ORD-", created.OrderId);
    }

    [Fact]
    public async Task GetOrderStats_ReturnsAccurateStats()
    {
        // Get initial stats
        var initialStats = await GetStats();
        var initialTotal = initialStats?.Total ?? 0;

        // Create multiple orders with different states
        var orders = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var order = await CreateTestOrder();
            orders.Add(order.OrderId);
        }

        // Pay first order
        await PayOrder(orders[0]);

        // Complete second order
        await PayOrder(orders[1]);
        await ProcessOrder(orders[1]);
        await ShipOrder(orders[1]);
        await DeliverOrder(orders[1]);

        // Cancel third order
        await _client.PostAsJsonAsync($"/api/orders/{orders[2]}/cancel", new { Reason = "Test" });

        // Verify stats
        var stats = await GetStats();
        Assert.NotNull(stats);
        Assert.True(stats.Total >= initialTotal + 3);
    }

    [Fact]
    public async Task GetAllOrders_WithStatusFilter_ReturnsFilteredOrders()
    {
        // Create orders
        var pendingOrder = await CreateTestOrder();
        var paidOrder = await CreateTestOrder();
        await PayOrder(paidOrder.OrderId);

        // Get pending orders
        var pendingResponse = await _client.GetAsync("/api/orders?status=0&limit=100");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pendingOrders = await pendingResponse.Content.ReadFromJsonAsync<List<OrderResponse>>(_jsonOptions);
        Assert.NotNull(pendingOrders);
        Assert.Contains(pendingOrders, o => o.OrderId == pendingOrder.OrderId);

        // Get paid orders
        var paidResponse = await _client.GetAsync("/api/orders?status=1&limit=100");
        Assert.Equal(HttpStatusCode.OK, paidResponse.StatusCode);
        var paidOrders = await paidResponse.Content.ReadFromJsonAsync<List<OrderResponse>>(_jsonOptions);
        Assert.NotNull(paidOrders);
        Assert.Contains(paidOrders, o => o.OrderId == paidOrder.OrderId);
    }

    [Fact]
    public async Task GetUserOrders_ReturnsOnlyUserOrders()
    {
        var customerId = $"user-{Guid.NewGuid():N}";

        // Create orders for specific customer
        for (int i = 0; i < 5; i++)
        {
            var request = new
            {
                CustomerId = customerId,
                Items = new[] { new { ProductId = $"PROD-{i}", ProductName = $"Product {i}", Quantity = 1, UnitPrice = 10.00m } }
            };
            await _client.PostAsJsonAsync("/api/orders", request);
        }

        // Create order for different customer
        var otherRequest = new
        {
            CustomerId = "other-customer",
            Items = new[] { new { ProductId = "OTHER", ProductName = "Other", Quantity = 1, UnitPrice = 10.00m } }
        };
        await _client.PostAsJsonAsync("/api/orders", otherRequest);

        // Get user orders (endpoint is /api/orders/customer/{customerId})
        var response = await _client.GetAsync($"/api/orders/customer/{customerId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var orders = JsonSerializer.Deserialize<List<OrderResponse>>(content, _jsonOptions);
        Assert.NotNull(orders);
        Assert.True(orders.Count >= 5, $"Expected at least 5 orders but got {orders.Count}");
        Assert.All(orders, o => Assert.Equal(customerId, o.CustomerId));
    }

    [Fact]
    public async Task SystemInfo_ReturnsExpectedData()
    {
        var response = await _client.GetAsync("/api/system/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var info = await response.Content.ReadFromJsonAsync<SystemInfoResponse>(_jsonOptions);
        Assert.NotNull(info);
        Assert.NotNull(info.Transport);
        Assert.NotNull(info.Persistence);
        Assert.NotNull(info.Environment);
        Assert.NotNull(info.Version);
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #region Helper Methods

    private async Task<OrderCreatedResponse> CreateTestOrder()
    {
        var request = new
        {
            CustomerId = $"test-{Guid.NewGuid():N}",
            Items = new[]
            {
                new { ProductId = "TEST-001", ProductName = "Test Product", Quantity = 1, UnitPrice = 100.00m }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        return (await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions))!;
    }

    private async Task<OrderResponse?> GetOrder(string orderId)
    {
        var response = await _client.GetAsync($"/api/orders/{orderId}");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        return await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
    }

    private async Task<StatsResponse?> GetStats()
    {
        var response = await _client.GetAsync("/api/orders/stats");
        return await response.Content.ReadFromJsonAsync<StatsResponse>(_jsonOptions);
    }

    private async Task PayOrder(string orderId)
    {
        var request = new { PaymentMethod = "Credit Card", TransactionId = $"TXN-{Guid.NewGuid():N}" };
        await _client.PostAsJsonAsync($"/api/orders/{orderId}/pay", request);
    }

    private async Task ProcessOrder(string orderId)
    {
        await _client.PostAsJsonAsync($"/api/orders/{orderId}/process", new { });
    }

    private async Task ShipOrder(string orderId)
    {
        var request = new { TrackingNumber = $"TRK-{Guid.NewGuid():N}" };
        await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", request);
    }

    private async Task DeliverOrder(string orderId)
    {
        await _client.PostAsJsonAsync($"/api/orders/{orderId}/deliver", new { });
    }

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal TotalAmount, DateTime CreatedAt);

    private record OrderResponse(
        string OrderId,
        string CustomerId,
        decimal TotalAmount,
        int Status,
        DateTime? PaidAt = null,
        DateTime? ShippedAt = null,
        DateTime? DeliveredAt = null,
        DateTime? CancelledAt = null,
        string? PaymentMethod = null,
        string? TrackingNumber = null,
        string? CancellationReason = null
    );

    private record StatsResponse(
        int Total,
        int Pending,
        int Paid,
        int Processing,
        int Shipped,
        int Delivered,
        int Cancelled,
        decimal TotalRevenue
    );

    private record SystemInfoResponse(
        string Transport,
        string Persistence,
        string Environment,
        string Version,
        bool DevelopmentMode,
        bool ClusterEnabled
    );

    #endregion
}
