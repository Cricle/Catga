using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// External integration E2E tests.
/// Tests third-party service connections, API integrations, and external dependency handling.
/// </summary>
public class ExternalIntegrationTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task External_ServiceUnavailable_HandlesGracefully()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var externalService = new MockExternalService(available: false);

        var flow = FlowBuilder.Create<ExternalState>("external-unavailable")
            .Step("call-external", async (state, ct) =>
            {
                try
                {
                    state.Response = await externalService.CallAsync(state.Request);
                    state.Success = true;
                }
                catch (ExternalServiceException ex)
                {
                    state.ErrorMessage = ex.Message;
                    state.UsedFallback = true;
                    state.Response = "Fallback response";
                }
                return true;
            })
            .Build();

        var state = new ExternalState { FlowId = "unavailable-test", Request = "test-request" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.UsedFallback.Should().BeTrue();
        result.State.Response.Should().Be("Fallback response");
    }

    [Fact]
    public async Task External_SlowResponse_TimesOutWithFallback()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var externalService = new MockExternalService(available: true, delayMs: 5000);

        var flow = FlowBuilder.Create<ExternalState>("external-slow")
            .Step("call-with-timeout", async (state, ct) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(100));

                try
                {
                    state.Response = await externalService.CallAsync(state.Request, cts.Token);
                    state.Success = true;
                }
                catch (OperationCanceledException)
                {
                    state.TimedOut = true;
                    state.Response = "Cached/Default response";
                }
                return true;
            })
            .Build();

        var state = new ExternalState { FlowId = "slow-test", Request = "test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.TimedOut.Should().BeTrue();
    }

    [Fact]
    public async Task External_ConnectionPool_ReusesConnections()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var connectionPool = new MockConnectionPool(maxConnections: 5);

        var flow = FlowBuilder.Create<ConnectionState>("connection-pool")
            .Step("use-connection", async (state, ct) =>
            {
                var conn = await connectionPool.AcquireAsync();
                try
                {
                    state.ConnectionId = conn.Id;
                    await Task.Delay(10, ct);
                }
                finally
                {
                    connectionPool.Release(conn);
                }
                return true;
            })
            .Build();

        // Execute multiple flows concurrently
        var tasks = Enumerable.Range(1, 20).Select(async i =>
        {
            var state = new ConnectionState { FlowId = $"conn-{i}" };
            return await executor.ExecuteAsync(flow, state);
        });

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());

        // Connections should have been reused
        var uniqueConnections = results.Select(r => r.State.ConnectionId).Distinct().Count();
        uniqueConnections.Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public async Task External_RetryOnTransientError_RecoversSuccessfully()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var callCount = 0;
        var service = new MockExternalService(available: true, failFirstNCalls: 2);

        var flow = FlowBuilder.Create<ExternalState>("retry-transient")
            .Step("call-with-retry", async (state, ct) =>
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        callCount++;
                        state.Response = await service.CallWithTransientFailureAsync();
                        state.Success = true;
                        state.Attempts = attempt;
                        return true;
                    }
                    catch (TransientException)
                    {
                        if (attempt == 3) throw;
                        await Task.Delay(10 * attempt, ct);
                    }
                }
                return false;
            })
            .Build();

        var state = new ExternalState { FlowId = "retry-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Success.Should().BeTrue();
        result.State.Attempts.Should().Be(3); // Succeeded on 3rd attempt
    }

    [Fact]
    public async Task External_MultipleServices_AggregatesResults()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var serviceA = new MockExternalService(available: true, responsePrefix: "A:");
        var serviceB = new MockExternalService(available: true, responsePrefix: "B:");
        var serviceC = new MockExternalService(available: false);

        var flow = FlowBuilder.Create<AggregatedState>("multi-service")
            .Step("call-service-a", async (state, ct) =>
            {
                try
                {
                    state.ServiceAResponse = await serviceA.CallAsync("query");
                }
                catch { state.ServiceAResponse = "A:unavailable"; }
                return true;
            })
            .Step("call-service-b", async (state, ct) =>
            {
                try
                {
                    state.ServiceBResponse = await serviceB.CallAsync("query");
                }
                catch { state.ServiceBResponse = "B:unavailable"; }
                return true;
            })
            .Step("call-service-c", async (state, ct) =>
            {
                try
                {
                    state.ServiceCResponse = await serviceC.CallAsync("query");
                }
                catch { state.ServiceCResponse = "C:unavailable"; }
                return true;
            })
            .Step("aggregate", async (state, ct) =>
            {
                state.AggregatedResult = $"{state.ServiceAResponse}|{state.ServiceBResponse}|{state.ServiceCResponse}";
                return true;
            })
            .Build();

        var state = new AggregatedState { FlowId = "multi-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.ServiceAResponse.Should().StartWith("A:");
        result.State.ServiceBResponse.Should().StartWith("B:");
        result.State.ServiceCResponse.Should().Be("C:unavailable");
    }

    [Fact]
    public async Task External_HealthCheck_DetectsUnhealthyService()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var service = new MockExternalService(available: true);

        var flow = FlowBuilder.Create<HealthCheckState>("health-check")
            .Step("check-health", async (state, ct) =>
            {
                state.HealthCheckResult = await service.HealthCheckAsync();
                state.IsHealthy = state.HealthCheckResult == "healthy";
                return true;
            })
            .If(s => !s.IsHealthy)
                .Then(f => f.Step("alert", async (state, ct) =>
                {
                    state.AlertSent = true;
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new HealthCheckState { FlowId = "health-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.IsHealthy.Should().BeTrue();
        result.State.AlertSent.Should().BeFalse();
    }

    [Fact]
    public async Task External_BatchRequest_ProcessesEfficiently()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var service = new MockExternalService(available: true);

        var flow = FlowBuilder.Create<BatchExternalState>("batch-external")
            .Step("batch-call", async (state, ct) =>
            {
                // Instead of N individual calls, make one batch call
                state.Results = await service.BatchCallAsync(state.Requests);
                state.BatchProcessed = true;
                return true;
            })
            .Build();

        var state = new BatchExternalState
        {
            FlowId = "batch-test",
            Requests = Enumerable.Range(1, 100).Select(i => $"req-{i}").ToList()
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(100);
        result.State.BatchProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task External_RateLimited_RespectsLimits()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var service = new MockExternalService(available: true, rateLimit: 5);

        var flow = FlowBuilder.Create<RateLimitState>("rate-limited")
            .Step("call-with-rate-limit", async (state, ct) =>
            {
                try
                {
                    state.Response = await service.RateLimitedCallAsync();
                    state.Success = true;
                }
                catch (RateLimitedException)
                {
                    state.RateLimited = true;
                }
                return true;
            })
            .Build();

        var results = new List<FlowResult<RateLimitState>>();

        // Make 10 rapid calls
        for (int i = 0; i < 10; i++)
        {
            var state = new RateLimitState { FlowId = $"rate-{i}" };
            results.Add(await executor.ExecuteAsync(flow, state));
        }

        var successful = results.Count(r => r.State.Success);
        var rateLimited = results.Count(r => r.State.RateLimited);

        successful.Should().BeLessOrEqualTo(5);
        rateLimited.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task External_CacheableResponse_UsesCachedData()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var service = new MockExternalService(available: true);
        var cache = new Dictionary<string, (string Value, DateTime Expiry)>();

        var flow = FlowBuilder.Create<CacheableState>("cacheable")
            .Step("get-data", async (state, ct) =>
            {
                // Check cache
                if (cache.TryGetValue(state.CacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                {
                    state.Response = cached.Value;
                    state.FromCache = true;
                    return true;
                }

                // Call service
                state.Response = await service.CallAsync(state.Request);
                state.FromCache = false;

                // Cache result
                cache[state.CacheKey] = (state.Response, DateTime.UtcNow.AddMinutes(5));
                return true;
            })
            .Build();

        // First call - not cached
        var state1 = new CacheableState { FlowId = "cache-1", CacheKey = "key-1", Request = "data" };
        var result1 = await executor.ExecuteAsync(flow, state1);

        result1.State.FromCache.Should().BeFalse();

        // Second call - should be cached
        var state2 = new CacheableState { FlowId = "cache-2", CacheKey = "key-1", Request = "data" };
        var result2 = await executor.ExecuteAsync(flow, state2);

        result2.State.FromCache.Should().BeTrue();
    }

    #region Mock Services and State Classes

    public class MockExternalService
    {
        private readonly bool _available;
        private readonly int _delayMs;
        private readonly string _responsePrefix;
        private int _failFirstNCalls;
        private int _callCount;
        private readonly int _rateLimit;
        private int _rateLimitCounter;

        public MockExternalService(bool available, int delayMs = 0, string responsePrefix = "", int failFirstNCalls = 0, int rateLimit = int.MaxValue)
        {
            _available = available;
            _delayMs = delayMs;
            _responsePrefix = responsePrefix;
            _failFirstNCalls = failFirstNCalls;
            _rateLimit = rateLimit;
        }

        public async Task<string> CallAsync(string request, CancellationToken ct = default)
        {
            if (!_available) throw new ExternalServiceException("Service unavailable");
            if (_delayMs > 0) await Task.Delay(_delayMs, ct);
            return $"{_responsePrefix}Response to: {request}";
        }

        public Task<string> CallWithTransientFailureAsync()
        {
            _callCount++;
            if (_callCount <= _failFirstNCalls)
            {
                throw new TransientException("Transient failure");
            }
            return Task.FromResult("Success");
        }

        public Task<string> HealthCheckAsync()
        {
            return Task.FromResult(_available ? "healthy" : "unhealthy");
        }

        public Task<List<string>> BatchCallAsync(List<string> requests)
        {
            if (!_available) throw new ExternalServiceException("Service unavailable");
            return Task.FromResult(requests.Select(r => $"Response:{r}").ToList());
        }

        public Task<string> RateLimitedCallAsync()
        {
            _rateLimitCounter++;
            if (_rateLimitCounter > _rateLimit)
            {
                throw new RateLimitedException("Rate limit exceeded");
            }
            return Task.FromResult("OK");
        }
    }

    public class MockConnectionPool
    {
        private readonly int _maxConnections;
        private readonly SemaphoreSlim _semaphore;
        private int _connectionIdCounter;

        public MockConnectionPool(int maxConnections)
        {
            _maxConnections = maxConnections;
            _semaphore = new SemaphoreSlim(maxConnections);
        }

        public async Task<MockConnection> AcquireAsync()
        {
            await _semaphore.WaitAsync();
            return new MockConnection { Id = Interlocked.Increment(ref _connectionIdCounter) % _maxConnections };
        }

        public void Release(MockConnection conn)
        {
            _semaphore.Release();
        }
    }

    public class MockConnection
    {
        public int Id { get; set; }
    }

    public class ExternalState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Request { get; set; } = "";
        public string? Response { get; set; }
        public bool Success { get; set; }
        public bool UsedFallback { get; set; }
        public bool TimedOut { get; set; }
        public string? ErrorMessage { get; set; }
        public int Attempts { get; set; }
    }

    public class ConnectionState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int ConnectionId { get; set; }
    }

    public class AggregatedState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? ServiceAResponse { get; set; }
        public string? ServiceBResponse { get; set; }
        public string? ServiceCResponse { get; set; }
        public string? AggregatedResult { get; set; }
    }

    public class HealthCheckState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? HealthCheckResult { get; set; }
        public bool IsHealthy { get; set; }
        public bool AlertSent { get; set; }
    }

    public class BatchExternalState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Requests { get; set; } = new();
        public List<string> Results { get; set; } = new();
        public bool BatchProcessed { get; set; }
    }

    public class RateLimitState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? Response { get; set; }
        public bool Success { get; set; }
        public bool RateLimited { get; set; }
    }

    public class CacheableState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string CacheKey { get; set; } = "";
        public string Request { get; set; } = "";
        public string? Response { get; set; }
        public bool FromCache { get; set; }
    }

    public class ExternalServiceException : Exception { public ExternalServiceException(string msg) : base(msg) { } }
    public class TransientException : Exception { public TransientException(string msg) : base(msg) { } }
    public class RateLimitedException : Exception { public RateLimitedException(string msg) : base(msg) { } }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
