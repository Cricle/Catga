using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Concurrency flow scenario tests.
/// Tests thread safety, race conditions, and concurrent execution patterns.
/// </summary>
public class ConcurrencyFlowTests
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
    public async Task Concurrency_MultipleFlowsParallel_ExecuteIndependently()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var results = new ConcurrentBag<string>();

        var flow = FlowBuilder.Create<ConcurrencyState>("parallel-flows")
            .Step("process", async (state, ct) =>
            {
                await Task.Delay(Random.Shared.Next(10, 50), ct);
                results.Add(state.FlowId);
                state.Completed = true;
                return true;
            })
            .Build();

        // Execute 50 flows in parallel
        var tasks = Enumerable.Range(1, 50).Select(i =>
        {
            var state = new ConcurrencyState { FlowId = $"flow-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        var flowResults = await Task.WhenAll(tasks);

        flowResults.Should().HaveCount(50);
        flowResults.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Should().HaveCount(50);
    }

    [Fact]
    public async Task Concurrency_SharedResource_ThreadSafeAccess()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var sharedCounter = new SharedCounter();

        var flow = FlowBuilder.Create<ConcurrencyState>("shared-resource")
            .Step("increment", async (state, ct) =>
            {
                for (int i = 0; i < 100; i++)
                {
                    sharedCounter.Increment();
                }
                return true;
            })
            .Build();

        // Execute 10 flows concurrently, each incrementing 100 times
        var tasks = Enumerable.Range(1, 10).Select(i =>
        {
            var state = new ConcurrencyState { FlowId = $"flow-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        await Task.WhenAll(tasks);

        sharedCounter.Value.Should().Be(1000); // 10 * 100
    }

    [Fact]
    public async Task Concurrency_RaceCondition_HandledWithLocking()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var lockObj = new object();
        var operationLog = new List<string>();

        var flow = FlowBuilder.Create<ConcurrencyState>("race-condition")
            .Step("critical-section", async (state, ct) =>
            {
                lock (lockObj)
                {
                    operationLog.Add($"enter-{state.FlowId}");
                    Thread.Sleep(5); // Simulate work
                    operationLog.Add($"exit-{state.FlowId}");
                }
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 5).Select(i =>
        {
            var state = new ConcurrencyState { FlowId = $"flow-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        await Task.WhenAll(tasks);

        // Verify no interleaving (enter-exit pairs are consecutive)
        for (int i = 0; i < operationLog.Count; i += 2)
        {
            var enterId = operationLog[i].Replace("enter-", "");
            var exitId = operationLog[i + 1].Replace("exit-", "");
            enterId.Should().Be(exitId);
        }
    }

    [Fact]
    public async Task Concurrency_SemaphoreLimit_RespectsMaxConcurrency()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var semaphore = new SemaphoreSlim(3); // Max 3 concurrent
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var lockObj = new object();

        var flow = FlowBuilder.Create<ConcurrencyState>("semaphore-limit")
            .Step("limited-operation", async (state, ct) =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    lock (lockObj)
                    {
                        currentConcurrent++;
                        if (currentConcurrent > maxConcurrent)
                            maxConcurrent = currentConcurrent;
                    }

                    await Task.Delay(50, ct);

                    lock (lockObj)
                    {
                        currentConcurrent--;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 20).Select(i =>
        {
            var state = new ConcurrencyState { FlowId = $"flow-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        await Task.WhenAll(tasks);

        maxConcurrent.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task Concurrency_DeadlockPrevention_CompletesSuccessfully()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var lockA = new SemaphoreSlim(1);
        var lockB = new SemaphoreSlim(1);

        var flow = FlowBuilder.Create<ConcurrencyState>("deadlock-prevention")
            .Step("acquire-locks", async (state, ct) =>
            {
                // Always acquire locks in the same order to prevent deadlock
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                if (await lockA.WaitAsync(1000, timeoutCts.Token))
                {
                    try
                    {
                        if (await lockB.WaitAsync(1000, timeoutCts.Token))
                        {
                            try
                            {
                                await Task.Delay(10, ct);
                                state.Completed = true;
                            }
                            finally
                            {
                                lockB.Release();
                            }
                        }
                    }
                    finally
                    {
                        lockA.Release();
                    }
                }
                return state.Completed;
            })
            .Build();

        var tasks = Enumerable.Range(1, 10).Select(i =>
        {
            var state = new ConcurrencyState { FlowId = $"flow-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.State.Completed.Should().BeTrue());
    }

    [Fact]
    public async Task Concurrency_AtomicOperations_MaintainConsistency()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var atomicBalance = new AtomicBalance(1000);

        var flow = FlowBuilder.Create<TransferState>("atomic-transfer")
            .Step("transfer", async (state, ct) =>
            {
                return atomicBalance.Transfer(state.Amount);
            })
            .Build();

        // Multiple concurrent transfers
        var tasks = new List<Task<FlowResult<TransferState>>>();
        for (int i = 0; i < 50; i++)
        {
            var state = new TransferState { FlowId = $"transfer-{i}", Amount = 10 };
            tasks.Add(executor.ExecuteAsync(flow, state).AsTask());
        }

        await Task.WhenAll(tasks);

        // Balance should never go negative
        atomicBalance.Balance.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Concurrency_ReaderWriterLock_AllowsConcurrentReads()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var rwLock = new ReaderWriterLockSlim();
        var readCount = 0;
        var maxConcurrentReads = 0;
        var lockObj = new object();

        var flow = FlowBuilder.Create<ConcurrencyState>("reader-writer")
            .Step("read-operation", async (state, ct) =>
            {
                rwLock.EnterReadLock();
                try
                {
                    lock (lockObj)
                    {
                        readCount++;
                        if (readCount > maxConcurrentReads)
                            maxConcurrentReads = readCount;
                    }

                    await Task.Delay(20, ct);

                    lock (lockObj)
                    {
                        readCount--;
                    }
                }
                finally
                {
                    rwLock.ExitReadLock();
                }
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 20).Select(i =>
        {
            var state = new ConcurrencyState { FlowId = $"reader-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        await Task.WhenAll(tasks);

        maxConcurrentReads.Should().BeGreaterThan(1); // Multiple concurrent reads allowed
    }

    [Fact]
    public async Task Concurrency_ProducerConsumer_ProcessesAllItems()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var queue = new ConcurrentQueue<string>();
        var processed = new ConcurrentBag<string>();

        // Producer flow
        var producerFlow = FlowBuilder.Create<ProducerState>("producer")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"produce-{item}", async (state, ct) =>
                {
                    queue.Enqueue(item);
                    return true;
                }))
            .Build();

        // Consumer flow
        var consumerFlow = FlowBuilder.Create<ConsumerState>("consumer")
            .While(s => s.ProcessedCount < s.ExpectedCount)
                .Do(f => f.Step("consume", async (state, ct) =>
                {
                    if (queue.TryDequeue(out var item))
                    {
                        processed.Add(item);
                        state.ProcessedCount++;
                    }
                    else
                    {
                        await Task.Delay(5, ct);
                    }
                    return true;
                }))
            .EndWhile()
            .Build();

        // Start producer
        var producerState = new ProducerState
        {
            FlowId = "producer-1",
            Items = Enumerable.Range(1, 100).Select(i => $"item-{i}").ToList()
        };
        await executor.ExecuteAsync(producerFlow, producerState);

        // Start consumers
        var consumerTasks = Enumerable.Range(1, 5).Select(i =>
        {
            var state = new ConsumerState { FlowId = $"consumer-{i}", ExpectedCount = 20 };
            return executor.ExecuteAsync(consumerFlow, state).AsTask();
        });

        await Task.WhenAll(consumerTasks);

        processed.Should().HaveCount(100);
    }

    [Fact]
    public async Task Concurrency_ContextIsolation_NoStateLeakage()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<IsolationState>("context-isolation")
            .Step("set-context", async (state, ct) =>
            {
                state.ContextValue = $"value-{state.FlowId}";
                await Task.Delay(Random.Shared.Next(10, 30), ct);
                return true;
            })
            .Step("verify-context", async (state, ct) =>
            {
                state.VerifiedValue = state.ContextValue;
                return state.ContextValue == $"value-{state.FlowId}";
            })
            .Build();

        var tasks = Enumerable.Range(1, 50).Select(i =>
        {
            var state = new IsolationState { FlowId = $"flow-{i}" };
            return executor.ExecuteAsync(flow, state).AsTask();
        });

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.State.VerifiedValue.Should().Be($"value-{r.State.FlowId}");
        });
    }

    #region State Classes

    public class ConcurrencyState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool Completed { get; set; }
    }

    public class TransferState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public class ProducerState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
    }

    public class ConsumerState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int ProcessedCount { get; set; }
        public int ExpectedCount { get; set; }
    }

    public class IsolationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? ContextValue { get; set; }
        public string? VerifiedValue { get; set; }
    }

    public class SharedCounter
    {
        private int _value;
        public int Value => _value;
        public void Increment() => Interlocked.Increment(ref _value);
    }

    public class AtomicBalance
    {
        private decimal _balance;
        private readonly object _lock = new();

        public AtomicBalance(decimal initial) => _balance = initial;
        public decimal Balance
        {
            get { lock (_lock) return _balance; }
        }

        public bool Transfer(decimal amount)
        {
            lock (_lock)
            {
                if (_balance >= amount)
                {
                    _balance -= amount;
                    return true;
                }
                return false;
            }
        }
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
