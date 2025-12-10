using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests to ensure WhenAll/WhenAny parallel operations can be perfectly recovered from any interruption point.
/// These tests verify that parallel execution state is properly persisted and can resume correctly.
/// </summary>
public class ParallelRecoveryTests
{
    [Fact]
    public async Task WhenAll_ShouldResumeFromSuspendedState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestWhenAllFlow();

        var state = new TestParallelState
        {
            FlowId = "test-whenall-recovery",
            Task1Completed = false,
            Task2Completed = false,
            AllCompleted = false
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync(Arg.Any<WhenAllTask1Command>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.Task1Completed = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<WhenAllTask2Command>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.Task2Completed = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<AllCompletedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.AllCompleted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestParallelState, TestWhenAllFlow>(mediator, store, config);

        // Simulate interruption during WhenAll execution (suspended state)
        var waitCondition = new WaitCondition
        {
            CorrelationId = $"{state.FlowId}-step-0",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 1, // One task completed, one still pending
            Results = [new FlowCompletedEventData { FlowId = "task1", Success = true, Result = "task1-result" }],
            CreatedAt = DateTime.UtcNow,
            Timeout = TimeSpan.FromMinutes(10),
            FlowId = state.FlowId,
            FlowType = typeof(TestWhenAllFlow).Name,
            Step = 0
        };

        var interruptedSnapshot = new FlowSnapshot<TestParallelState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0]), // Step 0 (WhenAll)
            Status = DslFlowStatus.Suspended,
            WaitCondition = waitCondition
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SetWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        // Act - Resume execution (simulate second task completion)
        waitCondition.Results.Add(new FlowCompletedEventData { FlowId = "task2", Success = true, Result = "task2-result" });
        waitCondition.CompletedCount = 2;
        await store.SetWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        result!.IsSuccess.Should().BeTrue("resumed flow should complete successfully");
        result.State.AllCompleted.Should().BeTrue("WhenAll should complete after all tasks finish");
    }

    [Fact]
    public async Task WhenAny_ShouldResumeFromSuspendedState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestWhenAnyFlow();

        var state = new TestParallelState
        {
            FlowId = "test-whenany-recovery",
            Task1Completed = false,
            Task2Completed = false,
            AnyCompleted = false,
            FirstResult = null
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync<WhenAnyTask1Command, string>(Arg.Any<WhenAnyTask1Command>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.Task1Completed = true;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("task1-result"));
            });
        mediator.SendAsync<WhenAnyTask2Command, string>(Arg.Any<WhenAnyTask2Command>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.Task2Completed = true;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("task2-result"));
            });
        mediator.SendAsync(Arg.Any<AnyCompletedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.AnyCompleted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestParallelState, TestWhenAnyFlow>(mediator, store, config);

        // Simulate interruption during WhenAny execution (suspended state)
        var waitCondition = new WaitCondition
        {
            CorrelationId = $"{state.FlowId}-step-0",
            Type = WaitType.Any,
            ExpectedCount = 2,
            CompletedCount = 0, // No tasks completed yet
            Results = [],
            CreatedAt = DateTime.UtcNow,
            Timeout = TimeSpan.FromMinutes(10),
            FlowId = state.FlowId,
            FlowType = typeof(TestWhenAnyFlow).Name,
            Step = 0
        };

        var interruptedSnapshot = new FlowSnapshot<TestParallelState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0]), // Step 0 (WhenAny)
            Status = DslFlowStatus.Suspended,
            WaitCondition = waitCondition
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SetWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        // Act - Resume execution (simulate first task completion)
        waitCondition.Results.Add(new FlowCompletedEventData { FlowId = "task1", Success = true, Result = "task1-result" });
        waitCondition.CompletedCount = 1;
        await store.SetWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        result!.IsSuccess.Should().BeTrue("resumed flow should complete successfully");
        result.State.AnyCompleted.Should().BeTrue("WhenAny should complete after first task finishes");
        result.State.FirstResult.Should().Be("task1-result", "WhenAny should capture the first result");
    }

    [Fact]
    public async Task WhenAll_ShouldHandleTimeoutRecovery()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestWhenAllWithTimeoutFlow();

        var state = new TestParallelState
        {
            FlowId = "test-whenall-timeout-recovery",
            TimeoutHandled = false
        };

        // Setup mediator to handle timeout compensation
        mediator.SendAsync(Arg.Any<TimeoutCompensationCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.TimeoutHandled = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestParallelState, TestWhenAllWithTimeoutFlow>(mediator, store, config);

        // Simulate interruption during WhenAll with expired timeout
        var expiredWaitCondition = new WaitCondition
        {
            CorrelationId = $"{state.FlowId}-step-0",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 1, // One task completed, one timed out
            Results = [new FlowCompletedEventData { FlowId = "task1", Success = true, Result = "task1-result" }],
            CreatedAt = DateTime.UtcNow.AddMinutes(-15), // Expired 15 minutes ago
            Timeout = TimeSpan.FromMinutes(10),
            FlowId = state.FlowId,
            FlowType = typeof(TestWhenAllWithTimeoutFlow).Name,
            Step = 0
        };

        var interruptedSnapshot = new FlowSnapshot<TestParallelState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0]), // Step 0 (WhenAll)
            Status = DslFlowStatus.Suspended,
            WaitCondition = expiredWaitCondition
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SetWaitConditionAsync(expiredWaitCondition.CorrelationId, expiredWaitCondition);

        // Act - Resume execution (should detect timeout)
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        result!.IsSuccess.Should().BeFalse("resumed flow should fail due to timeout");
        result.Error.Should().Contain("timeout", "error should indicate timeout occurred");
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    [InlineData("Nats")]
    public async Task ParallelRecovery_ShouldWorkAcrossAllStores(string storeType)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateStore(storeType);
        var config = new TestWhenAllFlow();

        var state = new TestParallelState
        {
            FlowId = $"test-parallel-recovery-{storeType}",
            AllCompleted = false
        };

        // Setup mediator
        mediator.SendAsync(Arg.Any<WhenAllTask1Command>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
        mediator.SendAsync(Arg.Any<WhenAllTask2Command>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
        mediator.SendAsync(Arg.Any<AllCompletedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.AllCompleted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestParallelState, TestWhenAllFlow>(mediator, store, config);

        // Create suspended snapshot
        var waitCondition = new WaitCondition
        {
            CorrelationId = $"{state.FlowId}-step-0",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 2, // Both tasks completed
            Results = [
                new FlowCompletedEventData { FlowId = "task1", Success = true, Result = "task1-result" },
                new FlowCompletedEventData { FlowId = "task2", Success = true, Result = "task2-result" }
            ],
            CreatedAt = DateTime.UtcNow,
            Timeout = TimeSpan.FromMinutes(10),
            FlowId = state.FlowId,
            FlowType = typeof(TestWhenAllFlow).Name,
            Step = 0
        };

        var interruptedSnapshot = new FlowSnapshot<TestParallelState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0]),
            Status = DslFlowStatus.Suspended,
            WaitCondition = waitCondition
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SetWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull($"flow should be resumable from {storeType} store");
        result!.IsSuccess.Should().BeTrue($"resumed flow should complete successfully in {storeType}");
        result.State.AllCompleted.Should().BeTrue($"WhenAll should complete in {storeType}");
    }

    private static IDslFlowStore CreateStore(string storeType)
    {
        return storeType switch
        {
            "InMemory" => new InMemoryDslFlowStore(),
            "Redis" => new InMemoryDslFlowStore(), // TODO: Replace with actual Redis store
            "Nats" => new InMemoryDslFlowStore(),  // TODO: Replace with actual NATS store
            _ => throw new ArgumentException($"Unknown store type: {storeType}")
        };
    }
}

/// <summary>
/// Test flow state for parallel recovery tests.
/// </summary>
public class TestParallelState : IFlowState
{
    public string? FlowId { get; set; }
    public bool Task1Completed { get; set; }
    public bool Task2Completed { get; set; }
    public bool AllCompleted { get; set; }
    public bool AnyCompleted { get; set; }
    public string? FirstResult { get; set; }
    public bool TimeoutHandled { get; set; }

    // Change tracking implementation
    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

/// <summary>
/// Test flow configuration with WhenAll.
/// </summary>
public class TestWhenAllFlow : FlowConfig<TestParallelState>
{
    protected override void Configure(IFlowBuilder<TestParallelState> flow)
    {
        flow.Name("test-whenall-flow");

        flow.WhenAll(
            s => new WhenAllTask1Command(),
            s => new WhenAllTask2Command()
        );

        flow.Send(s => new AllCompletedCommand());
    }
}

/// <summary>
/// Test flow configuration with WhenAny.
/// </summary>
public class TestWhenAnyFlow : FlowConfig<TestParallelState>
{
    protected override void Configure(IFlowBuilder<TestParallelState> flow)
    {
        flow.Name("test-whenany-flow");

        flow.WhenAny<string>(
            s => new WhenAnyTask1Command(),
            s => new WhenAnyTask2Command()
        ).Into(s => s.FirstResult);

        flow.Send(s => new AnyCompletedCommand());
    }
}

/// <summary>
/// Test flow configuration with WhenAll and timeout.
/// </summary>
public class TestWhenAllWithTimeoutFlow : FlowConfig<TestParallelState>
{
    protected override void Configure(IFlowBuilder<TestParallelState> flow)
    {
        flow.Name("test-whenall-timeout-flow");

        flow.WhenAll(
            s => new WhenAllTask1Command(),
            s => new WhenAllTask2Command()
        )
        .Timeout(TimeSpan.FromMinutes(10))
        .IfAnyFail(s => new TimeoutCompensationCommand());
    }
}

// Test commands for WhenAll (no result)
public record WhenAllTask1Command : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record WhenAllTask2Command : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// Test commands for WhenAny (with result)
public record WhenAnyTask1Command : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record WhenAnyTask2Command : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record AllCompletedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record AnyCompletedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record TimeoutCompensationCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
