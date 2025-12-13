using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Bulkhead pattern flow scenario tests.
/// Tests resource isolation, partition tolerance, and failure containment.
/// </summary>
public class BulkheadFlowTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Bulkhead_IsolatesServiceFailures_OthersUnaffected()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var bulkheadA = new Bulkhead("ServiceA", 5);
        var bulkheadB = new Bulkhead("ServiceB", 5);

        var flow = FlowBuilder.Create<BulkheadState>("isolated-services")
            .Step("call-service-a", async (state, ct) =>
            {
                if (!bulkheadA.TryAcquire())
                {
                    state.ServiceAResult = "Rejected-Bulkhead";
                    return true;
                }
                try
                {
                    if (state.ServiceAFails)
                        throw new InvalidOperationException("Service A failure");
                    state.ServiceAResult = "Success";
                }
                finally
                {
                    bulkheadA.Release();
                }
                return true;
            })
            .Step("call-service-b", async (state, ct) =>
            {
                if (!bulkheadB.TryAcquire())
                {
                    state.ServiceBResult = "Rejected-Bulkhead";
                    return true;
                }
                try
                {
                    state.ServiceBResult = "Success";
                }
                finally
                {
                    bulkheadB.Release();
                }
                return true;
            })
            .Build();

        // Service A fails but Service B should still work
        var state = new BulkheadState { FlowId = "isolated-test", ServiceAFails = true };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.ServiceBResult.Should().Be("Success");
    }

    [Fact]
    public async Task Bulkhead_LimitsMaxConcurrency_RejectsExcess()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var bulkhead = new Bulkhead("LimitedService", 3);
        var acceptedCount = 0;
        var rejectedCount = 0;

        var flow = FlowBuilder.Create<BulkheadState>("limited-service")
            .Step("limited-call", async (state, ct) =>
            {
                if (!bulkhead.TryAcquire())
                {
                    Interlocked.Increment(ref rejectedCount);
                    state.Rejected = true;
                    return true;
                }
                try
                {
                    Interlocked.Increment(ref acceptedCount);
                    await Task.Delay(100, ct); // Hold the slot
                    state.Accepted = true;
                }
                finally
                {
                    bulkhead.Release();
                }
                return true;
            })
            .Build();

        // Start 10 concurrent requests with max 3 concurrent
        var tasks = Enumerable.Range(1, 10).Select(i =>
        {
            var state = new BulkheadState { FlowId = $"limited-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        await Task.WhenAll(tasks);

        // Some should be rejected
        rejectedCount.Should().BeGreaterThan(0);
        (acceptedCount + rejectedCount).Should().Be(10);
    }

    [Fact]
    public async Task Bulkhead_QueuedRequests_ProcessedWhenSlotAvailable()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var bulkhead = new QueuedBulkhead("QueuedService", 2, 5);
        var processedOrder = new ConcurrentQueue<int>();

        var flow = FlowBuilder.Create<QueuedState>("queued-service")
            .Step("queued-call", async (state, ct) =>
            {
                var acquired = await bulkhead.AcquireAsync(TimeSpan.FromSeconds(5), ct);
                if (!acquired)
                {
                    state.QueueRejected = true;
                    return true;
                }
                try
                {
                    processedOrder.Enqueue(state.RequestId);
                    await Task.Delay(50, ct);
                    state.Processed = true;
                }
                finally
                {
                    bulkhead.Release();
                }
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 7).Select(i =>
        {
            var state = new QueuedState { FlowId = $"queued-{i}", RequestId = i };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        var results = await Task.WhenAll(tasks);

        var processed = results.Count(r => r.State.Processed);
        processed.Should().Be(7); // All should eventually process with queue
    }

    [Fact]
    public async Task Bulkhead_PerTenant_IsolatesResources()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var tenantBulkheads = new ConcurrentDictionary<string, Bulkhead>();

        var flow = FlowBuilder.Create<TenantState>("per-tenant-bulkhead")
            .Step("tenant-call", async (state, ct) =>
            {
                var bulkhead = tenantBulkheads.GetOrAdd(state.TenantId, _ => new Bulkhead(state.TenantId, 2));

                if (!bulkhead.TryAcquire())
                {
                    state.Rejected = true;
                    return true;
                }
                try
                {
                    await Task.Delay(50, ct);
                    state.Processed = true;
                }
                finally
                {
                    bulkhead.Release();
                }
                return true;
            })
            .Build();

        // Tenant A: 5 requests, Tenant B: 3 requests (each tenant has limit 2)
        var tasks = new List<Task<FlowResult<TenantState>>>();
        for (int i = 0; i < 5; i++)
            tasks.Add(executor.ExecuteAsync(flow, new TenantState { FlowId = $"a-{i}", TenantId = "TenantA" }).AsTask());
        for (int i = 0; i < 3; i++)
            tasks.Add(executor.ExecuteAsync(flow, new TenantState { FlowId = $"b-{i}", TenantId = "TenantB" }).AsTask());

        var results = await Task.WhenAll(tasks);

        var tenantAResults = results.Where(r => r.State.TenantId == "TenantA");
        var tenantBResults = results.Where(r => r.State.TenantId == "TenantB");

        // Each tenant should have some rejected due to bulkhead limit
        tenantAResults.Count(r => r.State.Rejected).Should().BeGreaterThan(0);
        tenantBResults.Count(r => r.State.Rejected).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Bulkhead_TimeoutOnQueue_RejectsWhenQueueFull()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var bulkhead = new QueuedBulkhead("TimeoutService", 1, 2); // 1 concurrent, 2 queue
        var results = new ConcurrentBag<FlowResult<TimeoutQueueState>>();

        var flow = FlowBuilder.Create<TimeoutQueueState>("timeout-queue")
            .Step("slow-operation", async (state, ct) =>
            {
                var acquired = await bulkhead.AcquireAsync(TimeSpan.FromMilliseconds(50), ct);
                if (!acquired)
                {
                    state.TimedOut = true;
                    return true;
                }
                try
                {
                    await Task.Delay(200, ct); // Slow operation
                    state.Completed = true;
                }
                finally
                {
                    bulkhead.Release();
                }
                return true;
            })
            .Build();

        // Start 5 requests - with 1 slot + 2 queue, 2 should timeout
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var state = new TimeoutQueueState { FlowId = $"timeout-{i}" };
            var result = await executor.ExecuteAsync(flow, state);
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        var timedOut = results.Count(r => r.State.TimedOut);
        timedOut.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Bulkhead_PartitionedByOperation_IndependentLimits()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var readBulkhead = new Bulkhead("Read", 10);
        var writeBulkhead = new Bulkhead("Write", 2);

        var flow = FlowBuilder.Create<OperationState>("partitioned-ops")
            .If(s => s.OperationType == "Read")
                .Then(f => f.Step("read-op", async (state, ct) =>
                {
                    if (!readBulkhead.TryAcquire())
                    {
                        state.Rejected = true;
                        return true;
                    }
                    try
                    {
                        await Task.Delay(10, ct);
                        state.Completed = true;
                    }
                    finally
                    {
                        readBulkhead.Release();
                    }
                    return true;
                }))
            .Else()
                .Then(f => f.Step("write-op", async (state, ct) =>
                {
                    if (!writeBulkhead.TryAcquire())
                    {
                        state.Rejected = true;
                        return true;
                    }
                    try
                    {
                        await Task.Delay(50, ct);
                        state.Completed = true;
                    }
                    finally
                    {
                        writeBulkhead.Release();
                    }
                    return true;
                }))
            .EndIf()
            .Build();

        // 15 reads and 5 writes concurrent
        var tasks = new List<Task<FlowResult<OperationState>>>();
        for (int i = 0; i < 15; i++)
            tasks.Add(executor.ExecuteAsync(flow, new OperationState { FlowId = $"read-{i}", OperationType = "Read" }).AsTask());
        for (int i = 0; i < 5; i++)
            tasks.Add(executor.ExecuteAsync(flow, new OperationState { FlowId = $"write-{i}", OperationType = "Write" }).AsTask());

        var results = await Task.WhenAll(tasks);

        var readResults = results.Where(r => r.State.OperationType == "Read");
        var writeResults = results.Where(r => r.State.OperationType == "Write");

        // Reads have higher limit, fewer rejections expected
        var readRejected = readResults.Count(r => r.State.Rejected);
        var writeRejected = writeResults.Count(r => r.State.Rejected);

        writeRejected.Should().BeGreaterOrEqualTo(readRejected);
    }

    #region State Classes and Bulkhead Implementations

    public class BulkheadState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ServiceAFails { get; set; }
        public string? ServiceAResult { get; set; }
        public string? ServiceBResult { get; set; }
        public bool Accepted { get; set; }
        public bool Rejected { get; set; }
    }

    public class QueuedState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int RequestId { get; set; }
        public bool Processed { get; set; }
        public bool QueueRejected { get; set; }
    }

    public class TenantState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string TenantId { get; set; } = "";
        public bool Processed { get; set; }
        public bool Rejected { get; set; }
    }

    public class TimeoutQueueState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool Completed { get; set; }
        public bool TimedOut { get; set; }
    }

    public class OperationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OperationType { get; set; } = "";
        public bool Completed { get; set; }
        public bool Rejected { get; set; }
    }

    public class Bulkhead
    {
        private readonly SemaphoreSlim _semaphore;
        public string Name { get; }

        public Bulkhead(string name, int maxConcurrency)
        {
            Name = name;
            _semaphore = new SemaphoreSlim(maxConcurrency);
        }

        public bool TryAcquire() => _semaphore.Wait(0);
        public void Release() => _semaphore.Release();
    }

    public class QueuedBulkhead
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly SemaphoreSlim _queue;
        public string Name { get; }

        public QueuedBulkhead(string name, int maxConcurrency, int maxQueue)
        {
            Name = name;
            _semaphore = new SemaphoreSlim(maxConcurrency);
            _queue = new SemaphoreSlim(maxQueue);
        }

        public async Task<bool> AcquireAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (!_queue.Wait(0)) return false;
            try
            {
                return await _semaphore.WaitAsync(timeout, ct);
            }
            finally
            {
                _queue.Release();
            }
        }

        public void Release() => _semaphore.Release();
    }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
