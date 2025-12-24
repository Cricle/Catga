using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow;

/// <summary>
/// Edge case tests for Flow DSL to ensure robustness.
/// </summary>
public class FlowDslEdgeCaseTests
{
    #region Null and Empty Handling

    [Fact]
    public async Task ForEach_NullCollection_HandlesGracefully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NullCollectionFlow();
        var executor = new DslFlowExecutor<NullTestState, NullCollectionFlow>(mediator, store, config);

        var state = new NullTestState { FlowId = "null-001", Items = null! };

        // Act & Assert - should not throw
        var result = await executor.RunAsync(state);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task If_NullConditionValue_HandlesGracefully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NullConditionFlow();
        var executor = new DslFlowExecutor<NullTestState, NullConditionFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new NullTestState { FlowId = "null-cond-001", NullableValue = null };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("else");
    }

    [Fact]
    public async Task Switch_NullSelector_UsesDefault()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NullSwitchFlow();
        var executor = new DslFlowExecutor<NullTestState, NullSwitchFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new NullTestState { FlowId = "null-switch-001", Category = null };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("default");
    }

    #endregion

    #region Deeply Nested Branching

    [Fact]
    public async Task If_DeeplyNested_ExecutesCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new DeeplyNestedIfFlow();
        var executor = new DslFlowExecutor<NestedState, DeeplyNestedIfFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new NestedState
        {
            FlowId = "nested-001",
            Level1 = true,
            Level2 = true,
            Level3 = true
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ReachedLevel.Should().Be(3);
    }

    [Fact]
    public async Task Switch_InsideIf_ExecutesCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SwitchInsideIfFlow();
        var executor = new DslFlowExecutor<NestedState, SwitchInsideIfFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new NestedState
        {
            FlowId = "switch-in-if-001",
            Level1 = true,
            Category = "gold"
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("gold-in-if");
    }

    #endregion

    #region Failure and Recovery

    [Fact]
    public async Task Flow_FailureAtFirstStep_FailsImmediately()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new MultiStepFlow();
        var executor = new DslFlowExecutor<SimpleState, MultiStepFlow>(mediator, store, config);

        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Failure("First step failed")));

        var state = new SimpleState { FlowId = "fail-first-001" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("First step failed");
    }

    [Fact]
    public async Task Flow_FailureAtLastStep_ExecutesPreviousSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new MultiStepFlow();
        var executor = new DslFlowExecutor<SimpleState, MultiStepFlow>(mediator, store, config);

        var callCount = 0;
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount >= 3) // Fail on third step
                    return new ValueTask<CatgaResult>(CatgaResult.Failure("Last step failed"));
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var state = new SimpleState { FlowId = "fail-last-001" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task Flow_OptionalStepFailure_ContinuesExecution()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new OptionalStepFlow();
        var executor = new DslFlowExecutor<SimpleState, OptionalStepFlow>(mediator, store, config);

        var callCount = 0;
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 2) // Second step (optional) fails
                    return new ValueTask<CatgaResult>(CatgaResult.Failure("Optional step failed"));
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var state = new SimpleState { FlowId = "optional-001" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        callCount.Should().Be(3); // All three steps called
    }

    #endregion

    #region Concurrent Execution

    [Fact]
    public async Task Flow_ConcurrentExecution_IsolatesState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new CounterFlow();

        SetupCounterMediator(mediator);

        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            var executor = new DslFlowExecutor<CounterState, CounterFlow>(mediator, store, config);
            var state = new CounterState { FlowId = $"concurrent-{i}", InitialValue = i };
            return await executor.RunAsync(state);
        });

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Select(r => r.State.FinalValue).Should().BeEquivalentTo(
            Enumerable.Range(1, 10).Select(i => i + 3)); // Each adds 3
    }

    #endregion

    #region Large Collections

    [Fact]
    public async Task ForEach_LargeCollection_ProcessesAll()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new LargeCollectionFlow();
        var executor = new DslFlowExecutor<LargeState, LargeCollectionFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new LargeState
        {
            FlowId = "large-001",
            Items = Enumerable.Range(1, 1000).Select(i => $"Item-{i}").ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(1000);
    }

    #endregion

    #region State Mutation

    [Fact]
    public async Task Flow_StateMutation_PreservedAcrossSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new StateMutationFlow();
        var executor = new DslFlowExecutor<MutableState, StateMutationFlow>(mediator, store, config);

        mediator.SendAsync<IncrementRequest, int>(Arg.Any<IncrementRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<IncrementRequest>();
                return new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(req.Current + 1));
            });

        var state = new MutableState { FlowId = "mutable-001", Counter = 0 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(5); // Incremented 5 times
    }

    #endregion

    #region Timeout Handling

    [Fact]
    public async Task Flow_Cancellation_StopsExecution()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SlowFlow();
        var executor = new DslFlowExecutor<SimpleState, SlowFlow>(mediator, store, config);

        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var ct = call.Arg<CancellationToken>();
                await Task.Delay(5000, ct);
                return CatgaResult.Success();
            });

        var state = new SimpleState { FlowId = "cancel-001" };
        using var cts = new CancellationTokenSource(100); // Cancel after 100ms

        // Act
        var result = await executor.RunAsync(state, cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(DslFlowStatus.Cancelled);
    }

    #endregion

    #region Helper Methods

    private void SetupMediatorSuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        mediator.SendAsync<IRequest<string>, string>(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));
    }

    private void SetupCounterMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
    }

    #endregion

    #region Test State Classes

    public class NullTestState : BaseFlowState
    {
        public List<string>? Items { get; set; }
        public string? NullableValue { get; set; }
        public string? Category { get; set; }
        public string? Branch { get; set; }
    }

    public class NestedState : BaseFlowState
    {
        public bool Level1 { get; set; }
        public bool Level2 { get; set; }
        public bool Level3 { get; set; }
        public int ReachedLevel { get; set; }
        public string? Category { get; set; }
        public string? Branch { get; set; }
    }

    public class SimpleState : BaseFlowState
    {
        public int StepCount { get; set; }
    }

    public class CounterState : BaseFlowState
    {
        public int InitialValue { get; set; }
        public int FinalValue { get; set; }
    }

    public class LargeState : BaseFlowState
    {
        public List<string> Items { get; set; } = [];
        public int ProcessedCount { get; set; }
    }

    public class MutableState : BaseFlowState
    {
        public int Counter { get; set; }
    }

    #endregion

    #region Test Request Classes

    public record ProcessRequest : IRequest;
    public record SetBranchRequest(string Branch) : IRequest;
    public record SetLevelRequest(int Level) : IRequest;
    public record IncrementRequest(int Current) : IRequest<int>;
    public record ProcessItemRequest(string Item) : IRequest;
    public record SlowRequest : IRequest;

    #endregion

    #region Test Flow Configs

    public class NullCollectionFlow : FlowConfig<NullTestState>
    {
        protected override void Configure(IFlowBuilder<NullTestState> flow)
        {
            flow.ForEach(s => s.Items ?? Enumerable.Empty<string>())
                .Configure((item, f) => f.Send(s => new ProcessRequest()))
                .EndForEach();
        }
    }

    public class NullConditionFlow : FlowConfig<NullTestState>
    {
        protected override void Configure(IFlowBuilder<NullTestState> flow)
        {
            flow.If(s => s.NullableValue != null)
                .Send(s => new SetBranchRequest("then"))
            .Else()
                .Send(s => new SetBranchRequest("else"))
            .EndIf();
        }
    }

    public class NullSwitchFlow : FlowConfig<NullTestState>
    {
        protected override void Configure(IFlowBuilder<NullTestState> flow)
        {
            flow.Switch(s => s.Category ?? "unknown")
                .Case("gold", c => c.Send(s => new SetBranchRequest("gold")))
                .Default(c => c.Send(s => new SetBranchRequest("default")))
                .EndSwitch();
        }
    }

    public class DeeplyNestedIfFlow : FlowConfig<NestedState>
    {
        protected override void Configure(IFlowBuilder<NestedState> flow)
        {
            flow.If(s => s.Level1)
                .Send(s => new SetLevelRequest(1))
                .If(s => s.Level2)
                    .Send(s => new SetLevelRequest(2))
                    .If(s => s.Level3)
                        .Send(s => new SetLevelRequest(3))
                    .EndIf()
                .EndIf()
            .EndIf();
        }
    }

    public class SwitchInsideIfFlow : FlowConfig<NestedState>
    {
        protected override void Configure(IFlowBuilder<NestedState> flow)
        {
            flow.If(s => s.Level1)
                .Switch(s => s.Category ?? "unknown")
                    .Case("gold", c => c.Send(s => new SetBranchRequest("gold-in-if")))
                    .Default(c => c.Send(s => new SetBranchRequest("default-in-if")))
                .EndSwitch()
            .EndIf();
        }
    }

    public class MultiStepFlow : FlowConfig<SimpleState>
    {
        protected override void Configure(IFlowBuilder<SimpleState> flow)
        {
            flow.Send(s => new ProcessRequest());
            flow.Send(s => new ProcessRequest());
            flow.Send(s => new ProcessRequest());
        }
    }

    public class OptionalStepFlow : FlowConfig<SimpleState>
    {
        protected override void Configure(IFlowBuilder<SimpleState> flow)
        {
            flow.Send(s => new ProcessRequest());
            flow.Send(s => new ProcessRequest()).Optional();
            flow.Send(s => new ProcessRequest());
        }
    }

    public class CounterFlow : FlowConfig<CounterState>
    {
        protected override void Configure(IFlowBuilder<CounterState> flow)
        {
            flow.Send(s => new ProcessRequest()).OnCompleted(s => { s.FinalValue = s.InitialValue + 1; return new CounterEvent(); });
            flow.Send(s => new ProcessRequest()).OnCompleted(s => { s.FinalValue++; return new CounterEvent(); });
            flow.Send(s => new ProcessRequest()).OnCompleted(s => { s.FinalValue++; return new CounterEvent(); });
        }
    }

    public record CounterEvent : IEvent { public long MessageId { get; init; } }

    public class LargeCollectionFlow : FlowConfig<LargeState>
    {
        protected override void Configure(IFlowBuilder<LargeState> flow)
        {
            flow.ForEach(s => s.Items)
                .Configure((item, f) => f.Send(s => new ProcessItemRequest(item)))
                .OnItemSuccess((s, _, _) => s.ProcessedCount++)
                .WithParallelism(10)
                .EndForEach();
        }
    }

    public class StateMutationFlow : FlowConfig<MutableState>
    {
        protected override void Configure(IFlowBuilder<MutableState> flow)
        {
            flow.Send(s => new IncrementRequest(s.Counter)).Into((s, r) => s.Counter = r);
            flow.Send(s => new IncrementRequest(s.Counter)).Into((s, r) => s.Counter = r);
            flow.Send(s => new IncrementRequest(s.Counter)).Into((s, r) => s.Counter = r);
            flow.Send(s => new IncrementRequest(s.Counter)).Into((s, r) => s.Counter = r);
            flow.Send(s => new IncrementRequest(s.Counter)).Into((s, r) => s.Counter = r);
        }
    }

    public class SlowFlow : FlowConfig<SimpleState>
    {
        protected override void Configure(IFlowBuilder<SimpleState> flow)
        {
            flow.Send(s => new SlowRequest());
        }
    }

    #endregion
}
