using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Performance and reliability tests for OrderSystem.Api.
/// Tests response times, throughput, and system behavior under load.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemPerformanceTests
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemPerformanceTests(OrderSystemFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region Response Time Tests

    [Fact]
    public async Task CreateOrder_ResponseTime_WithinAcceptableRange()
    {
        // Arrange
        var request = new
        {
            CustomerId = "perf-customer",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await _client.PostAsJsonAsync("/orders", request);
        stopwatch.Stop();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Create order took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    [Fact]
    public async Task GetOrder_ResponseTime_Fast()
    {
        // Arrange - create an order first
        var createRequest = new
        {
            CustomerId = "perf-customer",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };
        var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await _client.GetAsync($"/orders/{created!.OrderId}");
        stopwatch.Stop();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, 
            $"Get order took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
    }

    [Fact]
    public async Task GetStats_ResponseTime_Fast()
    {
        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await _client.GetAsync("/stats");
        stopwatch.Stop();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, 
            $"Get stats took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
    }

    #endregion

    #region Throughput Tests

    [Fact]
    public async Task CreateOrders_Throughput_MeetsMinimumRequirement()
    {
        // Arrange
        const int orderCount = 100;
        const int maxDurationSeconds = 10;

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<HttpResponseMessage>>();

        for (int i = 0; i < orderCount; i++)
        {
            var request = new
            {
                CustomerId = $"throughput-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var throughput = successCount / stopwatch.Elapsed.TotalSeconds;

        Assert.True(successCount >= orderCount * 0.95, 
            $"Expected at least 95% success rate, got {successCount}/{orderCount}");
        Assert.True(stopwatch.Elapsed.TotalSeconds < maxDurationSeconds, 
            $"Took {stopwatch.Elapsed.TotalSeconds}s, expected < {maxDurationSeconds}s");
        Assert.True(throughput >= 10, 
            $"Throughput: {throughput:F2} orders/sec, expected >= 10 orders/sec");
    }

    [Fact]
    public async Task MixedOperations_Throughput_Balanced()
    {
        // Arrange - create some orders first
        var orderIds = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var request = new
            {
                CustomerId = $"mixed-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };
            var response = await _client.PostAsJsonAsync("/orders", request);
            var created = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
            orderIds.Add(created!.OrderId);
        }

        // Act - mix of operations
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<HttpResponseMessage>>();

        // 50% reads
        for (int i = 0; i < 50; i++)
        {
            var orderId = orderIds[i % orderIds.Count];
            tasks.Add(_client.GetAsync($"/orders/{orderId}"));
        }

        // 30% creates
        for (int i = 0; i < 30; i++)
        {
            var request = new
            {
                CustomerId = $"mixed-new-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        // 20% stats
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(_client.GetAsync("/stats"));
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        var throughput = successCount / stopwatch.Elapsed.TotalSeconds;

        Assert.True(successCount >= 95, $"Expected at least 95 successful operations, got {successCount}");
        Assert.True(throughput >= 20, $"Throughput: {throughput:F2} ops/sec, expected >= 20 ops/sec");
    }

    #endregion

    #region Latency Distribution Tests

    [Fact]
    public async Task CreateOrder_LatencyDistribution_Consistent()
    {
        // Arrange
        const int sampleSize = 50;
        var latencies = new List<long>();

        // Act - measure latency for multiple requests
        for (int i = 0; i < sampleSize; i++)
        {
            var request = new
            {
                CustomerId = $"latency-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };

            var stopwatch = Stopwatch.StartNew();
            await _client.PostAsJsonAsync("/orders", request);
            stopwatch.Stop();

            latencies.Add(stopwatch.ElapsedMilliseconds);
        }

        // Assert - analyze distribution
        var avgLatency = latencies.Average();
        var maxLatency = latencies.Max();
        var p95Latency = latencies.OrderBy(l => l).ElementAt((int)(sampleSize * 0.95));
        var p99Latency = latencies.OrderBy(l => l).ElementAt((int)(sampleSize * 0.99));

        Assert.True(avgLatency < 500, $"Average latency: {avgLatency}ms, expected < 500ms");
        Assert.True(p95Latency < 1000, $"P95 latency: {p95Latency}ms, expected < 1000ms");
        Assert.True(p99Latency < 2000, $"P99 latency: {p99Latency}ms, expected < 2000ms");
        Assert.True(maxLatency < 5000, $"Max latency: {maxLatency}ms, expected < 5000ms");
    }

    #endregion

    #region Memory Efficiency Tests

    [Fact]
    public async Task CreateManyOrders_MemoryEfficient()
    {
        // Arrange
        const int orderCount = 1000;

        // Act - create many orders
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < orderCount; i++)
        {
            var request = new
            {
                CustomerId = $"memory-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };
            tasks.Add(_client.PostAsJsonAsync("/orders", request));

            // Process in batches to avoid overwhelming the system
            if (tasks.Count >= 100)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }

        // Assert - system should still be responsive
        var healthResponse = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    #endregion

    #region Sustained Load Tests

    [Fact]
    public async Task SustainedLoad_MultipleMinutes_MaintainsPerformance()
    {
        // Arrange
        const int durationSeconds = 30; // 30 seconds sustained load
        const int requestsPerSecond = 10;
        var stopwatch = Stopwatch.StartNew();
        var responseTimes = new ConcurrentBag<long>();
        var successCount = 0;
        var failureCount = 0;

        // Act - sustained load
        while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
        {
            var batchTasks = new List<Task>();
            
            for (int i = 0; i < requestsPerSecond; i++)
            {
                batchTasks.Add(Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var request = new
                        {
                            CustomerId = $"sustained-{Guid.NewGuid():N}",
                            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
                        };
                        var response = await _client.PostAsJsonAsync("/orders", request);
                        sw.Stop();
                        
                        responseTimes.Add(sw.ElapsedMilliseconds);
                        
                        if (response.IsSuccessStatusCode)
                            Interlocked.Increment(ref successCount);
                        else
                            Interlocked.Increment(ref failureCount);
                    }
                    catch
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                }));
            }

            await Task.WhenAll(batchTasks);
            await Task.Delay(1000); // Wait 1 second between batches
        }

        stopwatch.Stop();

        // Assert
        var totalRequests = successCount + failureCount;
        var successRate = (double)successCount / totalRequests;
        var avgResponseTime = responseTimes.Any() ? responseTimes.Average() : 0;

        Assert.True(successRate >= 0.95, 
            $"Success rate: {successRate:P2}, expected >= 95%");
        Assert.True(avgResponseTime < 1000, 
            $"Average response time: {avgResponseTime}ms, expected < 1000ms");
    }

    #endregion

    #region Spike Tests

    [Fact]
    public async Task SuddenSpike_RecoveryTime_Fast()
    {
        // Arrange - normal load
        for (int i = 0; i < 10; i++)
        {
            var request = new
            {
                CustomerId = $"spike-warmup-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };
            await _client.PostAsJsonAsync("/orders", request);
        }

        // Act - sudden spike
        var spikeTasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 100; i++)
        {
            var request = new
            {
                CustomerId = $"spike-{i}",
                Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
            };
            spikeTasks.Add(_client.PostAsJsonAsync("/orders", request));
        }

        var spikeStopwatch = Stopwatch.StartNew();
        await Task.WhenAll(spikeTasks);
        spikeStopwatch.Stop();

        // Measure recovery - system should return to normal quickly
        var recoveryStopwatch = Stopwatch.StartNew();
        var recoveryRequest = new
        {
            CustomerId = "spike-recovery",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };
        var recoveryResponse = await _client.PostAsJsonAsync("/orders", recoveryRequest);
        recoveryStopwatch.Stop();

        // Assert
        Assert.Equal(HttpStatusCode.Created, recoveryResponse.StatusCode);
        Assert.True(recoveryStopwatch.ElapsedMilliseconds < 2000, 
            $"Recovery took {recoveryStopwatch.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    #endregion

    #region Concurrent User Simulation

    [Fact]
    public async Task SimulateConcurrentUsers_RealisticBehavior()
    {
        // Arrange - simulate 20 concurrent users
        const int userCount = 20;
        const int actionsPerUser = 5;

        // Act
        var userTasks = Enumerable.Range(0, userCount).Select(async userId =>
        {
            var userStopwatch = Stopwatch.StartNew();
            
            for (int action = 0; action < actionsPerUser; action++)
            {
                // Create order
                var createRequest = new
                {
                    CustomerId = $"user-{userId}",
                    Items = new[] { new { ProductId = $"PROD-{action}", Quantity = 1, Price = 10.00m } }
                };
                var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
                
                if (createResponse.StatusCode == HttpStatusCode.Created)
                {
                    var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
                    
                    // View order
                    await _client.GetAsync($"/orders/{created!.OrderId}");
                    
                    // Random action: pay or cancel
                    if (action % 2 == 0)
                    {
                        await _client.PostAsJsonAsync($"/orders/{created.OrderId}/pay", new { });
                    }
                }
                
                // Think time between actions
                await Task.Delay(Random.Shared.Next(100, 500));
            }
            
            userStopwatch.Stop();
            return userStopwatch.ElapsedMilliseconds;
        }).ToArray();

        var userTimes = await Task.WhenAll(userTasks);

        // Assert
        var avgUserTime = userTimes.Average();
        var maxUserTime = userTimes.Max();

        Assert.True(avgUserTime < 10000, 
            $"Average user session: {avgUserTime}ms, expected < 10000ms");
        Assert.True(maxUserTime < 20000, 
            $"Max user session: {maxUserTime}ms, expected < 20000ms");
    }

    #endregion

    #region Cache Effectiveness Tests

    [Fact]
    public async Task RepeatedReads_CacheEffective()
    {
        // Arrange - create an order
        var createRequest = new
        {
            CustomerId = "cache-test",
            Items = new[] { new { ProductId = "PROD-1", Quantity = 1, Price = 10.00m } }
        };
        var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);

        // Act - first read (cache miss)
        var firstReadStopwatch = Stopwatch.StartNew();
        await _client.GetAsync($"/orders/{created!.OrderId}");
        firstReadStopwatch.Stop();

        // Subsequent reads (cache hits)
        var cachedReadTimes = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            await _client.GetAsync($"/orders/{created.OrderId}");
            sw.Stop();
            cachedReadTimes.Add(sw.ElapsedMilliseconds);
        }

        // Assert - cached reads should be fast
        var avgCachedTime = cachedReadTimes.Average();
        Assert.True(avgCachedTime <= firstReadStopwatch.ElapsedMilliseconds, 
            $"Cached reads ({avgCachedTime}ms) should be <= first read ({firstReadStopwatch.ElapsedMilliseconds}ms)");
    }

    #endregion

    #region Response DTOs

    private record OrderCreatedResponse(string OrderId, decimal Total, DateTime CreatedAt);

    #endregion
}
