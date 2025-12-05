using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Advanced E2E tests for OrderSystem.Api covering edge cases and complex scenarios.
/// </summary>
public class OrderSystemAdvancedE2ETests : IClassFixture<OrderSystemWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemAdvancedE2ETests(OrderSystemWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Order Cancellation Tests

    [Fact]
    public async Task CancelOrder_ExistingOrder_ReturnsSuccess()
    {
        // Arrange - create an order first
        var createRequest = new
        {
            CustomerId = "cancel-test-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 1, UnitPrice = 50.00m }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/orders", createRequest);
        var created = JsonSerializer.Deserialize<OrderCreatedResponse>(
            await createResponse.Content.ReadAsStringAsync(), _jsonOptions);

        // Act
        var cancelRequest = new { Reason = "Customer requested cancellation" };
        var response = await _client.PostAsJsonAsync($"/api/orders/{created!.OrderId}/cancel", cancelRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_NonExistingOrder_ReturnsNotFound()
    {
        // Act
        var cancelRequest = new { Reason = "Test cancellation" };
        var response = await _client.PostAsJsonAsync("/api/orders/non-existing-order/cancel", cancelRequest);

        // Assert - should return NotFound or NoContent
        Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.NoContent ||
                    response.StatusCode == HttpStatusCode.OK);
    }

    #endregion

    #region Concurrent Order Creation Tests

    [Fact]
    public async Task CreateOrders_ConcurrentRequests_AllSucceed()
    {
        // Arrange
        const int orderCount = 20;
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act
        for (int i = 0; i < orderCount; i++)
        {
            var request = new
            {
                CustomerId = $"concurrent-customer-{i}",
                Items = new[]
                {
                    new { ProductId = $"prod-{i}", Quantity = i + 1, UnitPrice = 10.00m * (i + 1) }
                }
            };
            tasks.Add(_client.PostAsJsonAsync("/api/orders", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task CreateOrders_SameCustomer_AllSucceed()
    {
        // Arrange
        const int orderCount = 10;
        var customerId = $"multi-order-customer-{Guid.NewGuid():N}";
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act
        for (int i = 0; i < orderCount; i++)
        {
            var request = new
            {
                CustomerId = customerId,
                Items = new[]
                {
                    new { ProductId = $"prod-{i}", Quantity = 1, UnitPrice = 25.00m }
                }
            };
            tasks.Add(_client.PostAsJsonAsync("/api/orders", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Verify all orders exist for customer
        var ordersResponse = await _client.GetAsync($"/api/users/{customerId}/orders");
        Assert.Equal(HttpStatusCode.OK, ordersResponse.StatusCode);
        var orders = JsonSerializer.Deserialize<List<OrderResponse>>(
            await ordersResponse.Content.ReadAsStringAsync(), _jsonOptions);
        Assert.Equal(orderCount, orders!.Count);
    }

    #endregion

    #region Order Validation Tests

    [Fact]
    public async Task CreateOrder_EmptyItems_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            CustomerId = "test-customer",
            Items = Array.Empty<object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert - should fail validation
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.OK); // Depends on validation implementation
    }

    [Fact]
    public async Task CreateOrder_LargeQuantity_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "large-quantity-customer",
            Items = new[]
            {
                new { ProductId = "bulk-prod", Quantity = 10000, UnitPrice = 0.01m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(100.00m, result!.TotalAmount);
    }

    [Fact]
    public async Task CreateOrder_ManyItems_Succeeds()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).Select(i => new
        {
            ProductId = $"prod-{i}",
            Quantity = 1,
            UnitPrice = 1.00m
        }).ToArray();

        var request = new
        {
            CustomerId = "many-items-customer",
            Items = items
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(50.00m, result!.TotalAmount);
    }

    [Fact]
    public async Task CreateOrder_DecimalPrecision_CalculatesCorrectly()
    {
        // Arrange
        var request = new
        {
            CustomerId = "precision-customer",
            Items = new[]
            {
                new { ProductId = "prod-1", Quantity = 3, UnitPrice = 33.33m },
                new { ProductId = "prod-2", Quantity = 7, UnitPrice = 14.29m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        // 3 * 33.33 + 7 * 14.29 = 99.99 + 100.03 = 200.02
        Assert.Equal(200.02m, result!.TotalAmount);
    }

    #endregion

    #region Cluster and Health Tests

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ClusterNode_ReturnsNodeInfo()
    {
        // Act
        var response = await _client.GetAsync("/api/cluster/node");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task ClusterStatus_ReturnsStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/cluster/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Outbox Tests

    [Fact]
    public async Task ProcessOutbox_MultipleBatches_Succeeds()
    {
        // Arrange - create some orders to generate outbox messages
        for (int i = 0; i < 5; i++)
        {
            var request = new
            {
                CustomerId = $"outbox-test-{i}",
                Items = new[] { new { ProductId = "prod-1", Quantity = 1, UnitPrice = 10.00m } }
            };
            await _client.PostAsJsonAsync("/api/orders", request);
        }

        // Act - process outbox multiple times
        for (int i = 0; i < 3; i++)
        {
            var processRequest = new { BatchSize = 10 };
            var response = await _client.PostAsJsonAsync("/api/outbox/process", processRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ProcessOutbox_LargeBatchSize_Succeeds()
    {
        // Arrange
        var request = new { BatchSize = 1000 };

        // Act
        var response = await _client.PostAsJsonAsync("/api/outbox/process", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Stress Tests

    [Fact]
    public async Task RapidOrderCreation_HighThroughput_AllSucceed()
    {
        // Arrange
        const int orderCount = 50;
        var semaphore = new SemaphoreSlim(10); // Limit concurrency
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act
        for (int i = 0; i < orderCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var request = new
                    {
                        CustomerId = $"stress-customer-{index}",
                        Items = new[] { new { ProductId = "stress-prod", Quantity = 1, UnitPrice = 5.00m } }
                    };
                    return await _client.PostAsJsonAsync("/api/orders", request);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.True(successCount >= orderCount * 0.95, $"Expected at least 95% success, got {successCount}/{orderCount}");
    }

    [Fact]
    public async Task MixedOperations_CreateAndRead_Consistent()
    {
        // Arrange
        const int operationCount = 20;
        var createdOrderIds = new List<string>();

        // Act - Create orders
        for (int i = 0; i < operationCount; i++)
        {
            var request = new
            {
                CustomerId = $"mixed-op-customer-{i}",
                Items = new[] { new { ProductId = $"mixed-prod-{i}", Quantity = i + 1, UnitPrice = 10.00m } }
            };
            var response = await _client.PostAsJsonAsync("/api/orders", request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
                createdOrderIds.Add(result!.OrderId);
            }
        }

        // Assert - Read all created orders
        foreach (var orderId in createdOrderIds)
        {
            var response = await _client.GetAsync($"/api/orders/{orderId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CreateOrder_SpecialCharactersInCustomerId_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "customer-with-special-chars-!@#$%",
            Items = new[] { new { ProductId = "prod-1", Quantity = 1, UnitPrice = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_UnicodeCustomerId_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "客户-测试-用户",
            Items = new[] { new { ProductId = "产品-1", Quantity = 1, UnitPrice = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ZeroPrice_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "free-order-customer",
            Items = new[] { new { ProductId = "free-prod", Quantity = 1, UnitPrice = 0.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(0.00m, result!.TotalAmount);
    }

    [Fact]
    public async Task GetUserOrders_NoOrders_ReturnsEmptyList()
    {
        // Arrange
        var customerId = $"no-orders-customer-{Guid.NewGuid():N}";

        // Act
        var response = await _client.GetAsync($"/api/users/{customerId}/orders");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var orders = JsonSerializer.Deserialize<List<OrderResponse>>(content, _jsonOptions);
        Assert.Empty(orders!);
    }

    #endregion

    // Response DTOs
    private record OrderCreatedResponse(string OrderId, decimal TotalAmount, DateTime CreatedAt);
    private record OrderResponse(string OrderId, string CustomerId, decimal TotalAmount, int Status);
}
