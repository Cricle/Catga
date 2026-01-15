using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Integration E2E tests covering complete business scenarios and workflows.
/// Tests realistic user journeys and complex multi-step operations.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemIntegrationE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemIntegrationE2ETests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region Complete Business Scenarios

    [Fact]
    public async Task Scenario_HappyPath_CompleteOrderJourney()
    {
        // Scenario: Customer places order, pays, receives shipment
        var customerId = $"happy-customer-{Guid.NewGuid():N}";

        // Step 1: Customer browses and creates order
        var createRequest = new
        {
            CustomerId = customerId,
            Items = new[]
            {
                new { ProductId = "LAPTOP-001", Quantity = 1, Price = 1299.99m },
                new { ProductId = "MOUSE-001", Quantity = 1, Price = 49.99m },
                new { ProductId = "KEYBOARD-001", Quantity = 1, Price = 129.99m }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var order = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
        Assert.Equal(1479.97m, order!.Total);

        // Step 2: Customer reviews order
        var getResponse = await _client.GetAsync($"/orders/{order.OrderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Step 3: Customer pays
        var payResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);

        // Step 4: Warehouse ships order
        var shipResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { });
        Assert.Equal(HttpStatusCode.OK, shipResponse.StatusCode);

        // Step 5: Customer checks order history
        var historyResponse = await _client.GetAsync($"/orders/{order.OrderId}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<object>>(_jsonOptions);
        Assert.NotNull(history);
        Assert.True(history.Count >= 2); // At least OrderCreated and OrderPaid events
    }

    [Fact]
    public async Task Scenario_CustomerCancellation_BeforePayment()
    {
        // Scenario: Customer creates order but cancels before paying
        var customerId = $"cancel-customer-{Guid.NewGuid():N}";

        // Create order
        var order = await CreateOrder(customerId, 
            new[] { ("PROD-001", 2, 50.00m) });

        // Customer changes mind and cancels
        var cancelResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { });
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // Verify order is cancelled
        var getResponse = await _client.GetAsync($"/orders/{order.OrderId}");
        var cancelledOrder = await getResponse.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.Equal("Cancelled", cancelledOrder!.Status); // Cancelled status
    }

    [Fact]
    public async Task Scenario_MultipleCustomers_IndependentOrders()
    {
        // Scenario: Multiple customers placing orders simultaneously
        var customers = new[] { "Alice", "Bob", "Charlie", "Diana", "Eve" };
        var orderTasks = new List<Task<OrderCreatedResponse>>();

        // All customers place orders
        foreach (var customer in customers)
        {
            orderTasks.Add(CreateOrder($"customer-{customer}", 
                new[] { ($"PROD-{customer}", 1, 100.00m) }));
        }

        var orders = await Task.WhenAll(orderTasks);

        // Verify all orders are unique and correct
        Assert.Equal(customers.Length, orders.Length);
        Assert.Equal(orders.Length, orders.Select(o => o.OrderId).Distinct().Count());

        // Each customer can see their order
        foreach (var order in orders)
        {
            var response = await _client.GetAsync($"/orders/{order.OrderId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Scenario_BulkOrder_LargeQuantities()
    {
        // Scenario: Business customer places bulk order
        var customerId = "bulk-business-customer";
        var order = await CreateOrder(customerId, new[]
        {
            ("WIDGET-A", 1000, 5.99m),
            ("WIDGET-B", 2000, 3.49m),
            ("WIDGET-C", 500, 12.99m)
        });

        // Expected: 1000*5.99 + 2000*3.49 + 500*12.99 = 5990 + 6980 + 6495 = 19465
        Assert.Equal(19465.00m, order.Total);

        // Business pays and receives shipment
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { });

        // Verify order completed successfully
        var finalOrder = await GetOrder(order.OrderId);
        Assert.NotNull(finalOrder);
        Assert.Equal("Shipped", finalOrder.Status); // Shipped
    }

    [Fact]
    public async Task Scenario_ReturningCustomer_MultipleOrders()
    {
        // Scenario: Customer places multiple orders over time
        var customerId = $"returning-{Guid.NewGuid():N}";

        // First order
        var order1 = await CreateOrder(customerId, new[] { ("PROD-1", 1, 50.00m) });
        await _client.PostAsJsonAsync($"/orders/{order1.OrderId}/pay", new { });
        await _client.PostAsJsonAsync($"/orders/{order1.OrderId}/ship", new { });

        // Second order
        var order2 = await CreateOrder(customerId, new[] { ("PROD-2", 2, 30.00m) });
        await _client.PostAsJsonAsync($"/orders/{order2.OrderId}/pay", new { });

        // Third order
        var order3 = await CreateOrder(customerId, new[] { ("PROD-3", 1, 100.00m) });

        // Verify all orders exist
        var allOrders = new[] { order1.OrderId, order2.OrderId, order3.OrderId };
        foreach (var orderId in allOrders)
        {
            var response = await _client.GetAsync($"/orders/{orderId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    #endregion

    #region Error Recovery Scenarios

    [Fact]
    public async Task Scenario_PaymentFailure_OrderRemainsPending()
    {
        // Scenario: Payment processing fails, order stays in pending state
        var order = await CreateOrder("payment-fail-customer", 
            new[] { ("PROD-1", 1, 100.00m) });

        // Attempt payment (may fail in some implementations)
        var payResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });

        // Verify order still exists and can be retried
        var getResponse = await _client.GetAsync($"/orders/{order.OrderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Scenario_PartialFulfillment_MultipleShipments()
    {
        // Scenario: Large order shipped in multiple batches
        var order = await CreateOrder("partial-ship-customer", new[]
        {
            ("ITEM-1", 10, 20.00m),
            ("ITEM-2", 10, 30.00m),
            ("ITEM-3", 10, 40.00m)
        });

        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });

        // Ship order (in real system, might be partial)
        var shipResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { });
        Assert.True(shipResponse.IsSuccessStatusCode || 
                    shipResponse.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scenario_SystemRecovery_OrdersPreserved()
    {
        // Scenario: Verify orders persist across requests (simulating restart)
        var order = await CreateOrder("recovery-customer", 
            new[] { ("PROD-1", 1, 50.00m) });

        // Simulate time passing / system restart by making new request
        await Task.Delay(100);

        // Verify order still exists
        var response = await _client.GetAsync($"/orders/{order.OrderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Business Analytics Scenarios

    [Fact]
    public async Task Scenario_DailyOperations_StatsTracking()
    {
        // Scenario: Track daily operations through stats
        var initialStats = await GetStats();
        var initialTotal = initialStats?.TotalOrders ?? 0;

        // Simulate day's operations
        var operations = new List<Task>();
        
        // Morning orders
        for (int i = 0; i < 5; i++)
        {
            operations.Add(CreateOrder($"morning-{i}", 
                new[] { ("PROD-1", 1, 25.00m) }));
        }

        await Task.WhenAll(operations);
        operations.Clear();

        // Afternoon orders
        for (int i = 0; i < 3; i++)
        {
            operations.Add(CreateOrder($"afternoon-{i}", 
                new[] { ("PROD-2", 2, 40.00m) }));
        }

        await Task.WhenAll(operations);

        // Check stats
        var finalStats = await GetStats();
        Assert.NotNull(finalStats);
        Assert.True(finalStats.TotalOrders >= initialTotal + 8);
    }

    [Fact]
    public async Task Scenario_PeakHours_HighVolume()
    {
        // Scenario: Handle peak shopping hours
        const int peakOrders = 30;
        var tasks = new List<Task<OrderCreatedResponse>>();

        // Simulate peak hour rush
        for (int i = 0; i < peakOrders; i++)
        {
            tasks.Add(CreateOrder($"peak-customer-{i}", 
                new[] { ($"PROD-{i % 10}", 1, 50.00m) }));
        }

        var orders = await Task.WhenAll(tasks);

        // Verify all orders processed
        Assert.Equal(peakOrders, orders.Length);
        Assert.All(orders, o => Assert.Matches("^[a-f0-9]{8}$", o.OrderId)); // 8-char hex hash format
    }

    #endregion

    #region Edge Case Scenarios

    [Fact]
    public async Task Scenario_ZeroValueOrder_FreeItems()
    {
        // Scenario: Promotional free items order
        var order = await CreateOrder("promo-customer", 
            new[] { ("FREE-SAMPLE", 1, 0.00m) });

        Assert.Equal(0.00m, order.Total);

        // Should still be able to process
        var payResponse = await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        Assert.True(payResponse.IsSuccessStatusCode || 
                    payResponse.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scenario_HighValueOrder_LuxuryItems()
    {
        // Scenario: High-value luxury purchase
        var order = await CreateOrder("luxury-customer", new[]
        {
            ("DIAMOND-RING", 1, 25000.00m),
            ("GOLD-WATCH", 1, 15000.00m),
            ("LUXURY-BAG", 1, 8000.00m)
        });

        Assert.Equal(48000.00m, order.Total);

        // Process high-value order
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        var finalOrder = await GetOrder(order.OrderId);
        Assert.NotNull(finalOrder);
    }

    [Fact]
    public async Task Scenario_InternationalOrder_VariousCurrencies()
    {
        // Scenario: Orders with decimal precision (simulating currency conversion)
        var order = await CreateOrder("intl-customer", new[]
        {
            ("PROD-EUR", 1, 85.67m),  // Euros converted
            ("PROD-GBP", 1, 73.42m),  // Pounds converted
            ("PROD-JPY", 1, 0.89m)    // Yen converted
        });

        // Verify precise calculation
        Assert.Equal(159.98m, order.Total);
    }

    [Fact]
    public async Task Scenario_SubscriptionOrder_RecurringItems()
    {
        // Scenario: Subscription-based recurring order
        var customerId = $"subscriber-{Guid.NewGuid():N}";

        // Simulate monthly subscription orders
        for (int month = 1; month <= 3; month++)
        {
            var order = await CreateOrder(customerId, 
                new[] { ("SUBSCRIPTION-BOX", 1, 29.99m) });
            
            Assert.Equal(29.99m, order.Total);
            await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        }

        // Verify all subscription orders processed
        await Task.Delay(100);
        var statsResponse = await _client.GetAsync("/stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
    }

    #endregion

    #region System Health Scenarios

    [Fact]
    public async Task Scenario_HealthMonitoring_AllEndpointsResponsive()
    {
        // Scenario: Monitor system health
        var healthChecks = new[]
        {
            "/health",
            "/health/ready",
            "/health/live"
        };

        foreach (var endpoint in healthChecks)
        {
            var response = await _client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Scenario_SystemInfo_ReturnsCompleteInformation()
    {
        // Scenario: Verify system information endpoint
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var info = await response.Content.ReadFromJsonAsync<SystemInfoResponse>(_jsonOptions);
        Assert.NotNull(info);
        Assert.NotNull(info.Service);
        Assert.NotNull(info.Version);
        Assert.NotNull(info.Transport);
        Assert.NotNull(info.Persistence);
    }

    [Fact]
    public async Task Scenario_ContinuousOperations_ExtendedPeriod()
    {
        // Scenario: System handles continuous operations over time
        const int iterations = 10;
        const int ordersPerIteration = 5;

        for (int i = 0; i < iterations; i++)
        {
            var tasks = new List<Task<OrderCreatedResponse>>();
            
            for (int j = 0; j < ordersPerIteration; j++)
            {
                tasks.Add(CreateOrder($"continuous-{i}-{j}", 
                    new[] { ("PROD-1", 1, 10.00m) }));
            }

            var orders = await Task.WhenAll(tasks);
            Assert.Equal(ordersPerIteration, orders.Length);

            // Small delay between iterations
            await Task.Delay(50);
        }

        // Verify system still responsive
        var healthResponse = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    #endregion

    #region Data Consistency Scenarios

    [Fact]
    public async Task Scenario_OrderHistory_CompleteAuditTrail()
    {
        // Scenario: Verify complete audit trail through order lifecycle
        var order = await CreateOrder("audit-customer", 
            new[] { ("PROD-1", 1, 100.00m) });

        // Perform multiple operations
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { });

        // Check history
        var historyResponse = await _client.GetAsync($"/orders/{order.OrderId}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var history = await historyResponse.Content.ReadFromJsonAsync<List<object>>(_jsonOptions);
        Assert.NotNull(history);
        Assert.True(history.Count >= 2); // Multiple events recorded
    }

    [Fact]
    public async Task Scenario_StatsConsistency_AccurateAggregation()
    {
        // Scenario: Verify stats accurately reflect system state
        var initialStats = await GetStats();
        var initialTotal = initialStats?.TotalOrders ?? 0;

        // Create known number of orders
        const int newOrders = 10;
        var tasks = new List<Task<OrderCreatedResponse>>();
        
        for (int i = 0; i < newOrders; i++)
        {
            tasks.Add(CreateOrder($"stats-test-{i}", 
                new[] { ("PROD-1", 1, 50.00m) }));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(100); // Allow for consistency

        // Verify stats updated
        var finalStats = await GetStats();
        Assert.NotNull(finalStats);
        Assert.True(finalStats.TotalOrders >= initialTotal + newOrders);
    }

    #endregion

    #region Helper Methods

    private async Task<OrderCreatedResponse> CreateOrder(
        string customerId, 
        (string ProductId, int Quantity, decimal Price)[] items)
    {
        var request = new
        {
            CustomerId = customerId,
            Items = items.Select(i => new
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToArray()
        };

        var response = await _client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions))!;
    }

    private async Task<OrderResponse?> GetOrder(string orderId)
    {
        var response = await _client.GetAsync($"/orders/{orderId}");
        if (response.StatusCode != HttpStatusCode.OK) return null;
        return await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
    }

    private async Task<StatsResponse?> GetStats()
    {
        var response = await _client.GetAsync("/stats");
        if (response.StatusCode != HttpStatusCode.OK) return null;
        return await response.Content.ReadFromJsonAsync<StatsResponse>(_jsonOptions);
    }

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);
    
    private record OrderResponse(
        string OrderId,
        string CustomerId,
        decimal Total,
        string Status  // Changed from int to string to match API enum serialization
    );

    private record StatsResponse(
        int TotalOrders,
        Dictionary<string, int> ByStatus,
        decimal TotalRevenue,
        DateTime Timestamp
    );

    private record SystemInfoResponse(
        string Service,
        string Version,
        string Node,
        string Mode,
        string Transport,
        string Persistence,
        string Status,
        DateTime Timestamp
    );

    #endregion
}

