using System.Text.Json;
using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// End-to-end tests for distributed Flow scenarios.
/// </summary>
public class FlowE2ETests
{
    [Fact]
    public async Task E2E_OrderFlow_SuccessfulExecution()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var orderId = Guid.NewGuid().ToString();
        var steps = new List<string>();

        // Act
        var result = await executor.ExecuteAsync(
            $"order:{orderId}",
            "OrderFlow",
            JsonSerializer.SerializeToUtf8Bytes(new { OrderId = orderId }),
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("OrderFlow")
                    .Step(async c => { steps.Add("CheckInventory"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("SaveOrder"); await Task.Delay(10, c); },
                        async c => { steps.Add("CancelOrder"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ReserveStock"); await Task.Delay(10, c); },
                        async c => { steps.Add("ReleaseStock"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ProcessPayment"); await Task.Delay(10, c); },
                        async c => { steps.Add("RefundPayment"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ConfirmOrder"); await Task.Delay(10, c); });

                return await flow.ExecuteFromAsync(state.Step, ct);
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        steps.Should().BeEquivalentTo([
            "CheckInventory", "SaveOrder", "ReserveStock", "ProcessPayment", "ConfirmOrder"
        ]);
    }

    [Fact]
    public async Task E2E_OrderFlow_FailureWithCompensation()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var orderId = Guid.NewGuid().ToString();
        var steps = new List<string>();

        // Act
        var result = await executor.ExecuteAsync(
            $"order:{orderId}",
            "OrderFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("OrderFlow")
                    .Step(async c => { steps.Add("CheckInventory"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("SaveOrder"); await Task.Delay(10, c); },
                        async c => { steps.Add("CancelOrder"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ReserveStock"); await Task.Delay(10, c); },
                        async c => { steps.Add("ReleaseStock"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ProcessPayment"); throw new Exception("Payment failed"); },
                        async c => { steps.Add("RefundPayment"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ConfirmOrder"); await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Payment failed");

        // Compensation should happen in reverse order
        steps.Should().ContainInOrder("CheckInventory", "SaveOrder", "ReserveStock", "ProcessPayment");
        steps.Should().Contain("ReleaseStock");
        steps.Should().Contain("CancelOrder");
        steps.IndexOf("ReleaseStock").Should().BeGreaterThan(steps.IndexOf("ProcessPayment"));
    }

    [Fact]
    public async Task E2E_FlowRecovery_ResumesFromLastStep()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store, new FlowOptions
        {
            NodeId = "new-node",
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });
        var flowId = "recovery-test";
        var steps = new List<string>();

        // Simulate a partially completed flow that was abandoned
        var state = new FlowState
        {
            Id = flowId,
            Type = "RecoveryFlow",
            Status = FlowStatus.Running,
            Step = 2, // Already completed 2 steps
            Version = 0,
            Owner = "crashed-node",
            HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds() // Abandoned
        };
        await store.CreateAsync(state);

        // Act - New node executes and should resume from step 2
        var result = await executor.ExecuteAsync(
            flowId,
            "RecoveryFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("RecoveryFlow")
                    .Step(async c => { steps.Add("Step1"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step2"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step3"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step4"); await Task.Delay(10, c); });

                return await flow.ExecuteFromAsync(s.Step, ct);
            });

        // Assert - Only steps 3 and 4 should execute
        result.IsSuccess.Should().BeTrue();
        steps.Should().BeEquivalentTo(["Step3", "Step4"]);
    }

    [Fact]
    public async Task E2E_ConcurrentFlowExecution_OnlyOneExecutes()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var flowId = "concurrent-test";
        var executionCount = 0;

        // Act - Multiple nodes try to execute the same flow
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var executor = new FlowExecutor(store, new FlowOptions { NodeId = $"node-{i}" });
            return await executor.ExecuteAsync(
                flowId,
                "ConcurrentFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    Interlocked.Increment(ref executionCount);
                    await Task.Delay(50, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert - Only one should actually execute
        executionCount.Should().Be(1);
        results.Count(r => r.IsSuccess).Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task E2E_FlowWithData_SerializesAndDeserializes()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var order = new TestOrder { Id = "order-123", Amount = 99.99m, Items = ["item1", "item2"] };
        var data = JsonSerializer.SerializeToUtf8Bytes(order);
        TestOrder? deserializedOrder = null;

        // Act
        var result = await executor.ExecuteAsync(
            "order:order-123",
            "OrderFlow",
            data,
            async (state, ct) =>
            {
                deserializedOrder = JsonSerializer.Deserialize<TestOrder>(state.Data);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        deserializedOrder.Should().NotBeNull();
        deserializedOrder!.Id.Should().Be("order-123");
        deserializedOrder.Amount.Should().Be(99.99m);
        deserializedOrder.Items.Should().BeEquivalentTo(["item1", "item2"]);
    }

    [Fact]
    public async Task E2E_MultipleFlowTypes_IndependentExecution()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var orderSteps = new List<string>();
        var paymentSteps = new List<string>();

        // Act - Execute different flow types
        var orderTask = executor.ExecuteAsync(
            "order:1",
            "OrderFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                orderSteps.Add("OrderStep1");
                await Task.Delay(20, ct);
                orderSteps.Add("OrderStep2");
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        var paymentTask = executor.ExecuteAsync(
            "payment:1",
            "PaymentFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                paymentSteps.Add("PaymentStep1");
                await Task.Delay(20, ct);
                paymentSteps.Add("PaymentStep2");
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        await Task.WhenAll(orderTask, paymentTask);

        // Assert
        orderSteps.Should().BeEquivalentTo(["OrderStep1", "OrderStep2"]);
        paymentSteps.Should().BeEquivalentTo(["PaymentStep1", "PaymentStep2"]);
    }

    [Fact]
    public async Task E2E_HeartbeatPreventsClaimDuringExecution()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor1 = new FlowExecutor(store, new FlowOptions
        {
            NodeId = "node-1",
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            ClaimTimeout = TimeSpan.FromMilliseconds(200)
        });

        var flowId = "heartbeat-test";
        var claimAttempts = 0;

        // Act - Start a long-running flow
        var flowTask = executor1.ExecuteAsync(
            flowId,
            "HeartbeatFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                await Task.Delay(500, ct); // Long execution
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Try to claim while flow is running
        await Task.Delay(100);
        for (int i = 0; i < 3; i++)
        {
            var claimed = await store.TryClaimAsync("HeartbeatFlow", "node-2", 200);
            if (claimed != null) claimAttempts++;
            await Task.Delay(100);
        }

        await flowTask;

        // Assert - No claims should succeed while flow is running with heartbeat
        claimAttempts.Should().Be(0);
    }

    #region TDD: Advanced E2E Scenarios

    [Fact]
    public async Task E2E_PartialFailure_CompensatesAndRecordsError()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var flowId = "partial-failure";
        var steps = new List<string>();

        // Act
        var result = await executor.ExecuteAsync(
            flowId,
            "PartialFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("PartialFlow")
                    .Step(async c => { steps.Add("Step1"); await Task.Delay(10, c); },
                        async c => { steps.Add("Comp1"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step2"); await Task.Delay(10, c); },
                        async c => { steps.Add("Comp2"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step3"); throw new Exception("Step3 failed"); });

                return await flow.ExecuteAsync(ct);
            });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Step3 failed");

        var stored = await store.GetAsync(flowId);
        stored!.Status.Should().Be(FlowStatus.Failed);
        stored.Error.Should().Contain("Step3 failed");

        steps.Should().ContainInOrder("Step1", "Step2", "Step3");
        steps.Should().Contain("Comp2");
        steps.Should().Contain("Comp1");
    }

    [Fact]
    public async Task E2E_MultiNodeRecovery_OnlyOneNodeRecovers()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var flowId = "multi-node-recovery";
        var recoveryCount = 0;

        // Create abandoned flow
        var state = new FlowState
        {
            Id = flowId,
            Type = "RecoveryFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "crashed-node",
            HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds()
        };
        await store.CreateAsync(state);

        // Act - Multiple nodes try to recover
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var executor = new FlowExecutor(store, new FlowOptions
            {
                NodeId = $"recovery-node-{i}",
                ClaimTimeout = TimeSpan.FromSeconds(1)
            });

            return await executor.ExecuteAsync(
                flowId,
                "RecoveryFlow",
                ReadOnlyMemory<byte>.Empty,
                async (s, ct) =>
                {
                    Interlocked.Increment(ref recoveryCount);
                    await Task.Delay(50, ct);
                    return new FlowResult(true, 2, TimeSpan.Zero);
                });
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert - Only one node should actually execute recovery
        recoveryCount.Should().Be(1);
    }

    [Fact]
    public async Task E2E_FlowTimeout_MarkedAsFailed()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var flowId = "timeout-flow";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var result = await executor.ExecuteAsync(
            flowId,
            "TimeoutFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                try
                {
                    await Task.Delay(5000, ct); // Long operation
                    return new FlowResult(true, 1, TimeSpan.Zero);
                }
                catch (OperationCanceledException)
                {
                    return new FlowResult(false, 0, TimeSpan.Zero, "Timeout") { IsCancelled = true };
                }
            },
            cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_ChainedFlows_ExecuteSequentially()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var executionOrder = new List<string>();

        // Act - Execute flows in sequence
        var result1 = await executor.ExecuteAsync(
            "flow-1",
            "ChainFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                executionOrder.Add("Flow1-Start");
                await Task.Delay(50, ct);
                executionOrder.Add("Flow1-End");
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var result2 = await executor.ExecuteAsync(
            "flow-2",
            "ChainFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                executionOrder.Add("Flow2-Start");
                await Task.Delay(50, ct);
                executionOrder.Add("Flow2-End");
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        executionOrder.Should().BeEquivalentTo([
            "Flow1-Start", "Flow1-End", "Flow2-Start", "Flow2-End"
        ]);
    }

    [Fact]
    public async Task E2E_ParallelFlows_ExecuteIndependently()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var flow1Steps = new List<string>();
        var flow2Steps = new List<string>();

        // Act - Execute flows in parallel
        var task1 = executor.ExecuteAsync(
            "parallel-1",
            "ParallelFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                flow1Steps.Add("Start");
                await Task.Delay(100, ct);
                flow1Steps.Add("End");
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var task2 = executor.ExecuteAsync(
            "parallel-2",
            "ParallelFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                flow2Steps.Add("Start");
                await Task.Delay(100, ct);
                flow2Steps.Add("End");
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var results = await Task.WhenAll(task1, task2);

        // Assert
        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeTrue();
        flow1Steps.Should().BeEquivalentTo(["Start", "End"]);
        flow2Steps.Should().BeEquivalentTo(["Start", "End"]);
    }

    [Fact]
    public async Task E2E_FlowWithRetry_EventuallySucceeds()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var attempts = 0;

        // Act - Simulate retry logic
        FlowResult result = default;
        for (int retry = 0; retry < 3; retry++)
        {
            result = await executor.ExecuteAsync(
                $"retry-flow-{retry}",
                "RetryFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    attempts++;
                    if (attempts < 3)
                        return new FlowResult(false, 0, TimeSpan.Zero, "Transient error");
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });

            if (result.IsSuccess) break;
        }

        // Assert
        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task E2E_LargeDataPayload_HandledCorrectly()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var largeData = new byte[1024 * 100]; // 100KB
        new Random(42).NextBytes(largeData);
        byte[]? receivedData = null;

        // Act
        var result = await executor.ExecuteAsync(
            "large-data-flow",
            "LargeDataFlow",
            largeData,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        receivedData.Should().BeEquivalentTo(largeData);
    }

    [Fact]
    public async Task E2E_FlowStateTransitions_Correct()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var flowId = "state-transition";
        var statesDuringExecution = new List<FlowStatus>();

        // Act
        var result = await executor.ExecuteAsync(
            flowId,
            "StateFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                statesDuringExecution.Add(state.Status);
                await Task.Delay(10, ct);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        statesDuringExecution.Should().Contain(FlowStatus.Running);

        var finalState = await store.GetAsync(flowId);
        finalState!.Status.Should().Be(FlowStatus.Done);
    }

    [Fact]
    public async Task E2E_SagaPattern_FullCompensation()
    {
        // Arrange - Simulate a Saga with multiple services
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var serviceACalls = new List<string>();
        var serviceBCalls = new List<string>();
        var serviceCCalls = new List<string>();

        // Act
        var result = await executor.ExecuteAsync(
            "saga-flow",
            "SagaFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("Saga")
                    .Step(
                        async c => { serviceACalls.Add("Execute"); await Task.Delay(10, c); },
                        async c => { serviceACalls.Add("Compensate"); await Task.Delay(10, c); })
                    .Step(
                        async c => { serviceBCalls.Add("Execute"); await Task.Delay(10, c); },
                        async c => { serviceBCalls.Add("Compensate"); await Task.Delay(10, c); })
                    .Step(
                        async c => { serviceCCalls.Add("Execute"); throw new Exception("Service C failed"); },
                        async c => { serviceCCalls.Add("Compensate"); await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            });

        // Assert
        result.IsSuccess.Should().BeFalse();

        // All services executed
        serviceACalls.Should().Contain("Execute");
        serviceBCalls.Should().Contain("Execute");
        serviceCCalls.Should().Contain("Execute");

        // Only A and B compensated (C failed during execution)
        serviceACalls.Should().Contain("Compensate");
        serviceBCalls.Should().Contain("Compensate");
        serviceCCalls.Should().NotContain("Compensate");
    }

    [Fact]
    public async Task E2E_DistributedTransaction_AllOrNothing()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var dbWrites = new List<string>();
        var messagesSent = new List<string>();
        var cacheUpdates = new List<string>();

        // Act - Simulate distributed transaction
        var result = await executor.ExecuteAsync(
            "distributed-tx",
            "DistributedTx",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("DistributedTx")
                    .Step(
                        async c => { dbWrites.Add("Write"); await Task.Delay(10, c); },
                        async c => { dbWrites.Add("Rollback"); await Task.Delay(10, c); })
                    .Step(
                        async c => { messagesSent.Add("Send"); await Task.Delay(10, c); },
                        async c => { messagesSent.Add("Unsend"); await Task.Delay(10, c); })
                    .Step(
                        async c => { cacheUpdates.Add("Update"); throw new Exception("Cache failed"); },
                        async c => { cacheUpdates.Add("Invalidate"); await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            });

        // Assert - All previous operations should be rolled back
        result.IsSuccess.Should().BeFalse();
        dbWrites.Should().Contain("Rollback");
        messagesSent.Should().Contain("Unsend");
    }

    #endregion

    #region TDD: Stress E2E Tests

    [Fact]
    public async Task E2E_HighConcurrency_ManyFlows()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var completedFlows = 0;

        // Act - Execute many flows concurrently
        var tasks = Enumerable.Range(1, 50).Select(async i =>
        {
            var result = await executor.ExecuteAsync(
                $"concurrent-{i}",
                "ConcurrentFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    await Task.Delay(10, ct);
                    Interlocked.Increment(ref completedFlows);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
            return result;
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.All(r => r.IsSuccess).Should().BeTrue();
        completedFlows.Should().Be(50);
    }

    [Fact]
    public async Task E2E_RapidCreateAndComplete_NoLeaks()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);

        // Act - Rapidly create and complete flows
        for (int i = 0; i < 100; i++)
        {
            var result = await executor.ExecuteAsync(
                $"rapid-{i}",
                "RapidFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    await Task.Yield();
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });

            result.IsSuccess.Should().BeTrue();
        }

        // Assert - All flows should be completed
        for (int i = 0; i < 100; i++)
        {
            var state = await store.GetAsync($"rapid-{i}");
            state!.Status.Should().Be(FlowStatus.Done);
        }
    }

    #endregion

    private record TestOrder
    {
        public string Id { get; init; } = "";
        public decimal Amount { get; init; }
        public string[] Items { get; init; } = [];
    }
}
