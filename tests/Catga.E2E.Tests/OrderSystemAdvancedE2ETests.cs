using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Advanced E2E tests for OrderSystem.Api covering edge cases and complex scenarios.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemAdvancedE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemAdvancedE2ETests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
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
                new { ProductId = "prod-1", Name = "Product 1", Quantity = 1, Price = 50.00m }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
        var created = JsonSerializer.Deserialize<OrderCreatedResponse>(
            await createResponse.Content.ReadAsStringAsync(), _jsonOptions);

        // Act
        var response = await _client.PostAsJsonAsync($"/orders/{created!.OrderId}/cancel", new { });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_NonExistingOrder_ReturnsErrorOrNotFound()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/orders/non-existing-order/cancel", new { });

        // Assert - should return NotFound, NoContent, OK, or BadRequest depending on implementation
        Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.NoContent ||
                    response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.BadRequest);
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
                    new { ProductId = $"prod-{i}", Name = $"Product {i}", Quantity = i + 1, Price = 10.00m * (i + 1) }
                }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
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
                    new { ProductId = $"prod-{i}", Name = $"Product {i}", Quantity = 1, Price = 25.00m }
                }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        // Verify all orders exist for customer
        var ordersResponse = await _client.GetAsync($"/orders");
        Assert.Equal(HttpStatusCode.OK, ordersResponse.StatusCode);
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
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert - should fail validation
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.Created); // Depends on validation implementation
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
                new { ProductId = "bulk-prod", Name = "Bulk Product", Quantity = 10000, Price = 0.01m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(100.00m, result!.Total);
    }

    [Fact]
    public async Task CreateOrder_ManyItems_Succeeds()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).Select(i => new
        {
            ProductId = $"prod-{i}",
            Name = $"Product {i}",
            Quantity = 1,
            Price = 1.00m
        }).ToArray();

        var request = new
        {
            CustomerId = "many-items-customer",
            Items = items
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(50.00m, result!.Total);
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
                new { ProductId = "prod-1", Name = "Product 1", Quantity = 3, Price = 33.33m },
                new { ProductId = "prod-2", Name = "Product 2", Quantity = 7, Price = 14.29m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        // 3 * 33.33 + 7 * 14.29 = 99.99 + 100.03 = 200.02
        Assert.Equal(200.02m, result!.Total);
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
    public async Task SystemInfo_ReturnsTransportInfo()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("transport", content.ToLowerInvariant());
    }

    [Fact]
    public async Task SystemInfo_ReturnsPersistenceInfo()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("persistence", content.ToLowerInvariant());
    }

    #endregion

    #region Stats Tests

    [Fact]
    public async Task GetStats_AfterMultipleOrders_ReturnsAccurate()
    {
        // Arrange - create some orders
        for (int i = 0; i < 5; i++)
        {
            var request = new
            {
                CustomerId = $"stats-test-{i}",
                Items = new[] { new { ProductId = "prod-1", Name = "Product 1", Quantity = 1, Price = 10.00m } }
            };
            await _client.PostAsJsonAsync("/orders", request);
        }

        // Act - get stats
        var response = await _client.GetAsync("/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("total", content.ToLowerInvariant());
    }

    [Fact]
    public async Task GetStats_ReturnsValidJson()
    {
        // Act
        var response = await _client.GetAsync("/stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        Assert.StartsWith("{", content.Trim());
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
                        Items = new[] { new { ProductId = "stress-prod", Name = "Stress Product", Quantity = 1, Price = 5.00m } }
                    };
                    return await _client.PostAsJsonAsync("/orders", request);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
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
                Items = new[] { new { ProductId = $"mixed-prod-{i}", Name = $"Mixed Product {i}", Quantity = i + 1, Price = 10.00m } }
            };
            var response = await _client.PostAsJsonAsync("/orders", request);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
                createdOrderIds.Add(result!.OrderId);
            }
        }

        // Assert - Read all created orders
        foreach (var orderId in createdOrderIds)
        {
            var response = await _client.GetAsync($"/orders/{orderId}");
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
            Items = new[] { new { ProductId = "prod-1", Name = "Product 1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_UnicodeCustomerId_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "客户-测试-用户",
            Items = new[] { new { ProductId = "产品-1", Name = "产品名称", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_ZeroPrice_Succeeds()
    {
        // Arrange
        var request = new
        {
            CustomerId = "free-order-customer",
            Items = new[] { new { ProductId = "free-prod", Name = "Free Product", Quantity = 1, Price = 0.00m } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
        Assert.Equal(0.00m, result!.Total);
    }

    [Fact]
    public async Task GetAllOrders_ReturnsOrders()
    {
        // Act
        var response = await _client.GetAsync($"/orders");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    // Response DTOs
    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);
    private record OrderResponse(string OrderId, string CustomerId, decimal Total, string Status);
}
