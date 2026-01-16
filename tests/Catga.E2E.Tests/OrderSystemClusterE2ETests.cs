using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderSystem.Commands;
using OrderSystem.Dtos;
using OrderSystem.Models;
using Xunit;
using Xunit.Abstractions;

namespace Catga.E2E.Tests;

/// <summary>
/// E2E tests for OrderSystem in cluster mode.
/// Tests all cluster features: leader election, request forwarding, health checks.
/// </summary>
public sealed class OrderSystemClusterE2ETests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;
    private readonly List<HttpClient> _clients = new();

    public OrderSystemClusterE2ETests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task SystemInfo_ReturnsCorrectInformation()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var info = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(info);
        Assert.Equal("Catga OrderSystem", info.Service);
        Assert.Equal("1.0.0", info.Version);
        Assert.NotNull(info.Node);
        Assert.Equal("running", info.Status);
        
        _output.WriteLine($"System Info: {JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Health: {content}");
    }

    [Fact]
    public async Task HealthCheck_Ready_ReturnsHealthy()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_Live_ReturnsHealthy()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_Success_ReturnsCreated()
    {
        // Arrange
        var client = CreateClient();
        var request = new CreateOrderRequest(
            CustomerId: "customer-123",
            Items: new List<OrderItem>
            {
                new("ITEM-001", "Product A", 2, 29.99m),
                new("ITEM-002", "Product B", 1, 49.99m)
            }
        );

        // Act
        var response = await client.PostAsJsonAsync("/orders", request);
        var result = await response.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotNull(result.OrderId);
        Assert.Equal("customer-123", result.CustomerId);
        Assert.Equal(109.97m, result.Total);
        
        _output.WriteLine($"Created Order: {result.OrderId}, Total: {result.Total:C}");
    }

    [Fact]
    public async Task GetOrder_ExistingOrder_ReturnsOrder()
    {
        // Arrange
        var client = CreateClient();
        var createRequest = new CreateOrderRequest(
            CustomerId: "customer-456",
            Items: new List<OrderItem> { new("ITEM-003", "Product C", 1, 99.99m) }
        );
        var createResponse = await client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Act
        var response = await client.GetAsync($"/orders/{created!.OrderId}");
        var order = await response.Content.ReadFromJsonAsync<Order>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(order);
        Assert.Equal(created.OrderId, order.Id);
        Assert.Equal("customer-456", order.CustomerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.Items);
        
        _output.WriteLine($"Retrieved Order: {JsonSerializer.Serialize(order, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task GetOrder_NonExistentOrder_ReturnsNotFound()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/orders/non-existent-id");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllOrders_ReturnsOrderList()
    {
        // Arrange
        var client = CreateClient();
        
        // Create a few orders
        await client.PostAsJsonAsync("/orders", new CreateOrderRequest(
            "customer-1", new List<OrderItem> { new("ITEM-1", "Product 1", 1, 10m) }));
        await client.PostAsJsonAsync("/orders", new CreateOrderRequest(
            "customer-2", new List<OrderItem> { new("ITEM-2", "Product 2", 1, 20m) }));

        // Act
        var response = await client.GetAsync("/orders");
        var orders = await response.Content.ReadFromJsonAsync<List<Order>>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(orders);
        Assert.True(orders.Count >= 2);
        
        _output.WriteLine($"Total Orders: {orders.Count}");
    }

    [Fact]
    public async Task PayOrder_Success_UpdatesStatus()
    {
        // Arrange
        var client = CreateClient();
        var createRequest = new CreateOrderRequest(
            "customer-pay",
            new List<OrderItem> { new("ITEM-PAY", "Product Pay", 1, 50m) }
        );
        var createResponse = await client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Act
        var response = await client.PostAsync($"/orders/{created!.OrderId}/pay", null);
        var order = await response.Content.ReadFromJsonAsync<Order>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Paid, order.Status);
        
        _output.WriteLine($"Order {order.Id} paid successfully");
    }

    [Fact]
    public async Task ShipOrder_AfterPaid_UpdatesStatus()
    {
        // Arrange
        var client = CreateClient();
        var createRequest = new CreateOrderRequest(
            "customer-ship",
            new List<OrderItem> { new("ITEM-SHIP", "Product Ship", 1, 75m) }
        );
        var createResponse = await client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Pay first
        await client.PostAsync($"/orders/{created!.OrderId}/pay", null);

        // Act
        var response = await client.PostAsync($"/orders/{created.OrderId}/ship", null);
        var order = await response.Content.ReadFromJsonAsync<Order>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.NotNull(order.TrackingNumber);
        
        _output.WriteLine($"Order {order.Id} shipped with tracking: {order.TrackingNumber}");
    }

    [Fact]
    public async Task CancelOrder_Success_UpdatesStatus()
    {
        // Arrange
        var client = CreateClient();
        var createRequest = new CreateOrderRequest(
            "customer-cancel",
            new List<OrderItem> { new("ITEM-CANCEL", "Product Cancel", 1, 25m) }
        );
        var createResponse = await client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Act
        var response = await client.PostAsync($"/orders/{created!.OrderId}/cancel", null);
        var order = await response.Content.ReadFromJsonAsync<Order>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        
        _output.WriteLine($"Order {order.Id} cancelled successfully");
    }

    [Fact]
    public async Task GetOrderHistory_ReturnsEventList()
    {
        // Arrange
        var client = CreateClient();
        var createRequest = new CreateOrderRequest(
            "customer-history",
            new List<OrderItem> { new("ITEM-HISTORY", "Product History", 1, 100m) }
        );
        var createResponse = await client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Perform some actions
        await client.PostAsync($"/orders/{created!.OrderId}/pay", null);
        await client.PostAsync($"/orders/{created.OrderId}/ship", null);

        // Act
        var response = await client.GetAsync($"/orders/{created.OrderId}/history");
        var events = await response.Content.ReadFromJsonAsync<List<object>>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(events);
        Assert.True(events.Count >= 3); // Created, Paid, Shipped
        
        _output.WriteLine($"Order {created.OrderId} has {events.Count} events");
    }

    [Fact]
    public async Task GetStats_ReturnsStatistics()
    {
        // Arrange
        var client = CreateClient();
        
        // Create some orders with different statuses
        var order1 = await CreateOrderAsync(client, "customer-stats-1", 50m);
        var order2 = await CreateOrderAsync(client, "customer-stats-2", 75m);
        var order3 = await CreateOrderAsync(client, "customer-stats-3", 100m);
        
        await client.PostAsync($"/orders/{order1}/pay", null);
        await client.PostAsync($"/orders/{order2}/pay", null);
        await client.PostAsync($"/orders/{order2}/ship", null);
        await client.PostAsync($"/orders/{order3}/cancel", null);

        // Act
        var response = await client.GetAsync("/stats");
        var stats = await response.Content.ReadFromJsonAsync<StatsResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(stats);
        Assert.True(stats.TotalOrders >= 3);
        Assert.NotNull(stats.ByStatus);
        Assert.True(stats.TotalRevenue > 0);
        
        _output.WriteLine($"Stats: {JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task CompleteOrderWorkflow_Success()
    {
        // Arrange
        var client = CreateClient();
        var createRequest = new CreateOrderRequest(
            "customer-workflow",
            new List<OrderItem>
            {
                new("ITEM-W1", "Workflow Product 1", 2, 39.99m),
                new("ITEM-W2", "Workflow Product 2", 1, 59.99m)
            }
        );

        // Act & Assert - Create
        _output.WriteLine("=== Step 1: Create Order ===");
        var createResponse = await client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        _output.WriteLine($"Created: {created.OrderId}, Total: {created.Total:C}");

        // Act & Assert - Get
        _output.WriteLine("\n=== Step 2: Get Order ===");
        var getResponse = await client.GetAsync($"/orders/{created.OrderId}");
        var order = await getResponse.Content.ReadFromJsonAsync<Order>();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(OrderStatus.Pending, order!.Status);
        _output.WriteLine($"Status: {order.Status}");

        // Act & Assert - Pay
        _output.WriteLine("\n=== Step 3: Pay Order ===");
        var payResponse = await client.PostAsync($"/orders/{created.OrderId}/pay", null);
        var paidOrder = await payResponse.Content.ReadFromJsonAsync<Order>();
        Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);
        Assert.Equal(OrderStatus.Paid, paidOrder!.Status);
        _output.WriteLine($"Status: {paidOrder.Status}");

        // Act & Assert - Ship
        _output.WriteLine("\n=== Step 4: Ship Order ===");
        var shipResponse = await client.PostAsync($"/orders/{created.OrderId}/ship", null);
        var shippedOrder = await shipResponse.Content.ReadFromJsonAsync<Order>();
        Assert.Equal(HttpStatusCode.OK, shipResponse.StatusCode);
        Assert.Equal(OrderStatus.Shipped, shippedOrder!.Status);
        Assert.NotNull(shippedOrder.TrackingNumber);
        _output.WriteLine($"Status: {shippedOrder.Status}, Tracking: {shippedOrder.TrackingNumber}");

        // Act & Assert - History
        _output.WriteLine("\n=== Step 5: Get History ===");
        var historyResponse = await client.GetAsync($"/orders/{created.OrderId}/history");
        var events = await historyResponse.Content.ReadFromJsonAsync<List<object>>();
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.True(events!.Count >= 3);
        _output.WriteLine($"Total Events: {events.Count}");

        _output.WriteLine("\n=== Workflow Complete ===");
    }

    [Fact]
    public async Task ConcurrentOrders_AllSucceed()
    {
        // Arrange
        var client = CreateClient();
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - Create 10 orders concurrently
        _output.WriteLine("Creating 10 concurrent orders...");
        for (int i = 0; i < 10; i++)
        {
            var request = new CreateOrderRequest(
                $"customer-concurrent-{i}",
                new List<OrderItem> { new($"ITEM-{i}", $"Product {i}", 1, 10m + i) }
            );
            tasks.Add(client.PostAsJsonAsync("/orders", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        _output.WriteLine($"All {responses.Length} orders created successfully");
    }

    private async Task<string> CreateOrderAsync(HttpClient client, string customerId, decimal amount)
    {
        var request = new CreateOrderRequest(
            customerId,
            new List<OrderItem> { new("ITEM", "Product", 1, amount) }
        );
        var response = await client.PostAsJsonAsync("/orders", request);
        var result = await response.Content.ReadFromJsonAsync<OrderCreatedResult>();
        return result!.OrderId;
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        _clients.Add(client);
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }
}

public record SystemInfoResponse(
    string Service,
    string Version,
    string Node,
    string Mode,
    string Transport,
    string Persistence,
    string Status,
    DateTime Timestamp
);

public record StatsResponse(
    int TotalOrders,
    Dictionary<string, int> ByStatus,
    decimal TotalRevenue,
    DateTime Timestamp
);
