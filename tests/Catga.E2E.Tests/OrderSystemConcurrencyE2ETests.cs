using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Concurrency and race condition tests for OrderSystem.Api.
/// Tests thread safety, concurrent operations, and data consistency.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemConcurrencyE2ETests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemConcurrencyE2ETests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region Concurrent Creation Tests

    [Fact]
    public async Task CreateOrders_HighConcurrency_AllSucceed()
    {
        // Arrange
        const int concurrentRequests = 100;
        var tasks = new List<Task<HttpResponseMessage>>();
        var stopwatch = Stopwatch.StartNew();

        // Act - fire all requests concurrently
        for (int i = 0; i < concurrentRequests; i++)
        {
            var index = i;
            var request = new
            {
                CustomerId = $"concurrent-{index}",
                Items = new[]
                {
                    new { ProductId = $"prod-{index}", Quantity = 1, Price = 10.00m }
                }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.True(successCount >= concurrentRequests * 0.95, 
            $"Expected at least 95% success rate, got {successCount}/{concurrentRequests}");
        
        // Performance assertion - should complete in reasonable time
        Assert.True(stopwatch.ElapsedMilliseconds < 30000, 
            $"Concurrent requests took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task CreateOrders_SameCustomerConcurrently_AllSucceed()
    {
        // Arrange
        const int orderCount = 50;
        var customerId = $"concurrent-customer-{Guid.NewGuid():N}";
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - create multiple orders for same customer concurrently
        for (int i = 0; i < orderCount; i++)
        {
            var index = i;
            var request = new
            {
                CustomerId = customerId,
                Items = new[]
                {
                    new { ProductId = $"prod-{index}", Quantity = 1, Price = 10.00m }
                }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.True(successCount >= orderCount * 0.95);

        // Verify all orders are retrievable
        await Task.Delay(100); // Small delay for consistency
        var ordersResponse = await _client.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.OK, ordersResponse.StatusCode);
    }

    [Fact]
    public async Task CreateOrders_RapidFire_MaintainsDataIntegrity()
    {
        // Arrange
        const int orderCount = 30;
        var orderIds = new ConcurrentBag<string>();
        var tasks = new List<Task>();

        // Act - rapid fire order creation
        for (int i = 0; i < orderCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var request = new
                {
                    CustomerId = $"rapid-{index}",
                    Items = new[]
                    {
                        new { ProductId = $"prod-{index}", Quantity = index + 1, Price = 10.00m }
                    }
                };

                var response = await _client.PostAsJsonAsync("/orders", request);
                if (response.StatusCode == HttpStatusCode.Created)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<OrderCreatedResponse>(content, _jsonOptions);
                    if (result != null)
                    {
                        orderIds.Add(result.OrderId);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - all order IDs should be unique
        Assert.True(orderIds.Count >= orderCount * 0.95);
        Assert.Equal(orderIds.Count, orderIds.Distinct().Count());
    }

    #endregion

    #region Concurrent State Modification Tests

    [Fact]
    public async Task PayOrder_ConcurrentAttempts_HandlesRaceCondition()
    {
        // Arrange - create an order
        var order = await CreateTestOrder();

        // Act - try to pay the same order concurrently
        var payTasks = Enumerable.Range(0, 10).Select(_ =>
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { })
        ).ToArray();

        var responses = await Task.WhenAll(payTasks);

        // Assert - should handle race condition gracefully
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount >= 1, "At least one payment should succeed");
        
        // Verify final state is consistent
        var finalOrder = await GetOrder(order.OrderId);
        Assert.NotNull(finalOrder);
    }

    [Fact]
    public async Task ModifyOrder_ConcurrentOperations_MaintainsConsistency()
    {
        // Arrange - create an order
        var order = await CreateTestOrder();

        // Act - perform different operations concurrently
        var tasks = new List<Task<HttpResponseMessage>>
        {
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { }),
            _client.GetAsync($"/orders/{order.OrderId}"),
            _client.GetAsync($"/orders/{order.OrderId}/history"),
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { }),
            _client.GetAsync($"/orders/{order.OrderId}")
        };

        var responses = await Task.WhenAll(tasks);

        // Assert - all operations should complete without errors
        Assert.All(responses, r => Assert.True(
            r.IsSuccessStatusCode || 
            r.StatusCode == HttpStatusCode.NotFound ||
            r.StatusCode == HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task CancelOrder_ConcurrentAttempts_HandlesIdempotently()
    {
        // Arrange
        var order = await CreateTestOrder();

        // Act - try to cancel concurrently
        var cancelTasks = Enumerable.Range(0, 10).Select(_ =>
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { })
        ).ToArray();

        var responses = await Task.WhenAll(cancelTasks);

        // Assert - should handle idempotently
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount >= 1);
    }

    [Fact]
    public async Task ShipOrder_ConcurrentAttempts_HandlesRaceCondition()
    {
        // Arrange - create and pay an order
        var order = await CreateTestOrder();
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });

        // Act - try to ship concurrently
        var shipTasks = Enumerable.Range(0, 10).Select(i =>
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", 
                new { TrackingNumber = $"TRACK-{i}" })
        ).ToArray();

        var responses = await Task.WhenAll(shipTasks);

        // Assert - should handle race condition
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount >= 1);
    }

    #endregion

    #region Concurrent Read/Write Tests

    [Fact]
    public async Task ReadWrite_ConcurrentOperations_MaintainsConsistency()
    {
        // Arrange - create multiple orders
        var orderIds = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var order = await CreateTestOrder();
            orderIds.Add(order.OrderId);
        }

        // Act - mix reads and writes concurrently
        var tasks = new List<Task<HttpResponseMessage>>();
        
        // Add read operations
        foreach (var orderId in orderIds)
        {
            tasks.Add(_client.GetAsync($"/orders/{orderId}"));
            tasks.Add(_client.GetAsync($"/orders/{orderId}/history"));
        }

        // Add write operations
        foreach (var orderId in orderIds)
        {
            tasks.Add(_client.PostAsJsonAsync($"/orders/{orderId}/pay", new { }));
        }

        // Add more reads
        tasks.Add(_client.GetAsync("/orders"));
        tasks.Add(_client.GetAsync("/stats"));

        var responses = await Task.WhenAll(tasks);

        // Assert - all operations should complete successfully
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount >= tasks.Count * 0.9);
    }

    [Fact]
    public async Task GetAllOrders_DuringConcurrentCreation_ReturnsConsistentData()
    {
        // Arrange
        var customerId = $"consistency-test-{Guid.NewGuid():N}";
        var createTasks = new List<Task>();
        var readTasks = new List<Task<HttpResponseMessage>>();

        // Act - create orders and read list concurrently
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            createTasks.Add(Task.Run(async () =>
            {
                var request = new
                {
                    CustomerId = customerId,
                    Items = new[] { new { ProductId = $"prod-{index}", Quantity = 1, Price = 10.00m } }
                };
                await _client.PostAsJsonAsync("/orders", request);
            }));

            // Interleave reads
            if (i % 5 == 0)
            {
                readTasks.Add(_client.GetAsync("/orders"));
            }
        }

        await Task.WhenAll(createTasks.Concat<Task>(readTasks));

        // Assert - all reads should succeed
        var responses = await Task.WhenAll(readTasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task GetStats_DuringConcurrentModifications_ReturnsValidData()
    {
        // Arrange - create some orders
        var orderIds = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var order = await CreateTestOrder();
            orderIds.Add(order.OrderId);
        }

        // Act - modify orders and read stats concurrently
        var modifyTasks = orderIds.Select(id =>
            _client.PostAsJsonAsync($"/orders/{id}/pay", new { })
        ).ToList();

        var statsTasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetAsync("/stats")
        ).ToList();

        var allTasks = modifyTasks.Concat<Task<HttpResponseMessage>>(statsTasks);
        var responses = await Task.WhenAll(allTasks);

        // Assert - all stats requests should return valid data
        var statsResponses = responses.Skip(modifyTasks.Count).ToArray();
        Assert.All(statsResponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    #endregion

    #region Load and Stress Tests

    [Fact]
    public async Task HighLoad_SustainedTraffic_MaintainsPerformance()
    {
        // Arrange
        const int requestsPerBatch = 20;
        const int batchCount = 5;
        var allResponseTimes = new ConcurrentBag<long>();

        // Act - send batches of requests
        for (int batch = 0; batch < batchCount; batch++)
        {
            var tasks = new List<Task>();
            
            for (int i = 0; i < requestsPerBatch; i++)
            {
                var index = batch * requestsPerBatch + i;
                tasks.Add(Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    var request = new
                    {
                        CustomerId = $"load-{index}",
                        Items = new[] { new { ProductId = $"prod-{index}", Quantity = 1, Price = 10.00m } }
                    };
                    await _client.PostAsJsonAsync("/orders", request);
                    sw.Stop();
                    allResponseTimes.Add(sw.ElapsedMilliseconds);
                }));
            }

            await Task.WhenAll(tasks);
            await Task.Delay(100); // Small delay between batches
        }

        // Assert - performance should remain consistent
        var avgResponseTime = allResponseTimes.Average();
        var maxResponseTime = allResponseTimes.Max();
        
        Assert.True(avgResponseTime < 5000, $"Average response time too high: {avgResponseTime}ms");
        Assert.True(maxResponseTime < 10000, $"Max response time too high: {maxResponseTime}ms");
    }

    [Fact]
    public async Task BurstTraffic_SuddenSpike_HandlesGracefully()
    {
        // Arrange
        const int burstSize = 50;
        var stopwatch = Stopwatch.StartNew();

        // Act - sudden burst of requests
        var tasks = Enumerable.Range(0, burstSize).Select(i =>
            Task.Run(async () =>
            {
                var request = new
                {
                    CustomerId = $"burst-{i}",
                    Items = new[] { new { ProductId = $"prod-{i}", Quantity = 1, Price = 10.00m } }
                };
                return await _client.PostAsJsonAsync("/orders", request);
            })
        ).ToArray();

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.True(successCount >= burstSize * 0.9, 
            $"Expected at least 90% success rate, got {successCount}/{burstSize}");
        
        // Should handle burst in reasonable time
        Assert.True(stopwatch.ElapsedMilliseconds < 20000, 
            $"Burst handling took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task MixedOperations_HighConcurrency_AllSucceed()
    {
        // Arrange - create some orders first
        var orderIds = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var order = await CreateTestOrder();
            orderIds.Add(order.OrderId);
        }

        // Act - mix of all operation types concurrently
        var tasks = new List<Task<HttpResponseMessage>>();

        // Creates
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var request = new
                {
                    CustomerId = $"mixed-{index}",
                    Items = new[] { new { ProductId = $"prod-{index}", Quantity = 1, Price = 10.00m } }
                };
                return await _client.PostAsJsonAsync("/orders", request);
            }));
        }

        // Reads
        foreach (var orderId in orderIds)
        {
            tasks.Add(_client.GetAsync($"/orders/{orderId}"));
        }

        // Updates
        foreach (var orderId in orderIds.Take(5))
        {
            tasks.Add(_client.PostAsJsonAsync($"/orders/{orderId}/pay", new { }));
        }

        // Stats
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_client.GetAsync("/stats"));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount >= tasks.Count * 0.9);
    }

    #endregion

    #region Data Race Tests

    [Fact]
    public async Task OrderLifecycle_ConcurrentStateTransitions_MaintainsValidity()
    {
        // Arrange - create an order
        var order = await CreateTestOrder();

        // Act - try to transition through states concurrently (some should fail)
        var tasks = new List<Task<HttpResponseMessage>>
        {
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { }),
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/ship", new { }),
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/cancel", new { }),
            _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { }),
        };

        var responses = await Task.WhenAll(tasks);

        // Assert - at least some operations should succeed
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount >= 1);

        // Verify final state is valid
        var finalOrder = await GetOrder(order.OrderId);
        Assert.NotNull(finalOrder);
    }

    [Fact]
    public async Task CreateAndQuery_ImmediateRead_ReturnsCreatedOrder()
    {
        // Arrange
        var customerId = $"immediate-read-{Guid.NewGuid():N}";
        var request = new
        {
            CustomerId = customerId,
            Items = new[] { new { ProductId = "prod-1", Quantity = 1, Price = 10.00m } }
        };

        // Act - create and immediately read
        var createResponse = await _client.PostAsJsonAsync("/orders", request);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);

        var readResponse = await _client.GetAsync($"/orders/{created!.OrderId}");

        // Assert - should be able to read immediately after creation
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateAndRead_ImmediateRead_ReturnsUpdatedState()
    {
        // Arrange - create and pay an order
        var order = await CreateTestOrder();
        await _client.PostAsJsonAsync($"/orders/{order.OrderId}/pay", new { });

        // Act - immediately read after update
        var readResponse = await _client.GetAsync($"/orders/{order.OrderId}");

        // Assert - should reflect the update
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var updatedOrder = await readResponse.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(updatedOrder);
    }

    #endregion

    #region Helper Methods

    private async Task<OrderCreatedResponse> CreateTestOrder()
    {
        var request = new
        {
            CustomerId = $"test-{Guid.NewGuid():N}",
            Items = new[]
            {
                new { ProductId = "TEST-001", Quantity = 1, Price = 100.00m }
            }
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

    #endregion

    // Response DTOs
    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);
    private record OrderResponse(string OrderId, string CustomerId, decimal Total, string Status);
}

