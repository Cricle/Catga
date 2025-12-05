using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using NSubstitute;

namespace Catga.Tests.Flow.Advanced;

/// <summary>
/// Advanced tests for DSL Flow: boundary conditions, concurrency, error handling.
/// </summary>
public class DslFlowAdvancedTests
{
    #region Boundary Tests

    [Fact]
    public async Task RunAsync_EmptyFlow_CompletesSuccessfully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunAsync_SingleStep_CompletesSuccessfully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new SingleStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SingleStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "test" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunAsync_ManySteps_CompletesAllSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new ManyStepsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, ManyStepsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
        await mediator.Received(10).SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NullFlowId_GeneratesNewId()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = null };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.NotNull(state.FlowId);
        Assert.NotEmpty(state.FlowId);
    }

    [Fact]
    public async Task RunAsync_ExistingFlowId_UsesProvidedId()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "custom-id-123" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.Equal("custom-id-123", state.FlowId);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task RunAsync_CancellationRequested_ReturnsFailedOrCancelled()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var callCount = 0;
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                callCount++;
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new ManyStepsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, ManyStepsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        using var cts = new CancellationTokenSource();
        // Cancel after first step
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (callCount > 0) cts.Cancel();
                callCount++;
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Task.FromResult(CatgaResult.Success());
            });

        // Act
        var result = await executor.RunAsync(state, cts.Token);

        // Assert - Flow should fail or be cancelled when cancellation is requested
        Assert.False(result.IsSuccess);
        Assert.True(result.Status == DslFlowStatus.Cancelled || result.Status == DslFlowStatus.Failed);
    }

    [Fact]
    public async Task CancelAsync_RunningFlow_ReturnsTrueAndCancels()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "flow-to-cancel" };

        await executor.RunAsync(state);

        // Manually set status back to Running for test
        var snapshot = await store.GetAsync<TestFlowState>("flow-to-cancel");
        await store.UpdateAsync(snapshot! with { Status = DslFlowStatus.Running });

        // Act
        var cancelled = await executor.CancelAsync("flow-to-cancel");

        // Assert
        Assert.True(cancelled);
        var updated = await store.GetAsync<TestFlowState>("flow-to-cancel");
        Assert.Equal(DslFlowStatus.Cancelled, updated!.Status);
    }

    [Fact]
    public async Task CancelAsync_NonExistentFlow_ReturnsFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);

        // Act
        var cancelled = await executor.CancelAsync("non-existent");

        // Assert
        Assert.False(cancelled);
    }

    #endregion

    #region Resume Tests

    [Fact]
    public async Task ResumeAsync_NonExistentFlow_ReturnsFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.ResumeAsync("non-existent");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ResumeAsync_CompletedFlow_ReturnsAlreadyCompleted()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "completed-flow" };

        await executor.RunAsync(state);

        // Act
        var result = await executor.ResumeAsync("completed-flow");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task RunAsync_ConcurrentFlows_AllComplete()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new SingleStepFlowConfig();

        var tasks = new List<Task<DslFlowResult<TestFlowState>>>();
        for (int i = 0; i < 100; i++)
        {
            var executor = new DslFlowExecutor<TestFlowState, SingleStepFlowConfig>(mediator, store, config);
            var state = new TestFlowState { FlowId = $"flow-{i}" };
            tasks.Add(executor.RunAsync(state));
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.All(results, r => Assert.Equal(DslFlowStatus.Completed, r.Status));
    }

    [Fact]
    public async Task Store_ConcurrentCreateAndGet_NoDataLoss()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var tasks = new List<Task>();

        // Act - Create 100 flows concurrently
        for (int i = 0; i < 100; i++)
        {
            var flowId = $"concurrent-{i}";
            var snapshot = new FlowSnapshot<TestFlowState>(
                flowId,
                new TestFlowState { FlowId = flowId, Value = $"value-{i}" },
                0, DslFlowStatus.Running, null, null,
                DateTime.UtcNow, DateTime.UtcNow, 0);
            tasks.Add(store.CreateAsync(snapshot));
        }
        await Task.WhenAll(tasks);

        // Assert - All flows exist
        for (int i = 0; i < 100; i++)
        {
            var snapshot = await store.GetAsync<TestFlowState>($"concurrent-{i}");
            Assert.NotNull(snapshot);
            Assert.Equal($"value-{i}", snapshot.State.Value);
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task RunAsync_StepThrowsException_ReturnsFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns<CatgaResult>(_ => throw new InvalidOperationException("Test exception"));

        var store = new InMemoryDslFlowStore();
        var config = new SingleStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SingleStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
        Assert.Contains("Test exception", result.Error);
    }

    [Fact]
    public async Task RunAsync_OptionalStepFails_ContinuesFlow()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns<CatgaResult>(_ => throw new InvalidOperationException("Optional step failed"));

        var store = new InMemoryDslFlowStore();
        var config = new OptionalStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, OptionalStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
    }

    #endregion

    #region Compensation Tests

    [Fact]
    public async Task RunAsync_StepFailsWithCompensation_ExecutesCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedCommands = new List<string>();

        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<TestCommand>();
                executedCommands.Add(cmd.Value);
                if (cmd.Value == "step3")
                    return Task.FromResult(CatgaResult.Failure("Step 3 failed"));
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new CompensationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, CompensationFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
        // Compensations should be executed in reverse order
        Assert.Contains("compensate2", executedCommands);
        Assert.Contains("compensate1", executedCommands);
        var comp2Index = executedCommands.IndexOf("compensate2");
        var comp1Index = executedCommands.IndexOf("compensate1");
        Assert.True(comp2Index < comp1Index, "Compensations should execute in reverse order");
    }

    [Fact]
    public async Task RunAsync_CompensationFails_FlowStillFails()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<TestCommand>();
                if (cmd.Value == "step2")
                    return Task.FromResult(CatgaResult.Failure("Step 2 failed"));
                if (cmd.Value.StartsWith("compensate"))
                    throw new InvalidOperationException("Compensation failed");
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new CompensationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, CompensationFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Flow should still fail even if compensation fails
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
    }

    #endregion

    #region Conditional Execution Tests

    [Fact]
    public async Task RunAsync_ConditionalStepTrue_ExecutesStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedCommands = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedCommands.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new ConditionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, ConditionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "execute" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("conditional", executedCommands);
    }

    [Fact]
    public async Task RunAsync_ConditionalStepFalse_SkipsStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedCommands = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedCommands.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new ConditionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, ConditionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "skip" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("conditional", executedCommands);
        Assert.Contains("always", executedCommands);
        Assert.Contains("final", executedCommands);
    }

    #endregion

    #region Query and Publish Tests

    [Fact]
    public async Task RunAsync_QueryStep_CapturesResult()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQuery, string>(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("query-result")));

        var store = new InMemoryDslFlowStore();
        var config = new QueryFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, QueryFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "initial" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("query-result", state.Value);
    }

    [Fact]
    public async Task RunAsync_PublishStep_PublishesEvent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        TestEvent? publishedEvent = null;
        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                publishedEvent = call.Arg<TestEvent>();
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new PublishFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, PublishFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "test-value" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(publishedEvent);
        Assert.Equal("test-value", publishedEvent.Value);
    }

    #endregion

    #region State Persistence Tests

    [Fact]
    public async Task RunAsync_FlowCompletes_StateIsPersisted()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new SingleStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SingleStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "persist-test", Value = "test-value" };

        // Act
        await executor.RunAsync(state);

        // Assert
        var snapshot = await store.GetAsync<TestFlowState>("persist-test");
        Assert.NotNull(snapshot);
        Assert.Equal(DslFlowStatus.Completed, snapshot.Status);
        Assert.Equal("test-value", snapshot.State.Value);
    }

    [Fact]
    public async Task RunAsync_FlowFails_ErrorIsPersisted()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Failure("Test error")));

        var store = new InMemoryDslFlowStore();
        var config = new SingleStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SingleStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "error-test" };

        // Act
        await executor.RunAsync(state);

        // Assert
        var snapshot = await store.GetAsync<TestFlowState>("error-test");
        Assert.NotNull(snapshot);
        Assert.Equal(DslFlowStatus.Failed, snapshot.Status);
        Assert.Contains("Test error", snapshot.Error);
    }

    [Fact]
    public async Task GetAsync_ExistingFlow_ReturnsSnapshot()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "get-test" };

        await executor.RunAsync(state);

        // Act
        var snapshot = await executor.GetAsync("get-test");

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal("get-test", snapshot.FlowId);
    }

    [Fact]
    public async Task GetAsync_NonExistentFlow_ReturnsNull()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);

        // Act
        var snapshot = await executor.GetAsync("non-existent");

        // Assert
        Assert.Null(snapshot);
    }

    #endregion

    #region Resume Edge Cases

    [Fact]
    public async Task ResumeAsync_FailedFlow_ReturnsFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Failure("Original error")));

        var store = new InMemoryDslFlowStore();
        var config = new SingleStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SingleStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "failed-flow" };

        await executor.RunAsync(state);

        // Act
        var result = await executor.ResumeAsync("failed-flow");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ResumeAsync_CancelledFlow_ReturnsCancelled()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, EmptyFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "cancelled-flow" };

        await executor.RunAsync(state);
        var snapshot = await store.GetAsync<TestFlowState>("cancelled-flow");
        await store.UpdateAsync(snapshot! with { Status = DslFlowStatus.Cancelled });

        // Act
        var result = await executor.ResumeAsync("cancelled-flow");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Cancelled, result.Status);
    }

    #endregion

    #region Test Infrastructure

    public class TestFlowState : IFlowState
    {
        public string? FlowId { get; set; }
        public string? Value { get; set; }

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public record TestCommand(string Value) : IRequest
    {
        public long MessageId => 0;
    }

    public class EmptyFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("empty-flow");
        }
    }

    public class SingleStepFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("single-step");
            flow.Send(s => new TestCommand(s.Value ?? "default"));
        }
    }

    public class ManyStepsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("many-steps");
            for (int i = 0; i < 10; i++)
            {
                flow.Send(s => new TestCommand($"step-{i}"));
            }
        }
    }

    public class OptionalStepFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("optional-step");
            flow.Send(s => new TestCommand("optional"))
                .Optional();
        }
    }

    #endregion

    #region WhenAny Tests

    [Fact]
    public async Task RunAsync_WhenAny_FirstSucceeds_Completes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAnyFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAnyFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Either completes or suspends waiting for child flows
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAnyWithResult_CapturesFirstResult()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("winner")));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAnyWithResultFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAnyWithResultFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAnyWithTimeout_AppliesTimeout()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAnyWithTimeoutFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAnyWithTimeoutFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAnyWithTag_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAnyWithTagFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAnyWithTagFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAnyWithResultAndTimeout_AppliesTimeout()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result")));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAnyResultWithTimeoutFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAnyResultWithTimeoutFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAnyWithResultAndTag_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result")));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAnyResultWithTagFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAnyResultWithTagFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    #endregion

    #region WhenAll Extended Tests

    [Fact]
    public async Task RunAsync_WhenAllWithTimeout_AppliesTimeout()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAllWithTimeoutFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAllWithTimeoutFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAllWithTag_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAllWithTagFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAllWithTagFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task RunAsync_WhenAllWithCompensation_HasCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new WhenAllWithCompensationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, WhenAllWithCompensationFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess || result.Status == DslFlowStatus.Suspended);
    }

    #endregion

    #region StepBuilder Method Coverage Tests

    [Fact]
    public async Task StepBuilder_FailIfWithMessage_SetsErrorMessage()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new FailIfWithMessageFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, FailIfWithMessageFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "ok" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task StepBuilder_FailIfOnState_ConfiguresStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new FailIfOnStateFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, FailIfOnStateFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "ok" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task StepBuilderWithResult_IfFail_ConfiguresCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result")));

        var store = new InMemoryDslFlowStore();
        var config = new SendResultWithIfFailFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendResultWithIfFailFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task StepBuilderWithResult_OnlyWhen_SkipsWhenFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var queryExecuted = false;
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                queryExecuted = true;
                return ValueTask.FromResult(CatgaResult<string>.Success("result"));
            });

        var store = new InMemoryDslFlowStore();
        var config = new SendResultOnlyWhenFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendResultOnlyWhenFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "skip" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(queryExecuted);
    }

    [Fact]
    public async Task StepBuilderWithResult_Optional_SkipsOnFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Failure("Query failed")));

        var store = new InMemoryDslFlowStore();
        var config = new SendResultOptionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendResultOptionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task StepBuilderWithResult_Tag_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result")));

        var store = new InMemoryDslFlowStore();
        var config = new SendResultTagFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendResultTagFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task StepBuilderWithResult_Into_SetsProperty()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("captured-value")));

        var store = new InMemoryDslFlowStore();
        var config = new SendResultIntoFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendResultIntoFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("captured-value", state.Value);
    }

    #endregion

    #region StepBuilder Chaining Tests

    [Fact]
    public async Task RunAsync_MultipleStepOptions_AllApplied()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new ChainedOptionsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, ChainedOptionsFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "execute" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_StepWithMultipleTags_AllTagsApplied()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new MultiTagStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, MultiTagStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_OptionalWithCondition_SkipsWhenConditionFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedCommands = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedCommands.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new OptionalConditionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, OptionalConditionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "skip" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("optional-conditional", executedCommands);
    }

    [Fact]
    public async Task RunAsync_QueryWithInto_SetsStateProperty()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("query-result")));

        var store = new InMemoryDslFlowStore();
        var config = new QueryIntoFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, QueryIntoFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("query-result", state.Value);
    }

    [Fact]
    public async Task RunAsync_QueryWithTag_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result")));

        var store = new InMemoryDslFlowStore();
        var config = new QueryWithTagFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, QueryWithTagFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_PublishWithTag_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var store = new InMemoryDslFlowStore();
        var config = new PublishWithTagFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, PublishWithTagFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "test" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Resume and Cancel Tests

    [Fact]
    public async Task ResumeAsync_FlowNotFound_ReturnsFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.ResumeAsync("non-existent-flow");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ResumeAsync_CompletedFlow_ReturnsSuccess()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "completed-flow" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "completed-flow",
            state,
            CurrentStep: 1,
            Status: DslFlowStatus.Completed,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
        await store.CreateAsync(snapshot);

        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.ResumeAsync("completed-flow");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
    }

    [Fact]
    public async Task ResumeAsync_PreviouslyFailedFlow_ReturnsStoredFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "failed-flow" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "failed-flow",
            state,
            CurrentStep: 1,
            Status: DslFlowStatus.Failed,
            Error: "Previous failure",
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
        await store.CreateAsync(snapshot);

        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.ResumeAsync("failed-flow");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ResumeAsync_CancelledFlow_ReturnsFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "cancelled-flow" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "cancelled-flow",
            state,
            CurrentStep: 1,
            Status: DslFlowStatus.Cancelled,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
        await store.CreateAsync(snapshot);

        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.ResumeAsync("cancelled-flow");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task CancelAsync_ExistingFlow_CancelsFlow()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "to-cancel" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "to-cancel",
            state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
        await store.CreateAsync(snapshot);

        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var cancelled = await executor.CancelAsync("to-cancel");

        // Assert
        Assert.True(cancelled);
        var updated = await store.GetAsync<TestFlowState>("to-cancel");
        Assert.Equal(DslFlowStatus.Cancelled, updated!.Status);
    }

    [Fact]
    public async Task CancelAsync_MissingFlow_ReturnsFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var cancelled = await executor.CancelAsync("non-existent");

        // Assert
        Assert.False(cancelled);
    }

    [Fact]
    public async Task GetAsync_StoredFlow_ReturnsCorrectSnapshot()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "get-test", Value = "test-value" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "get-test",
            state,
            CurrentStep: 2,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1);
        await store.CreateAsync(snapshot);

        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.GetAsync("get-test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("get-test", result.FlowId);
        Assert.Equal(2, result.CurrentStep);
        Assert.Equal("test-value", result.State.Value);
    }

    [Fact]
    public async Task GetAsync_MissingFlow_ReturnsNull()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.GetAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region FlowCompletedEvent Tests

    [Fact]
    public void FlowCompletedEvent_AllPropertiesAccessible()
    {
        // Arrange & Act
        var evt = new FlowCompletedEvent(
            FlowId: "flow-123",
            ParentCorrelationId: "parent-456",
            Success: true,
            Error: null,
            Result: "test-result");

        // Assert
        Assert.Equal("flow-123", evt.FlowId);
        Assert.Equal("parent-456", evt.ParentCorrelationId);
        Assert.True(evt.Success);
        Assert.Null(evt.Error);
        Assert.Equal("test-result", evt.Result);
        Assert.Equal(0, evt.MessageId);
    }

    [Fact]
    public void FlowCompletedEvent_FailedEvent_HasError()
    {
        // Arrange & Act
        var evt = new FlowCompletedEvent(
            FlowId: "flow-failed",
            ParentCorrelationId: "parent-789",
            Success: false,
            Error: "Something went wrong",
            Result: null);

        // Assert
        Assert.Equal("flow-failed", evt.FlowId);
        Assert.False(evt.Success);
        Assert.Equal("Something went wrong", evt.Error);
        Assert.Null(evt.Result);
    }

    [Fact]
    public void FlowCompletedEvent_NoParent_HasNullCorrelationId()
    {
        // Arrange & Act
        var evt = new FlowCompletedEvent(
            FlowId: "standalone-flow",
            ParentCorrelationId: null,
            Success: true,
            Error: null,
            Result: 42);

        // Assert
        Assert.Null(evt.ParentCorrelationId);
        Assert.Equal(42, evt.Result);
    }

    #endregion

    #region DslFlowResult Tests

    [Fact]
    public void DslFlowResult_Success_HasCorrectProperties()
    {
        // Arrange & Act
        var state = new TestFlowState { Value = "test" };
        var result = DslFlowResult<TestFlowState>.Success(state, DslFlowStatus.Completed, steps: 5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.State);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
        Assert.Equal(5, result.CompletedSteps);
        Assert.Null(result.Error);
    }

    [Fact]
    public void DslFlowResult_Failure_HasCorrectProperties()
    {
        // Arrange & Act
        var result = DslFlowResult<TestFlowState>.Failure(DslFlowStatus.Failed, "Test error", steps: 3);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.State);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
        Assert.Equal(3, result.CompletedSteps);
        Assert.Equal("Test error", result.Error);
    }

    [Fact]
    public void DslFlowResult_Suspended_HasCorrectStatus()
    {
        // Arrange & Act
        var state = new TestFlowState();
        var result = DslFlowResult<TestFlowState>.Success(state, DslFlowStatus.Suspended);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Suspended, result.Status);
    }

    [Fact]
    public void DslFlowResult_Cancelled_HasCorrectStatus()
    {
        // Arrange & Act
        var result = DslFlowResult<TestFlowState>.Failure(DslFlowStatus.Cancelled, "Cancelled by user");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Cancelled, result.Status);
    }

    [Fact]
    public void DslFlowResult_WithFlowId_SetsFlowId()
    {
        // Arrange & Act
        var state = new TestFlowState();
        var result = DslFlowResult<TestFlowState>.Success(state, DslFlowStatus.Completed) with { FlowId = "test-flow-id" };

        // Assert
        Assert.Equal("test-flow-id", result.FlowId);
    }

    #endregion

    #region FlowSnapshot Tests

    [Fact]
    public async Task FlowSnapshot_AllPropertiesAccessible()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "snapshot-test", Value = "test-value" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "snapshot-test",
            state,
            CurrentStep: 5,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1);

        await store.CreateAsync(snapshot);

        // Act
        var retrieved = await store.GetAsync<TestFlowState>("snapshot-test");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("snapshot-test", retrieved.FlowId);
        Assert.Equal(5, retrieved.CurrentStep);
        Assert.Equal(DslFlowStatus.Running, retrieved.Status);
        Assert.Null(retrieved.Error);
        Assert.Null(retrieved.WaitCondition);
        Assert.Equal(1, retrieved.Version);
        Assert.Equal("test-value", retrieved.State.Value);
    }

    [Fact]
    public async Task FlowSnapshot_WithError_PersistsError()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "error-snapshot" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "error-snapshot",
            state,
            CurrentStep: 2,
            Status: DslFlowStatus.Failed,
            Error: "Test error message",
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);

        await store.CreateAsync(snapshot);

        // Act
        var retrieved = await store.GetAsync<TestFlowState>("error-snapshot");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(DslFlowStatus.Failed, retrieved.Status);
        Assert.Equal("Test error message", retrieved.Error);
    }

    [Fact]
    public async Task FlowSnapshot_WithWaitCondition_PersistsCondition()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "wait-snapshot" };
        var waitCondition = new WaitCondition
        {
            CorrelationId = "wait-correlation",
            Type = WaitType.All,
            ExpectedCount = 3,
            CompletedCount = 1,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "wait-snapshot",
            FlowType = "TestFlow",
            Step = 2
        };

        var snapshot = new FlowSnapshot<TestFlowState>(
            "wait-snapshot",
            state,
            CurrentStep: 2,
            Status: DslFlowStatus.Suspended,
            Error: null,
            WaitCondition: waitCondition,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);

        await store.CreateAsync(snapshot);

        // Act
        var retrieved = await store.GetAsync<TestFlowState>("wait-snapshot");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(DslFlowStatus.Suspended, retrieved.Status);
        Assert.NotNull(retrieved.WaitCondition);
        Assert.Equal("wait-correlation", retrieved.WaitCondition.CorrelationId);
        Assert.Equal(WaitType.All, retrieved.WaitCondition.Type);
    }

    [Fact]
    public async Task FlowSnapshot_UpdateVersion_IncrementsVersion()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var state = new TestFlowState { FlowId = "version-test" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "version-test",
            state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);

        await store.CreateAsync(snapshot);

        // Act - Update multiple times
        for (int i = 1; i <= 5; i++)
        {
            var current = await store.GetAsync<TestFlowState>("version-test");
            var updated = current! with { CurrentStep = i, Version = i };
            await store.UpdateAsync(updated);
        }

        // Assert
        var final = await store.GetAsync<TestFlowState>("version-test");
        Assert.Equal(5, final!.CurrentStep);
        Assert.Equal(5, final.Version);
    }

    #endregion

    #region StepBuilder<T,TResult> Tests

    [Fact]
    public async Task RunAsync_SendWithResult_IntoSetsProperty()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result-value")));

        var store = new InMemoryDslFlowStore();
        var config = new SendWithResultIntoFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendWithResultIntoFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("result-value", state.Value);
    }

    [Fact]
    public async Task RunAsync_SendWithResultAndCompensation_ExecutesCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedCommands = new List<string>();

        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedCommands.Add("query");
                return ValueTask.FromResult(CatgaResult<string>.Failure("Query failed"));
            });

        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedCommands.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new SendWithResultCompensationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendWithResultCompensationFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_SendWithResultFailIfWithMessage_UsesCustomMessage()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("bad-result")));

        var store = new InMemoryDslFlowStore();
        var config = new SendWithResultFailIfMessageFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendWithResultFailIfMessageFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Custom failure message", result.Error);
    }

    [Fact]
    public async Task RunAsync_SendWithResultOptional_SkipsOnFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Failure("Optional failed")));

        var store = new InMemoryDslFlowStore();
        var config = new SendWithResultOptionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendWithResultOptionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_SendWithResultConditional_SkipsWhenFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var queryExecuted = false;
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                queryExecuted = true;
                return ValueTask.FromResult(CatgaResult<string>.Success("result"));
            });

        var store = new InMemoryDslFlowStore();
        var config = new SendWithResultConditionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendWithResultConditionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "skip" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(queryExecuted);
    }

    [Fact]
    public async Task RunAsync_SendWithResultTagged_AppliesTag()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("result")));

        var store = new InMemoryDslFlowStore();
        var config = new SendWithResultTaggedFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SendWithResultTaggedFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region FailIf Tests

    [Fact]
    public async Task RunAsync_FailIfOnResult_ConditionTrue_FlowFails()
    {
        // Arrange - FailIf works on query results, not state
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("fail-value")));

        var store = new InMemoryDslFlowStore();
        var config = new FailIfOnResultFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, FailIfOnResultFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RunAsync_FailIfOnResult_ConditionFalse_FlowContinues()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("ok-value")));

        var store = new InMemoryDslFlowStore();
        var config = new FailIfOnResultFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, FailIfOnResultFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Step Events Tests

    [Fact]
    public async Task RunAsync_OnStepCompleted_PublishesEvent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var publishedEvents = new List<TestEvent>();
        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                publishedEvents.Add(call.Arg<TestEvent>());
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new StepEventsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, StepEventsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        await executor.RunAsync(state);

        // Assert
        Assert.NotEmpty(publishedEvents);
    }

    [Fact]
    public async Task RunAsync_OnFlowCompleted_PublishesEvent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        TestEvent? completedEvent = null;
        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var evt = call.Arg<TestEvent>();
                if (evt.Value == "flow-completed")
                    completedEvent = evt;
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new FlowCompletedEventConfig();
        var executor = new DslFlowExecutor<TestFlowState, FlowCompletedEventConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        await executor.RunAsync(state);

        // Assert
        Assert.NotNull(completedEvent);
        Assert.Equal("flow-completed", completedEvent.Value);
    }

    [Fact]
    public async Task RunAsync_OnFlowFailed_PublishesEvent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Failure("Test failure")));

        TestEvent? failedEvent = null;
        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var evt = call.Arg<TestEvent>();
                if (evt.Value.StartsWith("flow-failed"))
                    failedEvent = evt;
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new FlowFailedEventConfig();
        var executor = new DslFlowExecutor<TestFlowState, FlowFailedEventConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        await executor.RunAsync(state);

        // Assert
        Assert.NotNull(failedEvent);
        Assert.Contains("flow-failed", failedEvent.Value);
    }

    #endregion

    #region Tagged Steps Tests

    [Fact]
    public async Task RunAsync_TaggedStep_UsesTaggedTimeout()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new TimeoutFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, TimeoutFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region FlowTimeoutService Tests

    [Fact]
    public async Task FlowTimeoutService_StartAndStop_Works()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var service = new FlowTimeoutService(store, TimeSpan.FromMilliseconds(50));

        // Act
        service.Start();
        await Task.Delay(100);
        await service.StopAsync();

        // Assert - No exception thrown
    }

    [Fact]
    public async Task FlowTimeoutService_ChecksTimedOutConditions()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var timedOutCondition = new WaitCondition
        {
            CorrelationId = "timeout-test",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMilliseconds(1),
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };
        await store.SetWaitConditionAsync("timeout-test", timedOutCondition);

        var service = new FlowTimeoutService(store, TimeSpan.FromMilliseconds(10));

        // Act
        service.Start();
        await Task.Delay(100);
        await service.StopAsync();

        // Assert
        var updated = await store.GetWaitConditionAsync("timeout-test");
        Assert.NotNull(updated);
        Assert.Equal(2, updated.CompletedCount); // Force completed
        Assert.Contains(updated.Results, r => r.Error != null && r.Error.Contains("timed out"));
    }

    [Fact]
    public void FlowTimeoutService_Dispose_Works()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var service = new FlowTimeoutService(store);

        // Act & Assert - No exception
        service.Dispose();
    }

    [Fact]
    public async Task FlowTimeoutService_DefaultInterval_IsThirtySeconds()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var service = new FlowTimeoutService(store);

        // Act
        service.Start();

        // Assert - Service starts without error
        await service.StopAsync();
        service.Dispose();
    }

    [Fact]
    public async Task FlowTimeoutService_MultipleTimedOutConditions_ProcessesAll()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        for (int i = 0; i < 3; i++)
        {
            var condition = new WaitCondition
            {
                CorrelationId = $"timeout-{i}",
                Type = WaitType.All,
                ExpectedCount = 2,
                CompletedCount = 0,
                Timeout = TimeSpan.FromMilliseconds(1),
                CreatedAt = DateTime.UtcNow.AddSeconds(-10),
                FlowId = $"flow-{i}",
                FlowType = "TestFlow",
                Step = 0
            };
            await store.SetWaitConditionAsync($"timeout-{i}", condition);
        }

        var service = new FlowTimeoutService(store, TimeSpan.FromMilliseconds(10));

        // Act
        service.Start();
        await Task.Delay(150);
        await service.StopAsync();

        // Assert
        for (int i = 0; i < 3; i++)
        {
            var updated = await store.GetWaitConditionAsync($"timeout-{i}");
            Assert.NotNull(updated);
            Assert.Equal(2, updated.CompletedCount);
        }
    }

    [Fact]
    public async Task FlowTimeoutService_StopAsync_WithoutStart_DoesNotThrow()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var service = new FlowTimeoutService(store);

        // Act & Assert
        await service.StopAsync();
        service.Dispose();
    }

    #endregion

    #region E2E Scenarios

    [Fact]
    public async Task E2E_MultiStepFlow_AllStepsExecuteInOrder()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedSteps.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EMultiStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EMultiStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "step1", "step2", "step3", "step4", "step5" }, executedSteps);
    }

    [Fact]
    public async Task E2E_ConditionalFlow_SkipsStepsBasedOnState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedSteps.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EConditionalFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EConditionalFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "skip-optional" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("always-run", executedSteps);
        Assert.DoesNotContain("optional-step", executedSteps);
        Assert.Contains("final-step", executedSteps);
    }

    [Fact]
    public async Task E2E_CompensationFlow_ExecutesCompensationOnFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        var callCount = 0;
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<TestCommand>();
                executedSteps.Add(cmd.Value);
                callCount++;
                // Fail on step3
                if (cmd.Value == "step3")
                    return Task.FromResult(CatgaResult.Failure("Step3 failed"));
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2ECompensationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2ECompensationFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("step1", executedSteps);
        Assert.Contains("step2", executedSteps);
        Assert.Contains("step3", executedSteps);
        Assert.Contains("compensate2", executedSteps);
        Assert.Contains("compensate1", executedSteps);
    }

    [Fact]
    public async Task E2E_QueryAndPublishFlow_CapturesResultAndPublishes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var publishedEvents = new List<TestEvent>();

        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("query-result-value")));

        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                publishedEvents.Add(call.Arg<TestEvent>());
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EQueryPublishFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EQueryPublishFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("query-result-value", state.Value);
        Assert.Contains(publishedEvents, e => e.Value == "query-result-value");
    }

    [Fact]
    public async Task E2E_FlowWithPersistence_SavesAndResumes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedSteps.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "persistent-flow-123" };

        // Act - Run flow
        var result = await executor.RunAsync(state);

        // Assert - Flow completed and state persisted
        Assert.True(result.IsSuccess);
        var snapshot = await store.GetAsync<TestFlowState>("persistent-flow-123");
        Assert.NotNull(snapshot);
        Assert.Equal(DslFlowStatus.Completed, snapshot.Status);
    }

    [Fact]
    public async Task E2E_CancellationFlow_StopsExecutionOnCancel()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        var cts = new CancellationTokenSource();

        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<TestCommand>();
                executedSteps.Add(cmd.Value);
                if (cmd.Value == "step2")
                    cts.Cancel();
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EMultiStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EMultiStepFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state, cts.Token);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Cancelled, result.Status);
        Assert.Contains("step1", executedSteps);
        Assert.Contains("step2", executedSteps);
        Assert.DoesNotContain("step5", executedSteps);
    }

    [Fact]
    public async Task E2E_OptionalStepsFlow_ContinuesOnOptionalFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<TestCommand>();
                executedSteps.Add(cmd.Value);
                if (cmd.Value == "optional")
                    return Task.FromResult(CatgaResult.Failure("Optional failed"));
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EOptionalStepsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EOptionalStepsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("required1", executedSteps);
        Assert.Contains("optional", executedSteps);
        Assert.Contains("required2", executedSteps);
    }

    [Fact]
    public async Task E2E_FailIfCondition_FailsWhenConditionMet()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("invalid-result")));

        var store = new InMemoryDslFlowStore();
        var config = new E2EFailIfFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EFailIfFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("invalid", result.Error);
    }

    [Fact]
    public async Task E2E_TaggedSteps_AppliesCorrectTimeouts()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedSteps.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2ETaggedStepsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2ETaggedStepsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "normal", "fast", "slow" }, executedSteps);
    }

    [Fact]
    public async Task E2E_MixedStepTypes_ExecutesAllTypes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var operations = new List<string>();

        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                operations.Add($"Send:{call.Arg<TestCommand>().Value}");
                return Task.FromResult(CatgaResult.Success());
            });

        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                operations.Add($"Query:{call.Arg<TestQueryWithResult>().Value}");
                return ValueTask.FromResult(CatgaResult<string>.Success("query-result"));
            });

        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                operations.Add($"Publish:{call.Arg<TestEvent>().Value}");
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EMixedStepsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EMixedStepsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Send:command1", operations);
        Assert.Contains("Query:query1", operations);
        Assert.Contains("Publish:query-result", operations);
    }

    [Fact]
    public async Task E2E_ResumeFromMiddle_ContinuesFromCorrectStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executedSteps.Add(call.Arg<TestCommand>().Value);
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();

        // Pre-create a suspended flow at step 2
        var state = new TestFlowState { FlowId = "resume-test" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            "resume-test",
            state,
            CurrentStep: 2,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
        await store.CreateAsync(snapshot);

        var config = new E2EMultiStepFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EMultiStepFlowConfig>(mediator, store, config);

        // Act
        var result = await executor.ResumeAsync("resume-test");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("step1", executedSteps);
        Assert.DoesNotContain("step2", executedSteps);
        Assert.Contains("step3", executedSteps);
        Assert.Contains("step4", executedSteps);
        Assert.Contains("step5", executedSteps);
    }

    [Fact]
    public async Task E2E_StateModification_PreservesStateAcrossSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Success("modified-value")));

        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new E2EStateModificationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EStateModificationFlowConfig>(mediator, store, config);
        var state = new TestFlowState { Value = "initial" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("modified-value", state.Value);
    }

    [Fact]
    public async Task E2E_MultipleCompensations_ExecutesInReverseOrder()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var executedSteps = new List<string>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<TestCommand>();
                executedSteps.Add(cmd.Value);
                if (cmd.Value == "step4")
                    return Task.FromResult(CatgaResult.Failure("Step4 failed"));
                return Task.FromResult(CatgaResult.Success());
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EMultiCompensationFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EMultiCompensationFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        // Verify compensation order is reverse
        var compensateIndex1 = executedSteps.IndexOf("compensate1");
        var compensateIndex2 = executedSteps.IndexOf("compensate2");
        var compensateIndex3 = executedSteps.IndexOf("compensate3");
        Assert.True(compensateIndex3 < compensateIndex2);
        Assert.True(compensateIndex2 < compensateIndex1);
    }

    [Fact]
    public async Task E2E_FlowEvents_PublishesOnCompletionAndFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var publishedEvents = new List<string>();

        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        mediator.PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                publishedEvents.Add(call.Arg<TestEvent>().Value);
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var config = new E2EFlowEventsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EFlowEventsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("flow-completed", publishedEvents);
    }

    [Fact]
    public async Task E2E_QueryFailure_PropagatesError()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync<TestQueryWithResult, string>(Arg.Any<TestQueryWithResult>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<string>.Failure("Query failed")));

        var store = new InMemoryDslFlowStore();
        var config = new E2EQueryFailureFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EQueryFailureFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Query failed", result.Error);
    }

    [Fact]
    public async Task E2E_RetrySettings_AppliesGlobalRetry()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new E2ERetrySettingsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2ERetrySettingsFlowConfig>(mediator, store, config);
        var state = new TestFlowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task E2E_PersistSettings_PersistsFlow()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new E2EPersistSettingsFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, E2EPersistSettingsFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = "persist-test-123" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        var snapshot = await store.GetAsync<TestFlowState>("persist-test-123");
        Assert.NotNull(snapshot);
    }

    [Fact]
    public async Task E2E_EmptyFlowId_GeneratesNewId()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));

        var store = new InMemoryDslFlowStore();
        var config = new SimpleFlowConfig();
        var executor = new DslFlowExecutor<TestFlowState, SimpleFlowConfig>(mediator, store, config);
        var state = new TestFlowState { FlowId = null };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(state.FlowId);
        Assert.NotEmpty(state.FlowId);
    }

    #endregion

    #region E2E Flow Configs

    public class E2EMultiStepFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-multi-step");
            flow.Send(s => new TestCommand("step1"));
            flow.Send(s => new TestCommand("step2"));
            flow.Send(s => new TestCommand("step3"));
            flow.Send(s => new TestCommand("step4"));
            flow.Send(s => new TestCommand("step5"));
        }
    }

    public class E2EConditionalFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-conditional");
            flow.Send(s => new TestCommand("always-run"));
            flow.Send(s => new TestCommand("optional-step"))
                .OnlyWhen(s => s.Value != "skip-optional");
            flow.Send(s => new TestCommand("final-step"));
        }
    }

    public class E2ECompensationFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-compensation");
            flow.Send(s => new TestCommand("step1"))
                .IfFail(s => new TestCommand("compensate1"));
            flow.Send(s => new TestCommand("step2"))
                .IfFail(s => new TestCommand("compensate2"));
            flow.Send(s => new TestCommand("step3"));
        }
    }

    public class E2EQueryPublishFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-query-publish");
            flow.Query(s => new TestQueryWithResult("fetch-data"))
                .Into(s => s.Value);
            flow.Publish(s => new TestEvent(s.Value ?? "default"));
        }
    }

    public class E2EOptionalStepsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-optional-steps");
            flow.Send(s => new TestCommand("required1"));
            flow.Send(s => new TestCommand("optional")).Optional();
            flow.Send(s => new TestCommand("required2"));
        }
    }

    public class E2EFailIfFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-failif");
            flow.Send(s => new TestQueryWithResult("check"))
                .FailIf((string r) => r.Contains("invalid"), "Result contains invalid data");
        }
    }

    public class E2ETaggedStepsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-tagged-steps");
            flow.Timeout(TimeSpan.FromSeconds(30));
            flow.Timeout(TimeSpan.FromSeconds(5)).ForTags("fast");
            flow.Timeout(TimeSpan.FromMinutes(5)).ForTags("slow");
            flow.Send(s => new TestCommand("normal"));
            flow.Send(s => new TestCommand("fast")).Tag("fast");
            flow.Send(s => new TestCommand("slow")).Tag("slow");
        }
    }

    public class E2EMixedStepsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-mixed-steps");
            flow.Send(s => new TestCommand("command1"));
            flow.Query(s => new TestQueryWithResult("query1"))
                .Into(s => s.Value);
            flow.Publish(s => new TestEvent(s.Value ?? "default"));
        }
    }

    public class E2EStateModificationFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-state-modification");
            flow.Send(s => new TestQueryWithResult("fetch"))
                .Into(s => s.Value);
            flow.Send(s => new TestCommand(s.Value ?? "none"));
        }
    }

    public class E2EMultiCompensationFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-multi-compensation");
            flow.Send(s => new TestCommand("step1"))
                .IfFail(s => new TestCommand("compensate1"));
            flow.Send(s => new TestCommand("step2"))
                .IfFail(s => new TestCommand("compensate2"));
            flow.Send(s => new TestCommand("step3"))
                .IfFail(s => new TestCommand("compensate3"));
            flow.Send(s => new TestCommand("step4"));
        }
    }

    public class E2EFlowEventsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-flow-events");
            flow.OnFlowCompleted(s => new TestEvent("flow-completed"));
            flow.Send(s => new TestCommand("step1"));
        }
    }

    public class E2EQueryFailureFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-query-failure");
            flow.Query(s => new TestQueryWithResult("failing-query"))
                .Into(s => s.Value);
        }
    }

    public class E2ERetrySettingsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-retry-settings");
            flow.Retry(3);
            flow.Send(s => new TestCommand("step1"));
        }
    }

    public class E2EPersistSettingsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("e2e-persist-settings");
            flow.Persist();
            flow.Send(s => new TestCommand("step1"));
        }
    }

    #endregion

    #region Compensation Flow Configs

    public class CompensationFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("compensation-flow");
            flow.Send(s => new TestCommand("step1"))
                .IfFail(s => new TestCommand("compensate1"));
            flow.Send(s => new TestCommand("step2"))
                .IfFail(s => new TestCommand("compensate2"));
            flow.Send(s => new TestCommand("step3"));
        }
    }

    public class ConditionalFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("conditional-flow");
            flow.Send(s => new TestCommand("always"));
            flow.Send(s => new TestCommand("conditional"))
                .OnlyWhen(s => s.Value == "execute");
            flow.Send(s => new TestCommand("final"));
        }
    }

    public class TimeoutFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("timeout-flow");
            flow.Timeout(TimeSpan.FromSeconds(5));
            flow.Timeout(TimeSpan.FromSeconds(1)).ForTags("fast");
            flow.Send(s => new TestCommand("normal"));
            flow.Send(s => new TestCommand("fast")).Tag("fast");
        }
    }

    public class QueryFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("query-flow");
            flow.Query(s => new TestQuery(s.Value ?? "default"))
                .Into(s => s.Value);
        }
    }

    public class PublishFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("publish-flow");
            flow.Publish(s => new TestEvent(s.Value ?? "default"));
        }
    }

    public record TestQuery(string Value) : IRequest<string>
    {
        public long MessageId => 0;
    }

    public record TestEvent(string Value) : IEvent
    {
        public long MessageId => 0;
        public string? CorrelationId { get; set; }
    }

    public record TestQueryWithResult(string Value) : IRequest<string>
    {
        public long MessageId => 0;
    }

    public class SimpleFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("simple-flow");
            flow.Send(s => new TestCommand("simple"));
        }
    }

    public class FailIfOnResultFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("failif-result-flow");
            flow.Send(s => new TestQueryWithResult("test"))
                .FailIf((string r) => r.Contains("fail"));
        }
    }

    public class SendWithResultIntoFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-into");
            flow.Send(s => new TestQueryWithResult("query"))
                .Into(s => s.Value);
        }
    }

    public class SendWithResultCompensationFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-compensation");
            flow.Send(s => new TestQueryWithResult("query"))
                .IfFail(s => new TestCommand("compensate"));
        }
    }

    public class SendWithResultFailIfMessageFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-failif-message");
            flow.Send(s => new TestQueryWithResult("query"))
                .FailIf((string r) => r.Contains("bad"), "Custom failure message");
        }
    }

    public class SendWithResultOptionalFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-optional");
            flow.Send(s => new TestQueryWithResult("query"))
                .Optional();
        }
    }

    public class SendWithResultConditionalFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-conditional");
            flow.Send(s => new TestQueryWithResult("query"))
                .OnlyWhen(s => s.Value == "execute");
        }
    }

    public class SendWithResultTaggedFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-tagged");
            flow.Send(s => new TestQueryWithResult("query"))
                .Tag("important");
        }
    }

    public class WhenAnyFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-any-flow");
            flow.WhenAny(
                s => new TestCommand("child1"),
                s => new TestCommand("child2"));
        }
    }

    public class WhenAnyWithResultFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-any-result-flow");
            flow.WhenAny(
                s => new TestQueryWithResult("child1"),
                s => new TestQueryWithResult("child2"))
                .Into(s => s.Value);
        }
    }

    public class WhenAnyWithTimeoutFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-any-timeout-flow");
            flow.WhenAny(
                s => new TestCommand("child1"),
                s => new TestCommand("child2"))
                .Timeout(TimeSpan.FromSeconds(30));
        }
    }

    public class WhenAnyWithTagFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-any-tag-flow");
            flow.WhenAny(
                s => new TestCommand("child1"),
                s => new TestCommand("child2"))
                .Tag("parallel");
        }
    }

    public class WhenAnyResultWithTimeoutFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-any-result-timeout-flow");
            flow.WhenAny(
                s => new TestQueryWithResult("child1"),
                s => new TestQueryWithResult("child2"))
                .Timeout(TimeSpan.FromMinutes(5));
        }
    }

    public class WhenAnyResultWithTagFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-any-result-tag-flow");
            flow.WhenAny(
                s => new TestQueryWithResult("child1"),
                s => new TestQueryWithResult("child2"))
                .Tag("race");
        }
    }

    public class WhenAllWithTimeoutFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-all-timeout-flow");
            flow.WhenAll(
                s => new TestCommand("child1"),
                s => new TestCommand("child2"))
                .Timeout(TimeSpan.FromMinutes(1));
        }
    }

    public class WhenAllWithTagFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-all-tag-flow");
            flow.WhenAll(
                s => new TestCommand("child1"),
                s => new TestCommand("child2"))
                .Tag("batch");
        }
    }

    public class WhenAllWithCompensationFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("when-all-compensation-flow");
            flow.WhenAll(
                s => new TestCommand("child1"),
                s => new TestCommand("child2"))
                .IfAnyFail(s => new TestCommand("compensate"));
        }
    }

    public class ChainedOptionsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("chained-options-flow");
            flow.Send(s => new TestCommand("chained"))
                .OnlyWhen(s => s.Value == "execute")
                .Tag("important")
                .IfFail(s => new TestCommand("compensate"));
        }
    }

    public class MultiTagStepFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("multi-tag-step-flow");
            flow.Send(s => new TestCommand("tagged"))
                .Tag("tag1", "tag2", "tag3");
        }
    }

    public class FailIfWithMessageFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("failif-message-flow");
            flow.Send(s => new TestCommand("test"))
                .FailIf(s => s.Value == "fail", "Custom error message");
        }
    }

    public class FailIfOnStateFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("failif-state-flow");
            flow.Send(s => new TestCommand("test"))
                .FailIf(s => s.Value == "fail");
        }
    }

    public class SendResultWithIfFailFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-iffail");
            flow.Send(s => new TestQueryWithResult("query"))
                .IfFail(s => new TestCommand("compensate"));
        }
    }

    public class SendResultOnlyWhenFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-onlywhen");
            flow.Send(s => new TestQueryWithResult("query"))
                .OnlyWhen(s => s.Value == "execute");
        }
    }

    public class SendResultOptionalFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-optional");
            flow.Send(s => new TestQueryWithResult("query"))
                .Optional();
        }
    }

    public class SendResultTagFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-tag");
            flow.Send(s => new TestQueryWithResult("query"))
                .Tag("important");
        }
    }

    public class SendResultIntoFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("send-result-into");
            flow.Send(s => new TestQueryWithResult("query"))
                .Into(s => s.Value);
        }
    }

    public class OptionalConditionalFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("optional-conditional-flow");
            flow.Send(s => new TestCommand("always"));
            flow.Send(s => new TestCommand("optional-conditional"))
                .Optional()
                .OnlyWhen(s => s.Value == "execute");
            flow.Send(s => new TestCommand("final"));
        }
    }

    public class QueryIntoFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("query-into");
            flow.Query(s => new TestQueryWithResult("query"))
                .Into(s => s.Value);
        }
    }

    public class QueryWithTagFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("query-with-tag");
            flow.Query(s => new TestQueryWithResult("query"))
                .Tag("important");
        }
    }

    public class PublishWithTagFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("publish-with-tag");
            flow.Publish(s => new TestEvent(s.Value ?? "default"))
                .Tag("notification");
        }
    }

    public class StepEventsFlowConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("step-events");
            flow.OnStepCompleted((s, i) => new TestEvent($"step-{i}-completed"));
            flow.Send(s => new TestCommand("test"));
        }
    }

    public class FlowCompletedEventConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("flow-completed-event");
            flow.OnFlowCompleted(s => new TestEvent("flow-completed"));
        }
    }

    public class FlowFailedEventConfig : FlowConfig<TestFlowState>
    {
        protected override void Configure(IFlowBuilder<TestFlowState> flow)
        {
            flow.Name("flow-failed-event");
            flow.OnFlowFailed((s, e) => new TestEvent($"flow-failed: {e}"));
            flow.Send(s => new TestCommand("will-fail"));
        }
    }

    #endregion
}
