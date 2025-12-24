using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow;

/// <summary>
/// Complete scenario tests for Flow DSL covering all control flow combinations.
/// </summary>
public class FlowDslCompleteScenarioTests
{
    #region If-ElseIf-Else Complete Scenarios

    [Fact]
    public async Task If_ThenBranchExecuted_WhenConditionTrue()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new IfThenElseFlow();
        var executor = new DslFlowExecutor<BranchingState, IfThenElseFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "if-then-001", Value = 100 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("then");
    }

    [Fact]
    public async Task If_ElseBranchExecuted_WhenConditionFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new IfThenElseFlow();
        var executor = new DslFlowExecutor<BranchingState, IfThenElseFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "if-else-001", Value = 10 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("else");
    }

    [Fact]
    public async Task If_ElseIfBranchExecuted_WhenElseIfConditionTrue()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new IfElseIfElseFlow();
        var executor = new DslFlowExecutor<BranchingState, IfElseIfElseFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "if-elseif-001", Value = 50 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("elseif");
    }

    [Fact]
    public async Task If_NestedIf_ExecutesCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NestedIfFlow();
        var executor = new DslFlowExecutor<BranchingState, NestedIfFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "nested-if-001", Value = 100, Category = "premium" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("premium-high");
    }

    #endregion

    #region Switch-Case Complete Scenarios

    [Theory]
    [InlineData("gold", "gold-processing")]
    [InlineData("silver", "silver-processing")]
    [InlineData("bronze", "bronze-processing")]
    [InlineData("unknown", "default-processing")]
    public async Task Switch_CorrectCaseExecuted(string tier, string expectedBranch)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SwitchCaseFlow();
        var executor = new DslFlowExecutor<BranchingState, SwitchCaseFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = $"switch-{tier}", Category = tier };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be(expectedBranch);
    }

    [Fact]
    public async Task Switch_WithNestedIf_ExecutesCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SwitchWithNestedIfFlow();
        var executor = new DslFlowExecutor<BranchingState, SwitchWithNestedIfFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "switch-nested-001", Category = "gold", Value = 1000 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("gold-vip");
    }

    #endregion

    #region ForEach Complete Scenarios

    [Fact]
    public async Task ForEach_ProcessesAllItems()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ForEachBasicFlow();
        var executor = new DslFlowExecutor<CollectionState, ForEachBasicFlow>(mediator, store, config);

        SetupMediatorForCollection(mediator);
        var state = new CollectionState
        {
            FlowId = "foreach-001",
            Items = ["A", "B", "C", "D", "E"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().BeEquivalentTo(["A", "B", "C", "D", "E"]);
    }

    [Fact]
    public async Task ForEach_WithCondition_SkipsItems()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ForEachWithConditionFlow();
        var executor = new DslFlowExecutor<CollectionState, ForEachWithConditionFlow>(mediator, store, config);

        SetupMediatorForCollection(mediator);
        var state = new CollectionState
        {
            FlowId = "foreach-condition-001",
            Items = ["A", "SKIP", "B", "SKIP", "C"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().BeEquivalentTo(["A", "B", "C"]);
    }

    [Fact]
    public async Task ForEach_StopOnFirstFailure_StopsAtFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ForEachStopOnFailureFlow();
        var executor = new DslFlowExecutor<CollectionState, ForEachStopOnFailureFlow>(mediator, store, config);

        SetupMediatorWithFailingItem(mediator, "FAIL");
        var state = new CollectionState
        {
            FlowId = "foreach-stop-001",
            Items = ["A", "B", "FAIL", "C", "D"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.ProcessedItems.Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public async Task ForEach_ContinueOnFailure_ProcessesAll()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ForEachContinueOnFailureFlow();
        var executor = new DslFlowExecutor<CollectionState, ForEachContinueOnFailureFlow>(mediator, store, config);

        SetupMediatorWithFailingItem(mediator, "FAIL");
        var state = new CollectionState
        {
            FlowId = "foreach-continue-001",
            Items = ["A", "FAIL", "B", "FAIL", "C"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().BeEquivalentTo(["A", "B", "C"]);
        result.State.FailedItems.Should().BeEquivalentTo(["FAIL", "FAIL"]);
    }

    #endregion

    #region WhenAll/WhenAny Scenarios

    [Fact]
    public async Task WhenAll_AllRequestsComplete()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new WhenAllBasicFlow();
        var executor = new DslFlowExecutor<ParallelState, WhenAllBasicFlow>(mediator, store, config);

        SetupMediatorForParallel(mediator);
        var state = new ParallelState { FlowId = "whenall-001" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CompletedTasks.Should().Be(3);
    }

    [Fact]
    public async Task WhenAll_FailsIfAnyFails()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new WhenAllWithFailureFlow();
        var executor = new DslFlowExecutor<ParallelState, WhenAllWithFailureFlow>(mediator, store, config);

        SetupMediatorWithOneFailure(mediator);
        var state = new ParallelState { FlowId = "whenall-fail-001" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task WhenAny_CompletesOnFirstSuccess()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new WhenAnyBasicFlow();
        var executor = new DslFlowExecutor<ParallelState, WhenAnyBasicFlow>(mediator, store, config);

        SetupMediatorForWhenAny(mediator);
        var state = new ParallelState { FlowId = "whenany-001" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.FirstCompleted.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Combined Control Flow Scenarios

    [Fact]
    public async Task Combined_IfInsideForEach_ExecutesCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new IfInsideForEachFlow();
        var executor = new DslFlowExecutor<CollectionState, IfInsideForEachFlow>(mediator, store, config);

        SetupMediatorForCollection(mediator);
        var state = new CollectionState
        {
            FlowId = "if-in-foreach-001",
            Items = ["VIP-A", "B", "VIP-C", "D"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.VipItems.Should().BeEquivalentTo(["VIP-A", "VIP-C"]);
        result.State.RegularItems.Should().BeEquivalentTo(["B", "D"]);
    }

    [Fact]
    public async Task Combined_SwitchInsideForEach_ExecutesCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SwitchInsideForEachFlow();
        var executor = new DslFlowExecutor<CollectionState, SwitchInsideForEachFlow>(mediator, store, config);

        SetupMediatorForCollection(mediator);
        var state = new CollectionState
        {
            FlowId = "switch-in-foreach-001",
            Items = ["gold-1", "silver-2", "bronze-3", "gold-4"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.GoldCount.Should().Be(2);
        result.State.SilverCount.Should().Be(1);
        result.State.BronzeCount.Should().Be(1);
    }

    [Fact]
    public async Task Combined_ForEachWithWhenAll_ProcessesInParallel()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ForEachWithParallelFlow();
        var executor = new DslFlowExecutor<CollectionState, ForEachWithParallelFlow>(mediator, store, config);

        SetupMediatorForCollection(mediator);
        var state = new CollectionState
        {
            FlowId = "foreach-parallel-001",
            Items = ["A", "B", "C"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ForEach_EmptyCollection_Succeeds()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ForEachBasicFlow();
        var executor = new DslFlowExecutor<CollectionState, ForEachBasicFlow>(mediator, store, config);

        var state = new CollectionState { FlowId = "empty-001", Items = [] };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task If_NoElse_SkipsWhenFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new IfWithoutElseFlow();
        var executor = new DslFlowExecutor<BranchingState, IfWithoutElseFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "no-else-001", Value = 10 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().BeNull();
    }

    [Fact]
    public async Task Switch_NoMatchingCase_ExecutesDefault()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SwitchCaseFlow();
        var executor = new DslFlowExecutor<BranchingState, SwitchCaseFlow>(mediator, store, config);

        SetupMediatorSuccess(mediator);
        var state = new BranchingState { FlowId = "no-match-001", Category = "platinum" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Branch.Should().Be("default-processing");
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

    private void SetupMediatorForCollection(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        mediator.SendAsync<ProcessItemRequest, string>(Arg.Any<ProcessItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<ProcessItemRequest>();
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{req.Item}"));
            });
    }

    private void SetupMediatorWithFailingItem(ICatgaMediator mediator, string failItem)
    {
        mediator.SendAsync<ProcessItemRequest, string>(Arg.Any<ProcessItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<ProcessItemRequest>();
                if (req.Item == failItem)
                    return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("Item failed"));
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{req.Item}"));
            });
    }

    private void SetupMediatorForParallel(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
    }

    private void SetupMediatorWithOneFailure(ICatgaMediator mediator)
    {
        var callCount = 0;
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 2)
                    return new ValueTask<CatgaResult>(CatgaResult.Failure("One task failed"));
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
    }

    private void SetupMediatorForWhenAny(ICatgaMediator mediator)
    {
        mediator.SendAsync<IRequest<string>, string>(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("first")));
    }

    #endregion

    #region Test State Classes

    public class BranchingState : BaseFlowState
    {
        public int Value { get; set; }
        public string? Category { get; set; }
        public string? Branch { get; set; }
    }

    public class CollectionState : BaseFlowState
    {
        public List<string> Items { get; set; } = [];
        public List<string> ProcessedItems { get; set; } = [];
        public List<string> FailedItems { get; set; } = [];
        public List<string> VipItems { get; set; } = [];
        public List<string> RegularItems { get; set; } = [];
        public int GoldCount { get; set; }
        public int SilverCount { get; set; }
        public int BronzeCount { get; set; }
    }

    public class ParallelState : BaseFlowState
    {
        public int CompletedTasks { get; set; }
        public string? FirstCompleted { get; set; }
    }

    #endregion

    #region Test Request Classes

    public record SetBranchRequest(string Branch) : IRequest;
    public record ProcessItemRequest(string Item) : IRequest<string>;
    public record ParallelTaskRequest(int TaskId) : IRequest;
    public record ParallelTaskWithResultRequest(int TaskId) : IRequest<string>;

    #endregion

    #region Test Flow Configs

    public class IfThenElseFlow : FlowConfig<BranchingState>
    {
        protected override void Configure(IFlowBuilder<BranchingState> flow)
        {
            flow.If(s => s.Value > 50)
                .Send(s => new SetBranchRequest("then"))
                .Else()
                .Send(s => new SetBranchRequest("else"))
                .EndIf();
        }
    }

    public class IfElseIfElseFlow : FlowConfig<BranchingState>
    {
        protected override void Configure(IFlowBuilder<BranchingState> flow)
        {
            flow.If(s => s.Value > 80)
                .Send(s => new SetBranchRequest("then"))
                .ElseIf(s => s.Value > 30)
                .Send(s => new SetBranchRequest("elseif"))
                .Else()
                .Send(s => new SetBranchRequest("else"))
                .EndIf();
        }
    }

    public class NestedIfFlow : FlowConfig<BranchingState>
    {
        protected override void Configure(IFlowBuilder<BranchingState> flow)
        {
            flow.If(s => s.Value > 50)
                .If(s => s.Category == "premium")
                .Send(s => new SetBranchRequest("premium-high"))
                .Else()
                .Send(s => new SetBranchRequest("regular-high"))
                .EndIf()
                .EndIf();
        }
    }

    public class IfWithoutElseFlow : FlowConfig<BranchingState>
    {
        protected override void Configure(IFlowBuilder<BranchingState> flow)
        {
            flow.If(s => s.Value > 50)
                .Send(s => new SetBranchRequest("executed"))
                .EndIf();
        }
    }

    public class SwitchCaseFlow : FlowConfig<BranchingState>
    {
        protected override void Configure(IFlowBuilder<BranchingState> flow)
        {
            flow.Switch(s => s.Category ?? "unknown")
                .Case("gold", c => c.Send(s => new SetBranchRequest("gold-processing")))
                .Case("silver", c => c.Send(s => new SetBranchRequest("silver-processing")))
                .Case("bronze", c => c.Send(s => new SetBranchRequest("bronze-processing")))
                .Default(c => c.Send(s => new SetBranchRequest("default-processing")))
                .EndSwitch();
        }
    }

    public class SwitchWithNestedIfFlow : FlowConfig<BranchingState>
    {
        protected override void Configure(IFlowBuilder<BranchingState> flow)
        {
            flow.Switch(s => s.Category ?? "unknown")
                .Case("gold", c => c
                    .Send(s => new SetBranchRequest(s.Value > 500 ? "gold-vip" : "gold-regular")))
                .Default(c => c.Send(s => new SetBranchRequest("default")))
                .EndSwitch();
        }
    }

    public class ForEachBasicFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items)
                .Send(item => new ProcessItemRequest(item))
                .OnItemSuccess((s, item, _) => s.ProcessedItems.Add(item))
                .EndForEach();
        }
    }

    public class ForEachWithConditionFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items.Where(i => i != "SKIP"))
                .Send(item => new ProcessItemRequest(item))
                .OnItemSuccess((s, item, _) => s.ProcessedItems.Add(item))
                .EndForEach();
        }
    }

    public class ForEachStopOnFailureFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items)
                .Send(item => new ProcessItemRequest(item))
                .OnItemSuccess((s, item, _) => s.ProcessedItems.Add(item))
                .StopOnFirstFailure()
                .EndForEach();
        }
    }

    public class ForEachContinueOnFailureFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items)
                .Send(item => new ProcessItemRequest(item))
                .OnItemSuccess((s, item, _) => s.ProcessedItems.Add(item))
                .OnItemFail((s, item, _) => s.FailedItems.Add(item))
                .ContinueOnFailure()
                .EndForEach();
        }
    }

    public class WhenAllBasicFlow : FlowConfig<ParallelState>
    {
        protected override void Configure(IFlowBuilder<ParallelState> flow)
        {
            flow.WhenAll(
                s => new ParallelTaskRequest(1),
                s => new ParallelTaskRequest(2),
                s => new ParallelTaskRequest(3));
        }
    }

    public class WhenAllWithFailureFlow : FlowConfig<ParallelState>
    {
        protected override void Configure(IFlowBuilder<ParallelState> flow)
        {
            flow.WhenAll(
                s => new ParallelTaskRequest(1),
                s => new ParallelTaskRequest(2),
                s => new ParallelTaskRequest(3));
        }
    }

    public class WhenAnyBasicFlow : FlowConfig<ParallelState>
    {
        protected override void Configure(IFlowBuilder<ParallelState> flow)
        {
            flow.WhenAny(
                s => new ParallelTaskWithResultRequest(1),
                s => new ParallelTaskWithResultRequest(2),
                s => new ParallelTaskWithResultRequest(3))
                .Into((s, r) => s.FirstCompleted = r);
        }
    }

    public class IfInsideForEachFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items)
                .Configure((item, f) => f
                    .If(s => item.StartsWith("VIP"))
                        .Send(s => new ProcessItemRequest(item))
                    .Else()
                        .Send(s => new ProcessItemRequest(item))
                    .EndIf())
                .OnItemSuccess((s, item, _) =>
                {
                    if (item.StartsWith("VIP"))
                        s.VipItems.Add(item);
                    else
                        s.RegularItems.Add(item);
                })
                .EndForEach();
        }
    }

    public class SwitchInsideForEachFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items)
                .Send(item => new ProcessItemRequest(item))
                .OnItemSuccess((s, item, _) =>
                {
                    if (item.StartsWith("gold")) s.GoldCount++;
                    else if (item.StartsWith("silver")) s.SilverCount++;
                    else if (item.StartsWith("bronze")) s.BronzeCount++;
                })
                .EndForEach();
        }
    }

    public class ForEachWithParallelFlow : FlowConfig<CollectionState>
    {
        protected override void Configure(IFlowBuilder<CollectionState> flow)
        {
            flow.ForEach(s => s.Items)
                .Send(item => new ProcessItemRequest(item))
                .OnItemSuccess((s, item, _) => s.ProcessedItems.Add(item))
                .WithParallelism(3)
                .EndForEach();
        }
    }

    #endregion
}
