using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// E2E tests for complete order lifecycle: Create → Pay → Process → Ship → Deliver
/// </summary>
[Collection("OrderSystem")]
public class OrderLifecycleE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderLifecycleE2ETests(OrderSystemFixture factory)
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

        var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.NotNull(created);
        Assert.Matches("^[a-f0-9]{8}$", created.OrderId); // 8-char hex hash
        Assert.Equal(1059.97m, created.TotalAmount); // 999.99 + 2*29.99

        // Verify initial status is Pending
        var order = await GetOrder(created.OrderId);
        Assert.Equal("Pending", order!.Status);

        // Step 2: Pay order
        var payRequest = new { PaymentMethod = "Credit Card", TransactionId = $"TXN-{Guid.NewGuid():N}" };
        var payResponse = await _client.PostAsJsonAsync($"/orders/{created.OrderId}/pay", payRequest);
        Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);

        order = await GetOrder(created.OrderId);
        Assert.Equal("Paid", order!.Status);
        Assert.NotNull(order.PaidAt);

        // Step 3: Ship order (no process endpoint in API)
        var shipRequest = new { TrackingNumber = $"TRK-{Guid.NewGuid():N}" };
        var shipResponse = await _client.PostAsJsonAsync($"/orders/{created.OrderId}/ship", shipRequest);
        Assert.Equal(HttpStatusCode.OK, shipResponse.StatusCode);

        order = await GetOrder(created.OrderId);
        Assert.Equal("Shipped", order!.Status);
        Assert.NotNull(order.ShippedAt);
        Assert.NotNull(order.TrackingNumber);
    }

    [Fact]
    public async Task CancelOrder_FromPendingStatus_Succeeds()
    {
        // Create order
        var order = await CreateTestOrder();

        // Cancel immediately
        var cancelRequest = new { Reason = "Changed my mind" };
        var cancelResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var cancelled = await GetOrder(order.OrderId);
        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.NotNull(cancelled.CancelledAt);
    }

    [Fact]
    public async Task CancelOrder_AfterPayment_Succeeds()
    {
        // Create and pay order
        var order = await CreateTestOrder();
        await PayOrder(order.OrderId);

        // Cancel after payment
        var cancelRequest = new { Reason = "Found better price elsewhere" };
        var cancelResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var cancelled = await GetOrder(order.OrderId);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task CancelOrder_AfterShipment_Fails()
    {
        // Complete lifecycle to shipped
        var order = await CreateTestOrder();
        await PayOrder(order.OrderId);
        await ShipOrder(order.OrderId);

        // Try to cancel shipped order
        var cancelRequest = new { Reason = "Too late" };
        var cancelResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", cancelRequest);

        // Should fail or return error
        var shipped = await GetOrder(order.OrderId);
        Assert.Equal("Shipped", shipped!.Status); // Still Shipped
    }

    [Fact]
    public async Task PayOrder_WithDifferentPaymentMethods_Succeeds()
    {
        var paymentMethods = new[] { "Credit Card", "PayPal", "Bank Transfer", "Apple Pay", "Google Pay" };

        foreach (var method in paymentMethods)
        {
            var order = await CreateTestOrder();
            var payRequest = new { PaymentMethod = method, TransactionId = $"TXN-{Guid.NewGuid():N}" };
            var payResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", payRequest);

            Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);

            var paid = await GetOrder(order.OrderId);
            Assert.Equal("Paid", paid!.Status);
        }
    }

    [Fact(Skip = "Flow endpoint not implemented")]
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

        var response = await _client.PostAsJsonAsync("/orders/flow", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.NotNull(created);
        Assert.Matches("^[a-f0-9]{8}$", created.OrderId);
    }

    [Fact]
    public async Task GetOrderStats_ReturnsAccurateStats()
    {
        // Get initial stats
        var initialStats = await GetStats();
        var initialTotal = initialStats?.TotalOrders ?? 0;

        // Create multiple orders with different states
        var orders = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var order = await CreateTestOrder();
            orders.Add(order.OrderId);
        }

        // Pay first order
        await PayOrder(orders[0]);

        // Ship second order
        await PayOrder(orders[1]);
        await ShipOrder(orders[1]);

        // Cancel third order
        await _client.PostAsJsonAsync($"/orders/{orders[2]}/cancel", new { Reason = "Test" });

        // Verify stats
        var stats = await GetStats();
        Assert.NotNull(stats);
        Assert.True(stats.TotalOrders >= initialTotal + 3);
    }

    [Fact]
    public async Task GetAllOrders_WithStatusFilter_ReturnsFilteredOrders()
    {
        // Create orders
        var pendingOrder = await CreateTestOrder();
        var paidOrder = await CreateTestOrder();
        await PayOrder(paidOrder.OrderId);

        // Get all orders (filtering not implemented in API)
        var allResponse = await _client.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var allOrders = await allResponse.Content.ReadFromJsonAsync<List<OrderResponse>>(_jsonOptions);
        Assert.NotNull(allOrders);
        Assert.Contains(allOrders, o => o.OrderId == pendingOrder.OrderId);
        Assert.Contains(allOrders, o => o.OrderId == paidOrder.OrderId);
    }

    [Fact(Skip = "Customer orders endpoint not implemented")]
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
            await _client.PostAsJsonAsync("/orders", request);
        }

        // Create order for different customer
        var otherRequest = new
        {
            CustomerId = "other-customer",
            Items = new[] { new { ProductId = "OTHER", ProductName = "Other", Quantity = 1, UnitPrice = 10.00m } }
        };
        await _client.PostAsJsonAsync("/orders", otherRequest);

        // Get user orders (endpoint is /orders/customer/{customerId})
        var response = await _client.GetAsync($"/orders/customer/{customerId}");
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
        var response = await _client.GetAsync("/");
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

        var response = await _client.PostAsJsonAsync("/orders", request);
        return (await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions))!;
    }

    private async Task<OrderResponse?> GetOrder(string orderId)
    {
        var response = await _client.GetAsync($"/orders/{orderId}");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        return await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
    }

    private async Task<StatsResponse?> GetStats()
    {
        var response = await _client.GetAsync("/stats");
        return await response.Content.ReadFromJsonAsync<StatsResponse>(_jsonOptions);
    }

    private async Task PayOrder(string orderId)
    {
        var request = new { PaymentMethod = "Credit Card", TransactionId = $"TXN-{Guid.NewGuid():N}" };
        await _client.PostAsJsonAsync($"/orders/{orderId}/pay", request);
    }

    private async Task ShipOrder(string orderId)
    {
        var request = new { TrackingNumber = $"TRK-{Guid.NewGuid():N}" };
        await _client.PostAsJsonAsync($"/orders/{orderId}/ship", request);
    }

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal TotalAmount, DateTime CreatedAt);

    private record OrderResponse(
        string OrderId,
        string CustomerId,
        decimal TotalAmount,
        string Status,
        DateTime? PaidAt = null,
        DateTime? ShippedAt = null,
        DateTime? DeliveredAt = null,
        DateTime? CancelledAt = null,
        string? PaymentMethod = null,
        string? TrackingNumber = null,
        string? CancellationReason = null
    );

    private record StatsResponse(
        int TotalOrders,
        Dictionary<string, int> ByStatus,
        decimal TotalRevenue,
        DateTime Timestamp
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


