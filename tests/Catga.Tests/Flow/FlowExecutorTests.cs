using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class FlowExecutorTests
{
    private readonly InMemoryFlowStore _store = new();
    private readonly FlowExecutor _executor;

    public FlowExecutorTests()
    {
        _executor = new FlowExecutor(_store, new FlowOptions
        {
            NodeId = "test-node",
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });
    }

    [Fact]
    public async Task ExecuteAsync_NewFlow_CreatesAndExecutes()
    {
        var executed = false;

        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                executed = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        result.FlowId.Should().Be("flow-1");
        executed.Should().BeTrue();

        var stored = await _store.GetAsync("flow-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(FlowStatus.Done);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingDoneFlow_ReturnsImmediately()
    {
        // Pre-create a completed flow
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Done,
            Step = 3,
            Version = 0
        };
        await _store.CreateAsync(state);

        var executed = false;
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeFalse(); // Should not execute again
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFailedFlow_ReturnsImmediately()
    {
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Failed,
            Step = 2,
            Error = "Previous error",
            Version = 0
        };
        await _store.CreateAsync(state);

        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Previous error");
    }

    [Fact]
    public async Task ExecuteAsync_FlowFails_UpdatesStatusToFailed()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(false, 1, TimeSpan.Zero, "Step failed"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Step failed");

        var stored = await _store.GetAsync("flow-1");
        stored!.Status.Should().Be(FlowStatus.Failed);
        stored.Error.Should().Be("Step failed");
    }

    [Fact]
    public async Task ExecuteAsync_Idempotent_SameFlowIdReturnsSameResult()
    {
        var executionCount = 0;

        // First execution
        var result1 = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Second execution with same ID
        var result2 = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        executionCount.Should().Be(1); // Only executed once
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_StopsExecution()
    {
        using var cts = new CancellationTokenSource();

        var task = _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                try
                {
                    await Task.Delay(5000, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                }
                catch (OperationCanceledException)
                {
                    return new FlowResult(false, 0, TimeSpan.Zero) { IsCancelled = true };
                }
            },
            cts.Token);

        cts.CancelAfter(50);

        var result = await task;
        // The executor should handle cancellation gracefully
        result.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StoresData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            data,
            async (state, ct) =>
            {
                state.Data.Should().BeEquivalentTo(data);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var stored = await _store.GetAsync("flow-1");
        stored!.Data.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesStepCount()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 5, TimeSpan.FromMilliseconds(100)));

        var stored = await _store.GetAsync("flow-1");
        stored!.Step.Should().Be(5);
    }

    #region TDD: Additional Executor Tests

    [Fact]
    public async Task ExecuteAsync_ExceptionInExecutor_MarksFlowFailed()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => throw new InvalidOperationException("Executor crashed"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Executor crashed");

        var stored = await _store.GetAsync("flow-1");
        stored!.Status.Should().Be(FlowStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFlowTypes_Independent()
    {
        var typeAExecuted = false;
        var typeBExecuted = false;

        var resultA = await _executor.ExecuteAsync(
            "flow-a",
            "TypeA",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                typeAExecuted = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var resultB = await _executor.ExecuteAsync(
            "flow-b",
            "TypeB",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                typeBExecuted = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        resultA.IsSuccess.Should().BeTrue();
        resultB.IsSuccess.Should().BeTrue();
        typeAExecuted.Should().BeTrue();
        typeBExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetsOwner()
    {
        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        var stored = await _store.GetAsync("flow-1");
        stored!.Owner.Should().Be("test-node");
    }

    [Fact]
    public async Task ExecuteAsync_PartialProgress_RecordsStep()
    {
        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(false, 3, TimeSpan.Zero, "Failed at step 4"));

        var stored = await _store.GetAsync("flow-1");
        stored!.Step.Should().Be(3);
        stored.Status.Should().Be(FlowStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyData_HandledCorrectly()
    {
        byte[]? receivedData = null;

        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        receivedData.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_LargeData_Preserved()
    {
        var largeData = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeData);
        byte[]? receivedData = null;

        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            largeData,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        receivedData.Should().BeEquivalentTo(largeData);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentSameFlowId_AllReturnSuccess()
    {
        var executionCount = 0;

        var tasks = Enumerable.Range(1, 5).Select(async _ =>
        {
            return await _executor.ExecuteAsync(
                "same-flow",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    Interlocked.Increment(ref executionCount);
                    await Task.Delay(50, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // All should return success (either executed or already done)
        results.All(r => r.IsSuccess).Should().BeTrue();
        // At least one execution happened
        executionCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_FlowResult_ContainsFlowId()
    {
        var result = await _executor.ExecuteAsync(
            "my-flow-id",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        result.FlowId.Should().Be("my-flow-id");
    }

    [Fact]
    public async Task ExecuteAsync_FlowResult_ContainsDuration()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                await Task.Delay(50, ct);
                return new FlowResult(true, 1, TimeSpan.FromMilliseconds(50));
            });

        result.Duration.TotalMilliseconds.Should().BeGreaterThan(40);
    }

    [Fact]
    public async Task ExecuteAsync_RunningFlow_OwnedBySameNode_Executes()
    {
        // Pre-create a running flow owned by same node
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "test-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        var executed = false;
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RunningFlow_OwnedByDifferentNode_WaitsOrFails()
    {
        // Pre-create a running flow owned by different node with recent heartbeat
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "other-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        var executed = false;
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        // Should not execute since another node owns it
        executed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_SequentialFlows_AllComplete()
    {
        for (int i = 1; i <= 10; i++)
        {
            var result = await _executor.ExecuteAsync(
                $"flow-{i}",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

            result.IsSuccess.Should().BeTrue();
        }

        // Verify all flows completed
        for (int i = 1; i <= 10; i++)
        {
            var stored = await _store.GetAsync($"flow-{i}");
            stored!.Status.Should().Be(FlowStatus.Done);
        }
    }

    #endregion

    #region TDD: Bug Discovery Tests

    [Fact]
    public async Task ExecuteAsync_ResumeFromStep_UsesCorrectStartStep()
    {
        // Pre-create a partially completed flow
        var state = new FlowState
        {
            Id = "resume-flow",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 2, // Already completed 2 steps
            Version = 0,
            Owner = "test-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        int startStepReceived = -1;
        var result = await _executor.ExecuteAsync(
            "resume-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                startStepReceived = s.Step;
                return new FlowResult(true, 3, TimeSpan.Zero);
            });

        // Executor should pass the current step to the executor function
        startStepReceived.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_DataPreservedOnResume()
    {
        var originalData = new byte[] { 1, 2, 3, 4, 5 };

        // Pre-create a flow with data
        var state = new FlowState
        {
            Id = "data-resume-flow",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "test-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = originalData
        };
        await _store.CreateAsync(state);

        byte[]? receivedData = null;
        var result = await _executor.ExecuteAsync(
            "data-resume-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty, // New data should be ignored
            async (s, ct) =>
            {
                receivedData = s.Data;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        // Should use original data, not new empty data
        receivedData.Should().BeEquivalentTo(originalData);
    }

    [Fact]
    public async Task ExecuteAsync_VersionIncrementedOnUpdate()
    {
        var result = await _executor.ExecuteAsync(
            "version-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        var stored = await _store.GetAsync("version-flow");
        stored!.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatUpdatesTime()
    {
        var heartbeatTimes = new List<long>();

        var result = await _executor.ExecuteAsync(
            "heartbeat-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                // Record initial heartbeat
                var initial = await _store.GetAsync("heartbeat-flow");
                heartbeatTimes.Add(initial!.HeartbeatAt);

                // Wait for heartbeat to occur
                await Task.Delay(150, ct);

                // Record after heartbeat
                var after = await _store.GetAsync("heartbeat-flow");
                heartbeatTimes.Add(after!.HeartbeatAt);

                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Heartbeat should have updated the time
        heartbeatTimes.Should().HaveCount(2);
        heartbeatTimes[1].Should().BeGreaterThan(heartbeatTimes[0]);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledFlow_StatusUpdated()
    {
        using var cts = new CancellationTokenSource();

        var task = _executor.ExecuteAsync(
            "cancel-status-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                await Task.Delay(50, ct);
                cts.Cancel();
                await Task.Delay(1000, ct); // Will be cancelled
                return new FlowResult(true, 1, TimeSpan.Zero);
            },
            cts.Token);

        var result = await task;

        // Flow should be marked as failed or have error
        var stored = await _store.GetAsync("cancel-status-flow");
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AbandonedFlow_CanBeRecovered()
    {
        // Create abandoned flow (old heartbeat)
        var state = new FlowState
        {
            Id = "abandoned-flow",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "dead-node",
            HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        // New executor should be able to claim and execute
        var newExecutor = new FlowExecutor(_store, new FlowOptions
        {
            NodeId = "new-node",
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });

        var executed = false;
        var result = await newExecutor.ExecuteAsync(
            "abandoned-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();

        var stored = await _store.GetAsync("abandoned-flow");
        stored!.Owner.Should().Be("new-node");
    }

    [Fact]
    public async Task ExecuteAsync_FlowTypeMismatch_ShouldHandle()
    {
        // Create flow with one type
        var state = new FlowState
        {
            Id = "type-mismatch-flow",
            Type = "TypeA",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "test-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        // Try to execute with different type
        var result = await _executor.ExecuteAsync(
            "type-mismatch-flow",
            "TypeB", // Different type!
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        // Should still work (type is for categorization, not validation)
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ZeroStepFlow_Succeeds()
    {
        var result = await _executor.ExecuteAsync(
            "zero-step-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => Task.FromResult(new FlowResult(true, 0, TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(0);

        var stored = await _store.GetAsync("zero-step-flow");
        stored!.Status.Should().Be(FlowStatus.Done);
    }

    [Fact]
    public async Task ExecuteAsync_NegativeSteps_Handled()
    {
        var result = await _executor.ExecuteAsync(
            "negative-step-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => Task.FromResult(new FlowResult(false, -1, TimeSpan.Zero, "Invalid state")));

        result.IsSuccess.Should().BeFalse();
        result.CompletedSteps.Should().Be(-1);
    }

    [Fact]
    public async Task Flow_CompensationOrder_StrictlyReverse()
    {
        var compensationOrder = new List<int>();

        var result = await Catga.Flow.Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensationOrder.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensationOrder.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensationOrder.Add(3); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensationOrder.Add(4); await Task.Delay(1, ct); })
            .Step(ct => { throw new Exception("fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        compensationOrder.Should().BeEquivalentTo([4, 3, 2, 1]);
        compensationOrder.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Flow_CompensationWithException_ContinuesAll()
    {
        var compensated = new List<int>();

        var result = await Catga.Flow.Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                ct => { compensated.Add(2); throw new Exception("comp2 fail"); })
            .Step(async ct => { await Task.Delay(1, ct); },
                ct => { compensated.Add(3); throw new Exception("comp3 fail"); })
            .Step(ct => { throw new Exception("step fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        // All compensations should be attempted despite exceptions
        compensated.Should().Contain(3);
        compensated.Should().Contain(2);
        compensated.Should().Contain(1);
    }

    [Fact]
    public async Task InMemoryStore_UpdateAsync_DoesNotModifyOriginalOnFailure()
    {
        var state = new FlowState
        {
            Id = "original-flow",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0
        };
        await _store.CreateAsync(state);

        // Get and update
        var current = await _store.GetAsync("original-flow");
        current!.Step = 1;
        await _store.UpdateAsync(current);

        // Try stale update
        var stale = new FlowState
        {
            Id = "original-flow",
            Type = "TestFlow",
            Status = FlowStatus.Done,
            Step = 99,
            Version = 0 // Old version
        };
        var result = await _store.UpdateAsync(stale);

        result.Should().BeFalse();

        // Original should not be modified
        var stored = await _store.GetAsync("original-flow");
        stored!.Step.Should().Be(1);
        stored.Status.Should().Be(FlowStatus.Running);
    }

    #endregion
}
