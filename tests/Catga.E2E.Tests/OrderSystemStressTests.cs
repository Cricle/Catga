using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Catga.E2E.Tests;

/// <summary>
/// Stress and load tests for OrderSystem.Api
/// These tests require a running instance and may have timing issues in CI.
/// </summary>
[Collection("OrderSystem")]
public class OrderSystemStressTests
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderSystemStressTests(OrderSystemFixture factory, ITestOutputHelper output)
    {
        _client = factory.CreateClient();
        _output = output;
    }

    [Theory]
    [InlineData(5, 20)]    // Light load for CI
    [InlineData(10, 50)]   // Medium load
    [Trait("Category", "Stress")]
    public async Task ConcurrentOrderCreation_AllSucceed(int concurrency, int totalRequests)
    {
        var results = new ConcurrentBag<(bool Success, long LatencyMs)>();
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrency).Select(async workerId =>
        {
            var requestsPerWorker = totalRequests / concurrency;
            for (int i = 0; i < requestsPerWorker; i++)
            {
                var reqSw = Stopwatch.StartNew();
                try
                {
                    var request = new
                    {
                        CustomerId = $"stress-{workerId}-{i}",
                        Items = new[]
                        {
                            new { ProductId = "STRESS-001", ProductName = "Stress Product", Quantity = 1, UnitPrice = 10.00m }
                        }
                    };

                    var response = await _client.PostAsJsonAsync("/orders", request);
                    reqSw.Stop();
                    results.Add((response.IsSuccessStatusCode, reqSw.ElapsedMilliseconds));
                }
                catch
                {
                    reqSw.Stop();
                    results.Add((false, reqSw.ElapsedMilliseconds));
                }
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        // Calculate metrics
        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);
        var successRate = (double)successCount / results.Count * 100;
        var rps = results.Count / sw.Elapsed.TotalSeconds;
        var latencies = results.Select(r => r.LatencyMs).OrderBy(l => l).ToList();
        var avgLatency = latencies.Average();
        var p50Latency = latencies[latencies.Count / 2];
        var p95Latency = latencies[(int)(latencies.Count * 0.95)];
        var p99Latency = latencies[(int)(latencies.Count * 0.99)];

        _output.WriteLine($"Stress Test Results ({concurrency} concurrent, {totalRequests} requests):");
        _output.WriteLine($"  Success: {successCount}, Failed: {failCount}");
        _output.WriteLine($"  Success Rate: {successRate:F2}%");
        _output.WriteLine($"  RPS: {rps:F2}");
        _output.WriteLine($"  Latency - Avg: {avgLatency:F2}ms, P50: {p50Latency}ms, P95: {p95Latency}ms, P99: {p99Latency}ms");

        // Assertions
        Assert.True(successRate >= 95, $"Success rate {successRate:F2}% below 95%");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task ConcurrentReadOperations_HighThroughput()
    {
        // First create some orders
        for (int i = 0; i < 5; i++)
        {
            var request = new
            {
                CustomerId = $"read-test-{i}",
                Items = new[] { new { ProductId = "READ-001", ProductName = "Read Product", Quantity = 1, UnitPrice = 10.00m } }
            };
            await _client.PostAsJsonAsync("/orders", request);
        }

        var concurrency = 5;
        var requestsPerWorker = 10;
        var results = new ConcurrentBag<(bool Success, long LatencyMs)>();
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            for (int i = 0; i < requestsPerWorker; i++)
            {
                var reqSw = Stopwatch.StartNew();
                try
                {
                    var response = await _client.GetAsync("/stats");
                    reqSw.Stop();
                    results.Add((response.IsSuccessStatusCode, reqSw.ElapsedMilliseconds));
                }
                catch
                {
                    reqSw.Stop();
                    results.Add((false, reqSw.ElapsedMilliseconds));
                }
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        var successRate = (double)results.Count(r => r.Success) / results.Count * 100;
        var rps = results.Count / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"Read Stress Test ({concurrency} concurrent, {concurrency * requestsPerWorker} requests):");
        _output.WriteLine($"  Success Rate: {successRate:F2}%");
        _output.WriteLine($"  RPS: {rps:F2}");

        Assert.True(successRate >= 99, "Read success rate should be at least 99%");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task MixedReadWriteOperations_Stable()
    {
        var concurrency = 3;
        var duration = TimeSpan.FromSeconds(2);
        var results = new ConcurrentBag<(string Operation, bool Success, long LatencyMs)>();
        var cts = new CancellationTokenSource(duration);

        var tasks = Enumerable.Range(0, concurrency).Select(async workerId =>
        {
            var orderIds = new List<string>();
            var random = new Random(workerId);

            while (!cts.Token.IsCancellationRequested)
            {
                var reqSw = Stopwatch.StartNew();
                try
                {
                    // 60% reads, 40% writes
                    if (random.NextDouble() < 0.6 && orderIds.Count > 0)
                    {
                        // Read operation
                        var orderId = orderIds[random.Next(orderIds.Count)];
                        var response = await _client.GetAsync($"/orders/{orderId}", cts.Token);
                        reqSw.Stop();
                        results.Add(("Read", response.IsSuccessStatusCode, reqSw.ElapsedMilliseconds));
                    }
                    else
                    {
                        // Write operation
                        var request = new
                        {
                            CustomerId = $"mixed-{workerId}-{Guid.NewGuid():N}",
                            Items = new[] { new { ProductId = "MIX-001", ProductName = "Mixed", Quantity = 1, UnitPrice = 10.00m } }
                        };
                        var response = await _client.PostAsJsonAsync("/orders", request, cts.Token);
                        reqSw.Stop();

                        if (response.IsSuccessStatusCode)
                        {
                            var created = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
                            if (created != null) orderIds.Add(created.OrderId);
                        }
                        results.Add(("Write", response.IsSuccessStatusCode, reqSw.ElapsedMilliseconds));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    reqSw.Stop();
                    results.Add(("Unknown", false, reqSw.ElapsedMilliseconds));
                }
            }
        });

        await Task.WhenAll(tasks);

        var readResults = results.Where(r => r.Operation == "Read").ToList();
        var writeResults = results.Where(r => r.Operation == "Write").ToList();

        _output.WriteLine($"Mixed Load Test ({duration.TotalSeconds}s):");
        _output.WriteLine($"  Total Requests: {results.Count}");
        _output.WriteLine($"  Reads: {readResults.Count} ({readResults.Count(r => r.Success)} success)");
        _output.WriteLine($"  Writes: {writeResults.Count} ({writeResults.Count(r => r.Success)} success)");

        Assert.True(results.Count > 0, "Should complete at least some requests");
        Assert.True((double)results.Count(r => r.Success) / results.Count >= 0.95, "Overall success rate should be >= 95%");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task RapidOrderLifecycle_StressTest()
    {
        var concurrency = 2;
        var ordersPerWorker = 3;
        var results = new ConcurrentBag<(bool Success, long TotalMs)>();

        var tasks = Enumerable.Range(0, concurrency).Select(async workerId =>
        {
            for (int i = 0; i < ordersPerWorker; i++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    // Create
                    var createRequest = new
                    {
                        CustomerId = $"lifecycle-{workerId}-{i}",
                        Items = new[] { new { ProductId = "LIFE-001", ProductName = "Lifecycle", Quantity = 1, UnitPrice = 100.00m } }
                    };
                    var createResponse = await _client.PostAsJsonAsync("/orders", createRequest);
                    if (!createResponse.IsSuccessStatusCode) { results.Add((false, sw.ElapsedMilliseconds)); continue; }

                    var created = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>(_jsonOptions);
                    var orderId = created!.OrderId;

                    // Pay
                    var payRequest = new { PaymentMethod = "Card", TransactionId = $"TXN-{Guid.NewGuid():N}" };
                    var payResponse = await _client.PostAsJsonAsync($"/orders/{orderId}/pay", payRequest);
                    if (!payResponse.IsSuccessStatusCode) { results.Add((false, sw.ElapsedMilliseconds)); continue; }

                    // Ship
                    var shipRequest = new { TrackingNumber = $"TRK-{Guid.NewGuid():N}" };
                    var shipResponse = await _client.PostAsJsonAsync($"/orders/{orderId}/ship", shipRequest);

                    sw.Stop();
                    results.Add((shipResponse.IsSuccessStatusCode, sw.ElapsedMilliseconds));
                }
                catch
                {
                    sw.Stop();
                    results.Add((false, sw.ElapsedMilliseconds));
                }
            }
        });

        await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.Success);
        var avgTime = results.Average(r => r.TotalMs);

        _output.WriteLine($"Rapid Lifecycle Test ({concurrency} workers, {ordersPerWorker} orders each):");
        _output.WriteLine($"  Total: {results.Count}, Success: {successCount}");
        _output.WriteLine($"  Avg Lifecycle Time: {avgTime:F2}ms");

        Assert.True(successCount >= results.Count * 0.9, "At least 90% of lifecycles should complete");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task BurstTraffic_HandlesSpikes()
    {
        var burstSize = 10;
        var results = new ConcurrentBag<(bool Success, long LatencyMs)>();

        // Send burst of requests
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, burstSize).Select(async i =>
        {
            var reqSw = Stopwatch.StartNew();
            try
            {
                var request = new
                {
                    CustomerId = $"burst-{i}",
                    Items = new[] { new { ProductId = "BURST-001", ProductName = "Burst", Quantity = 1, UnitPrice = 10.00m } }
                };
                var response = await _client.PostAsJsonAsync("/orders", request);
                reqSw.Stop();
                results.Add((response.IsSuccessStatusCode, reqSw.ElapsedMilliseconds));
            }
            catch
            {
                reqSw.Stop();
                results.Add((false, reqSw.ElapsedMilliseconds));
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        var successRate = (double)results.Count(r => r.Success) / results.Count * 100;
        var maxLatency = results.Max(r => r.LatencyMs);

        _output.WriteLine($"Burst Traffic Test ({burstSize} simultaneous requests):");
        _output.WriteLine($"  Success Rate: {successRate:F2}%");
        _output.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Max Latency: {maxLatency}ms");

        Assert.True(successRate >= 90, "Burst success rate should be at least 90%");
    }

    private record OrderCreatedResponse(string OrderId, decimal TotalAmount, DateTime CreatedAt);
}
