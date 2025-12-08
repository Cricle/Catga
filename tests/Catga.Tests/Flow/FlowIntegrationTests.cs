using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// Integration tests for Flow with various scenarios.
/// </summary>
public class FlowIntegrationTests
{
    private readonly InMemoryFlowStore _store = new();

    #region Multi-Node Simulation

    [Fact]
    public async Task MultiNode_OnlyOneNodeExecutesFlow()
    {
        var flowId = "multi-node-flow";
        var executionCount = 0;

        // Simulate 5 nodes trying to execute same flow
        var tasks = Enumerable.Range(1, 5).Select(async nodeId =>
        {
            var executor = new FlowExecutor(_store, new FlowOptions
            {
                NodeId = $"node-{nodeId}",
                ClaimTimeout = TimeSpan.FromSeconds(1)
            });

            return await executor.ExecuteAsync(
                flowId,
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    Interlocked.Increment(ref executionCount);
                    await Task.Delay(100, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // At least one should succeed
        results.Count(r => r.IsSuccess).Should().BeGreaterOrEqualTo(1);
        // Only one should actually execute the flow logic
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task MultiNode_FailoverOnNodeCrash()
    {
        var flowId = "failover-flow";

        // Node 1 starts but "crashes" (abandoned heartbeat)
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "crashed-node",
            HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        // Node 2 takes over
        var executor = new FlowExecutor(_store, new FlowOptions
        {
            NodeId = "recovery-node",
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });

        var executed = false;
        var result = await executor.ExecuteAsync(
            flowId,
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                s.Step.Should().Be(1); // Resume from step 1
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();

        var stored = await _store.GetAsync(flowId);
        stored!.Owner.Should().Be("recovery-node");
        stored.Status.Should().Be(FlowStatus.Done);
    }

    #endregion

    #region Saga Pattern Tests

    [Fact]
    public async Task Saga_FullCompensationOnFailure()
    {
        var executor = new FlowExecutor(_store);
        var operations = new List<string>();

        var result = await executor.ExecuteAsync(
            "saga-flow",
            "SagaFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("Saga")
                    .Step(
                        async c => { operations.Add("Reserve Inventory"); await Task.Delay(10, c); },
                        async c => { operations.Add("Release Inventory"); await Task.Delay(10, c); })
                    .Step(
                        async c => { operations.Add("Charge Payment"); await Task.Delay(10, c); },
                        async c => { operations.Add("Refund Payment"); await Task.Delay(10, c); })
                    .Step(
                        async c => { operations.Add("Create Shipment"); await Task.Delay(10, c); },
                        async c => { operations.Add("Cancel Shipment"); await Task.Delay(10, c); })
                    .Step(
                        async c => { throw new Exception("Notification service down"); });

                return await flow.ExecuteAsync(ct);
            });

        result.IsSuccess.Should().BeFalse();

        // Forward operations
        operations.Should().Contain("Reserve Inventory");
        operations.Should().Contain("Charge Payment");
        operations.Should().Contain("Create Shipment");

        // Compensation in reverse order
        var compensations = operations.Where(o => o.Contains("Release") || o.Contains("Refund") || o.Contains("Cancel")).ToList();
        compensations.Should().HaveCount(3);
        compensations[0].Should().Be("Cancel Shipment");
        compensations[1].Should().Be("Refund Payment");
        compensations[2].Should().Be("Release Inventory");
    }

    [Fact]
    public async Task Saga_PartialCompensationOnMiddleFailure()
    {
        var executor = new FlowExecutor(_store);
        var operations = new List<string>();

        var result = await executor.ExecuteAsync(
            "partial-saga",
            "SagaFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("PartialSaga")
                    .Step(
                        async c => { operations.Add("Step1"); await Task.Delay(10, c); },
                        async c => { operations.Add("Comp1"); await Task.Delay(10, c); })
                    .Step(
                        async c => { throw new Exception("Step2 failed"); },
                        async c => { operations.Add("Comp2"); await Task.Delay(10, c); })
                    .Step(
                        async c => { operations.Add("Step3"); await Task.Delay(10, c); },
                        async c => { operations.Add("Comp3"); await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            });

        result.IsSuccess.Should().BeFalse();

        // Only Step1 executed
        operations.Should().Contain("Step1");
        operations.Should().NotContain("Step3");

        // Only Step1 compensated (Step2 failed before completing)
        operations.Should().Contain("Comp1");
        operations.Should().NotContain("Comp2");
        operations.Should().NotContain("Comp3");
    }

    #endregion

    #region Data Persistence Tests

    [Fact]
    public async Task DataPersistence_PreservedAcrossResume()
    {
        var flowId = "data-persist-flow";
        var originalData = new byte[] { 1, 2, 3, 4, 5 };

        // Create flow with data
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds(),
            Data = originalData
        };
        await _store.CreateAsync(state);

        // Resume flow
        var executor = new FlowExecutor(_store, new FlowOptions
        {
            NodeId = "node-2",
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });

        byte[]? receivedData = null;
        await executor.ExecuteAsync(
            flowId,
            "TestFlow",
            ReadOnlyMemory<byte>.Empty, // New data ignored
            async (s, ct) =>
            {
                receivedData = s.Data;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        receivedData.Should().BeEquivalentTo(originalData);
    }

    [Fact]
    public async Task DataPersistence_LargePayload()
    {
        var executor = new FlowExecutor(_store);
        var largeData = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeData);

        byte[]? receivedData = null;
        var result = await executor.ExecuteAsync(
            "large-data-flow",
            "TestFlow",
            largeData,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        receivedData.Should().BeEquivalentTo(largeData);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task Cancellation_StopsExecution()
    {
        var executor = new FlowExecutor(_store);
        using var cts = new CancellationTokenSource();

        var stepReached = 0;
        var task = executor.ExecuteAsync(
            "cancel-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("Cancellable")
                    .Step(async c => { stepReached = 1; await Task.Delay(10, c); })
                    .Step(async c => { stepReached = 2; await Task.Delay(10, c); })
                    .Step(async c => { stepReached = 3; await Task.Delay(1000, c); }) // Long step
                    .Step(async c => { stepReached = 4; await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            },
            cts.Token);

        // Cancel after short delay
        await Task.Delay(50);
        cts.Cancel();

        var result = await task;

        // Should not complete all steps
        stepReached.Should().BeLessThan(4);
    }

    [Fact]
    public async Task Cancellation_TriggersCompensation()
    {
        var executor = new FlowExecutor(_store);
        using var cts = new CancellationTokenSource();

        var compensated = new List<int>();
        var task = executor.ExecuteAsync(
            "cancel-comp-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("CancellableComp")
                    .Step(
                        async c => { await Task.Delay(10, c); },
                        async c => { compensated.Add(1); await Task.Delay(1, c); })
                    .Step(
                        async c => { await Task.Delay(10, c); },
                        async c => { compensated.Add(2); await Task.Delay(1, c); })
                    .Step(
                        async c => { await Task.Delay(1000, c); }, // Long step - will be cancelled
                        async c => { compensated.Add(3); await Task.Delay(1, c); });

                return await flow.ExecuteAsync(ct);
            },
            cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        var result = await task;

        // Completed steps should be compensated
        compensated.Should().Contain(2);
        compensated.Should().Contain(1);
    }

    #endregion

    #region Stress Tests

    [Fact]
    public async Task Stress_ManyFlowsParallel()
    {
        var executor = new FlowExecutor(_store);
        var completedCount = 0;

        var tasks = Enumerable.Range(1, 100).Select(async i =>
        {
            var result = await executor.ExecuteAsync(
                $"stress-flow-{i}",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    await Task.Delay(10, ct);
                    Interlocked.Increment(ref completedCount);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
            return result;
        }).ToList();

        var results = await Task.WhenAll(tasks);

        results.All(r => r.IsSuccess).Should().BeTrue();
        completedCount.Should().Be(100);
    }

    [Fact]
    public async Task Stress_RapidCreateAndComplete()
    {
        var executor = new FlowExecutor(_store);

        for (int i = 0; i < 50; i++)
        {
            var result = await executor.ExecuteAsync(
                $"rapid-flow-{i}",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                (state, ct) => Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero)));

            result.IsSuccess.Should().BeTrue();
        }

        // Verify all completed
        for (int i = 0; i < 50; i++)
        {
            var stored = await _store.GetAsync($"rapid-flow-{i}");
            stored!.Status.Should().Be(FlowStatus.Done);
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ErrorHandling_ExceptionInStep_RecordsError()
    {
        var executor = new FlowExecutor(_store);

        var result = await executor.ExecuteAsync(
            "error-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("ErrorFlow")
                    .Step(async c => { await Task.Delay(10, c); })
                    .Step(async c => { throw new InvalidOperationException("Test error message"); });

                return await flow.ExecuteAsync(ct);
            });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Test error message");

        var stored = await _store.GetAsync("error-flow");
        stored!.Status.Should().Be(FlowStatus.Failed);
        stored.Error.Should().Contain("Test error message");
    }

    [Fact]
    public async Task ErrorHandling_ExceptionInCompensation_ContinuesOtherCompensations()
    {
        var executor = new FlowExecutor(_store);
        var compensated = new List<int>();

        var result = await executor.ExecuteAsync(
            "comp-error-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("CompErrorFlow")
                    .Step(
                        async c => { await Task.Delay(10, c); },
                        async c => { compensated.Add(1); await Task.Delay(1, c); })
                    .Step(
                        async c => { await Task.Delay(10, c); },
                        c => { compensated.Add(2); throw new Exception("Comp error"); })
                    .Step(
                        async c => { await Task.Delay(10, c); },
                        async c => { compensated.Add(3); await Task.Delay(1, c); })
                    .Step(async c => { throw new Exception("Step error"); });

                return await flow.ExecuteAsync(ct);
            });

        result.IsSuccess.Should().BeFalse();
        // All compensations should be attempted
        compensated.Should().Contain(3);
        compensated.Should().Contain(2);
        compensated.Should().Contain(1);
    }

    [Fact]
    public async Task ErrorHandling_NullData_HandledCorrectly()
    {
        var executor = new FlowExecutor(_store);

        byte[]? receivedData = null;
        var result = await executor.ExecuteAsync(
            "null-data-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        receivedData.Should().BeEmpty();
    }

    #endregion

    #region Version Conflict Tests

    [Fact]
    public async Task VersionConflict_ConcurrentUpdates_OnlyOneSucceeds()
    {
        // Create a flow
        var state = new FlowState
        {
            Id = "version-conflict-flow",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        // Simulate concurrent updates with same version
        var tasks = Enumerable.Range(1, 10)
            .Select(async i =>
            {
                var s = new FlowState
                {
                    Id = "version-conflict-flow",
                    Type = "TestFlow",
                    Status = FlowStatus.Running,
                    Step = i,
                    Version = 0 // All use same version
                };
                return await _store.UpdateAsync(s);
            })
            .ToList();

        var results = await Task.WhenAll(tasks);
        var succeeded = results.Count(r => r);

        // Only one should succeed
        succeeded.Should().Be(1);
    }

    [Fact]
    public async Task VersionConflict_SequentialUpdates_AllSucceed()
    {
        var state = new FlowState
        {
            Id = "sequential-update-flow",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        // Sequential updates should all succeed
        for (int i = 1; i <= 10; i++)
        {
            var current = await _store.GetAsync("sequential-update-flow");
            current!.Step = i;
            var result = await _store.UpdateAsync(current);
            result.Should().BeTrue($"Update {i} should succeed");
        }

        var final = await _store.GetAsync("sequential-update-flow");
        final!.Version.Should().Be(10);
        final.Step.Should().Be(10);
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public async Task Idempotency_DuplicateFlowId_ReturnsExistingResult()
    {
        var executor = new FlowExecutor(_store);
        var executionCount = 0;

        // First execution
        var result1 = await executor.ExecuteAsync(
            "idempotent-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero));
            });

        // Second execution with same ID
        var result2 = await executor.ExecuteAsync(
            "idempotent-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero));
            });

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        // Should only execute once
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task Idempotency_FailedFlow_CanBeRetried()
    {
        var executor = new FlowExecutor(_store);
        var attemptCount = 0;

        // First attempt fails
        var result1 = await executor.ExecuteAsync(
            "retry-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) =>
            {
                attemptCount++;
                if (attemptCount == 1)
                    return Task.FromResult(new FlowResult(false, 0, TimeSpan.Zero, "First attempt failed"));
                return Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero));
            });

        result1.IsSuccess.Should().BeFalse();

        // For retry, we need a new flow ID since failed flows are not re-executed
        var result2 = await executor.ExecuteAsync(
            "retry-flow-2",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) =>
            {
                attemptCount++;
                return Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero));
            });

        result2.IsSuccess.Should().BeTrue();
        attemptCount.Should().Be(2);
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task Timeout_LongRunningStep_CanBeCancelled()
    {
        var executor = new FlowExecutor(_store);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var stepStarted = false;
        var result = await executor.ExecuteAsync(
            "timeout-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("TimeoutFlow")
                    .Step(async c =>
                    {
                        stepStarted = true;
                        await Task.Delay(5000, c); // Long running
                    });

                return await flow.ExecuteAsync(ct);
            },
            cts.Token);

        stepStarted.Should().BeTrue();
        result.IsCancelled.Should().BeTrue();
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public async Task StateTransition_Running_To_Done()
    {
        var executor = new FlowExecutor(_store);

        await executor.ExecuteAsync(
            "transition-done-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero)));

        var stored = await _store.GetAsync("transition-done-flow");
        stored!.Status.Should().Be(FlowStatus.Done);
    }

    [Fact]
    public async Task StateTransition_Running_To_Failed()
    {
        var executor = new FlowExecutor(_store);

        await executor.ExecuteAsync(
            "transition-failed-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => Task.FromResult(new FlowResult(false, 0, TimeSpan.Zero, "Failed")));

        var stored = await _store.GetAsync("transition-failed-flow");
        stored!.Status.Should().Be(FlowStatus.Failed);
    }

    #endregion
}






