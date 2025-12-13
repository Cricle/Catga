using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// End-to-end tests for OrderSystem.Api example.
/// These tests verify the complete request flow from HTTP to handler execution.
/// </summary>
public class OrderSystemE2ETests : IClassFixture<OrderSystemWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemE2ETests(OrderSystemWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsOrderId()
    {
        // Arrange
        var request = new
        {
            CustomerId = "test-customer-1",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 2, UnitPrice = 99.99m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.NotNull(result);
        Assert.StartsWith("ORD-", result.OrderId);
        Assert.Equal(199.98m, result.TotalAmount);
    }

    [Fact]
    public async Task GetOrder_ExistingOrder_ReturnsOrder()
    {
        // Arrange - create an order first
        var createRequest = new
        {
            CustomerId = "test-customer-2",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1, UnitPrice = 50.00m }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/orders", createRequest);
        var created = JsonSerializer.Deserialize<OrderCreatedResponse>(
            await createResponse.Content.ReadAsStringAsync(), _jsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/orders/{created!.OrderId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var order = JsonSerializer.Deserialize<OrderResponse>(content, _jsonOptions);
        Assert.NotNull(order);
        Assert.Equal(created.OrderId, order.OrderId);
        Assert.Equal("test-customer-2", order.CustomerId);
    }

    [Fact]
    public async Task GetOrder_NonExistingOrder_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/orders/non-existing-id");

        // Assert - returns NotFound when order not found
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserOrders_ReturnsOrdersForCustomer()
    {
        // Arrange - create orders for a customer
        var customerId = $"test-customer-{Guid.NewGuid():N}";
        for (int i = 0; i < 3; i++)
        {
            var request = new
            {
                CustomerId = customerId,
                Items = new[]
                {
                    new { ProductId = $"prod-{i}", Quantity = 1, UnitPrice = 10.00m * (i + 1) }
                }
            };
            await _client.PostAsJsonAsync("/api/orders", request);
        }

        // Act - endpoint is /api/orders/customer/{customerId}
        var response = await _client.GetAsync($"/api/orders/customer/{customerId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var orders = JsonSerializer.Deserialize<List<OrderResponse>>(content, _jsonOptions);
        Assert.NotNull(orders);
        Assert.Equal(3, orders.Count);
        Assert.All(orders, o => Assert.Equal(customerId, o.CustomerId));
    }

    [Fact]
    public async Task GetOrderStats_ReturnsStats()
    {
        // Act
        var response = await _client.GetAsync("/api/orders/stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("total", content.ToLowerInvariant());
    }

    [Fact]
    public async Task CreateOrder_MultipleItems_CalculatesTotalCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "test-customer-multi",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 2, UnitPrice = 10.00m },
                new { ProductId = "prod-2", Quantity = 3, UnitPrice = 20.00m },
                new { ProductId = "prod-3", Quantity = 1, UnitPrice = 50.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.NotNull(result);
        // 2*10 + 3*20 + 1*50 = 20 + 60 + 50 = 130
        Assert.Equal(130.00m, result.TotalAmount);
    }

    [Fact]
    public async Task Swagger_IsAvailable()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Response DTOs
    private record OrderCreatedResponse(string OrderId, decimal TotalAmount, DateTime CreatedAt);
    private record OrderResponse(string OrderId, string CustomerId, decimal TotalAmount, int Status);
}

/// <summary>
/// WebApplicationFactory for OrderSystem.Api tests.
/// </summary>
public class OrderSystemWebApplicationFactory : WebApplicationFactory<OrderSystem.Api.Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            context.HostingEnvironment.EnvironmentName = "Development";
        });
    }
}
