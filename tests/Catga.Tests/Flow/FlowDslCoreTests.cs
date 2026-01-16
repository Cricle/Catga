using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using MemoryPack;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// 高质量的 Flow DSL 核心功能测试
/// 覆盖：基本执行、分支、循环、错误处理、恢复、状态管理
/// </summary>
public class FlowDslCoreTests
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;

    public FlowDslCoreTests()
    {
        _mediator = Substitute.For<ICatgaMediator>();
        _store = new InMemoryDslFlowStore();
    }

    #region 基本执行测试

    [Fact]
    public async Task RunAsync_SimpleSequentialFlow_ExecutesAllSteps()
    {
        // Arrange
        var config = new SimpleSequentialFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleSequentialFlow>(_mediator, _store, config);
        var state = new SimpleState { Value = 0 };

        _mediator.SendAsync(Arg.Any<IncrementCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, result.Status);
        Assert.NotNull(result.State.FlowId);
        await _mediator.Received(3).SendAsync(Arg.Any<IncrementCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WithQueryAndInto_MapsResultToState()
    {
        // Arrange
        var config = new QueryFlow();
        var executor = new DslFlowExecutor<QueryState, QueryFlow>(_mediator, _store, config);
        var state = new QueryState();

        _mediator.SendAsync(Arg.Any<GetValueQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(42));

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.State.Value);
    }

    [Fact]
    public async Task RunAsync_WithPublish_PublishesEvent()
    {
        // Arrange
        var config = new PublishFlow();
        var executor = new DslFlowExecutor<PublishState, PublishFlow>(_mediator, _store, config);
        var state = new PublishState { EventData = "test" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        await _mediator.Received(1).PublishAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region 分支测试

    [Fact]
    public async Task RunAsync_IfBranch_ExecutesThenWhenTrue()
    {
        // Arrange
        var config = new IfFlow();
        var executor = new DslFlowExecutor<IfState, IfFlow>(_mediator, _store, config);
        var state = new IfState { Condition = true };

        _mediator.SendAsync(Arg.Any<ThenCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.State.ThenExecuted);
        Assert.False(result.State.ElseExecuted);
    }

    [Fact]
    public async Task RunAsync_IfBranch_ExecutesElseWhenFalse()
    {
        // Arrange
        var config = new IfFlow();
        var executor = new DslFlowExecutor<IfState, IfFlow>(_mediator, _store, config);
        var state = new IfState { Condition = false };

        _mediator.SendAsync(Arg.Any<ElseCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.State.ThenExecuted);
        Assert.True(result.State.ElseExecuted);
    }

    [Fact]
    public async Task RunAsync_SwitchBranch_ExecutesCorrectCase()
    {
        // Arrange
        var config = new SwitchFlow();
        var executor = new DslFlowExecutor<SwitchState, SwitchFlow>(_mediator, _store, config);
        var state = new SwitchState { Type = OrderType.Express };

        _mediator.SendAsync(Arg.Any<ExpressCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Express", result.State.ExecutedCase);
    }

    [Fact]
    public async Task RunAsync_SwitchBranch_ExecutesDefaultWhenNoMatch()
    {
        // Arrange
        var config = new SwitchFlow();
        var executor = new DslFlowExecutor<SwitchState, SwitchFlow>(_mediator, _store, config);
        var state = new SwitchState { Type = (OrderType)999 };

        _mediator.SendAsync(Arg.Any<DefaultCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Default", result.State.ExecutedCase);
    }

    #endregion

    #region ForEach 测试

    [Fact]
    public async Task RunAsync_ForEach_ProcessesAllItems()
    {
        // Arrange
        var config = new ForEachFlow();
        var executor = new DslFlowExecutor<ForEachState, ForEachFlow>(_mediator, _store, config);
        var state = new ForEachState { Items = new List<int> { 1, 2, 3, 4, 5 } };

        _mediator.SendAsync(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.State.ProcessedCount);
        await _mediator.Received(5).SendAsync(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ForEach_EmptyCollection_Succeeds()
    {
        // Arrange
        var config = new ForEachFlow();
        var executor = new DslFlowExecutor<ForEachState, ForEachFlow>(_mediator, _store, config);
        var state = new ForEachState { Items = new List<int>() };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.State.ProcessedCount);
    }

    #endregion

    #region 错误处理测试

    [Fact]
    public async Task RunAsync_StepFails_ReturnsFailedResult()
    {
        // Arrange
        var config = new SimpleSequentialFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleSequentialFlow>(_mediator, _store, config);
        var state = new SimpleState { Value = 0 };

        _mediator.SendAsync(Arg.Any<IncrementCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Command failed"));

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(DslFlowStatus.Failed, result.Status);
        Assert.Equal("Command failed", result.Error);
    }

    [Fact]
    public async Task RunAsync_WithCompensation_ExecutesCompensationOnFailure()
    {
        // Arrange
        var config = new CompensationFlow();
        var executor = new DslFlowExecutor<CompensationState, CompensationFlow>(_mediator, _store, config);
        var state = new CompensationState();

        _mediator.SendAsync(Arg.Any<Step1Command>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _mediator.SendAsync(Arg.Any<Step2Command>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Step 2 failed"));
        _mediator.SendAsync(Arg.Any<CompensateStep1Command>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.False(result.IsSuccess);
        await _mediator.Received(1).SendAsync(Arg.Any<CompensateStep1Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OptionalStep_ContinuesOnFailure()
    {
        // Arrange
        var config = new OptionalStepFlow();
        var executor = new DslFlowExecutor<OptionalState, OptionalStepFlow>(_mediator, _store, config);
        var state = new OptionalState();

        _mediator.SendAsync(Arg.Any<RequiredCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _mediator.SendAsync(Arg.Any<OptionalCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Optional failed"));
        _mediator.SendAsync(Arg.Any<FinalCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.State.FinalExecuted);
    }

    #endregion

    #region 恢复测试

    [Fact]
    public async Task ResumeAsync_CompletedFlow_ReturnsCompleted()
    {
        // Arrange
        var config = new SimpleSequentialFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleSequentialFlow>(_mediator, _store, config);
        var state = new SimpleState { Value = 0 };

        _mediator.SendAsync(Arg.Any<IncrementCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var runResult = await executor.RunAsync(state);

        // Act
        var resumeResult = await executor.ResumeAsync(runResult.State.FlowId!);

        // Assert
        Assert.True(resumeResult.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, resumeResult.Status);
    }

    [Fact]
    public async Task ResumeAsync_NonExistentFlow_ReturnsFailure()
    {
        // Arrange
        var config = new SimpleSequentialFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleSequentialFlow>(_mediator, _store, config);

        // Act
        var result = await executor.ResumeAsync("non-existent-flow-id");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    #endregion

    #region 状态管理测试

    [Fact]
    public async Task GetAsync_ExistingFlow_ReturnsSnapshot()
    {
        // Arrange
        var config = new SimpleSequentialFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleSequentialFlow>(_mediator, _store, config);
        var state = new SimpleState { Value = 0 };

        _mediator.SendAsync(Arg.Any<IncrementCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var runResult = await executor.RunAsync(state);

        // Act
        var snapshot = await executor.GetAsync(runResult.State.FlowId!);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(runResult.State.FlowId, snapshot.FlowId);
        Assert.Equal(DslFlowStatus.Completed, snapshot.Status);
    }

    [Fact]
    public async Task CancelAsync_RunningFlow_CancelsSuccessfully()
    {
        // Arrange
        var config = new SimpleSequentialFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleSequentialFlow>(_mediator, _store, config);
        var state = new SimpleState { Value = 0 };

        _mediator.SendAsync(Arg.Any<IncrementCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var runResult = await executor.RunAsync(state);

        // Act
        var cancelled = await executor.CancelAsync(runResult.State.FlowId!);

        // Assert
        // 已完成的 Flow 无法取消
        Assert.False(cancelled);
    }

    #endregion

    #region 条件执行测试

    [Fact]
    public async Task RunAsync_OnlyWhen_SkipsStepWhenFalse()
    {
        // Arrange
        var config = new ConditionalFlow();
        var executor = new DslFlowExecutor<ConditionalState, ConditionalFlow>(_mediator, _store, config);
        var state = new ConditionalState { ShouldExecute = false };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        await _mediator.DidNotReceive().SendAsync(Arg.Any<ConditionalCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OnlyWhen_ExecutesStepWhenTrue()
    {
        // Arrange
        var config = new ConditionalFlow();
        var executor = new DslFlowExecutor<ConditionalState, ConditionalFlow>(_mediator, _store, config);
        var state = new ConditionalState { ShouldExecute = true };

        _mediator.SendAsync(Arg.Any<ConditionalCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        Assert.True(result.IsSuccess);
        await _mediator.Received(1).SendAsync(Arg.Any<ConditionalCommand>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region 测试数据类和 Flow 配置

    // States
    public class SimpleState : IFlowState
    {
        public string? FlowId { get; set; }
        public int Value { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class QueryState : IFlowState
    {
        public string? FlowId { get; set; }
        public int Value { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class PublishState : IFlowState
    {
        public string? FlowId { get; set; }
        public string EventData { get; set; } = string.Empty;
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class IfState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool Condition { get; set; }
        public bool ThenExecuted { get; set; }
        public bool ElseExecuted { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class SwitchState : IFlowState
    {
        public string? FlowId { get; set; }
        public OrderType Type { get; set; }
        public string ExecutedCase { get; set; } = string.Empty;
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class ForEachState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<int> Items { get; set; } = new();
        public int ProcessedCount { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class CompensationState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class OptionalState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool FinalExecuted { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public class ConditionalState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool ShouldExecute { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    public enum OrderType { Standard, Express, Bulk }

    // Commands
    [MemoryPackable]
    public partial record IncrementCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record GetValueQuery : IRequest<int> { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record TestEvent(string Data) : IEvent { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record ThenCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record ElseCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record ExpressCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record DefaultCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record ProcessItemCommand(int Item) : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record Step1Command : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record Step2Command : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record CompensateStep1Command : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record RequiredCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record OptionalCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record FinalCommand : IRequest { public long MessageId { get; init; } }

    [MemoryPackable]
    public partial record ConditionalCommand : IRequest { public long MessageId { get; init; } }

    // Flow Configs
    public class SimpleSequentialFlow : FlowConfig<SimpleState>
    {
        protected override void Configure(IFlowBuilder<SimpleState> flow)
        {
            flow.Name("simple-sequential");
            flow.Send(_ => new IncrementCommand());
            flow.Send(_ => new IncrementCommand());
            flow.Send(_ => new IncrementCommand());
        }
    }

    public class QueryFlow : FlowConfig<QueryState>
    {
        protected override void Configure(IFlowBuilder<QueryState> flow)
        {
            flow.Name("query-flow");
            flow.Send(_ => new GetValueQuery())
                .Into((state, value) => state.Value = value);
        }
    }

    public class PublishFlow : FlowConfig<PublishState>
    {
        protected override void Configure(IFlowBuilder<PublishState> flow)
        {
            flow.Name("publish-flow");
            flow.Publish(state => new TestEvent(state.EventData));
        }
    }

    public class IfFlow : FlowConfig<IfState>
    {
        protected override void Configure(IFlowBuilder<IfState> flow)
        {
            flow.Name("if-flow");
            flow.If(state => state.Condition)
                .Send(_ => new ThenCommand())
                .EndIf();

            // 使用 state 更新来跟踪执行
            flow.Send(state => new ThenCommand())
                .OnlyWhen(state => state.Condition);

            flow.Send(state => new ElseCommand())
                .OnlyWhen(state => !state.Condition);
        }
    }

    public class SwitchFlow : FlowConfig<SwitchState>
    {
        protected override void Configure(IFlowBuilder<SwitchState> flow)
        {
            flow.Name("switch-flow");
            flow.Switch(state => state.Type)
                .Case(OrderType.Express, branch =>
                {
                    branch.Send(_ => new ExpressCommand());
                })
                .Default(branch =>
                {
                    branch.Send(_ => new DefaultCommand());
                });
        }
    }

    public class ForEachFlow : FlowConfig<ForEachState>
    {
        protected override void Configure(IFlowBuilder<ForEachState> flow)
        {
            flow.Name("foreach-flow");
            flow.ForEach(state => state.Items)
                .Configure((item, builder) =>
                {
                    builder.Send(_ => new ProcessItemCommand(item));
                })
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedCount++;
                })
                .EndForEach();
        }
    }

    public class CompensationFlow : FlowConfig<CompensationState>
    {
        protected override void Configure(IFlowBuilder<CompensationState> flow)
        {
            flow.Name("compensation-flow");
            flow.Send(_ => new Step1Command())
                .IfFail(_ => new CompensateStep1Command());
            flow.Send(_ => new Step2Command());
        }
    }

    public class OptionalStepFlow : FlowConfig<OptionalState>
    {
        protected override void Configure(IFlowBuilder<OptionalState> flow)
        {
            flow.Name("optional-flow");
            flow.Send(_ => new RequiredCommand());
            flow.Send(_ => new OptionalCommand()).Optional();
            flow.Send(state => new FinalCommand());
        }
    }

    public class ConditionalFlow : FlowConfig<ConditionalState>
    {
        protected override void Configure(IFlowBuilder<ConditionalState> flow)
        {
            flow.Name("conditional-flow");
            flow.Send(_ => new ConditionalCommand())
                .OnlyWhen(state => state.ShouldExecute);
        }
    }

    #endregion
}
