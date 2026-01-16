using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using MemoryPack;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// 高质量严格的 Flow DSL 单元测试
/// 覆盖：FlowConfig 构建、状态管理、存储操作
/// </summary>
public class FlowDslStrictTests
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;

    public FlowDslStrictTests()
    {
        _mediator = Substitute.For<ICatgaMediator>();
        _store = new InMemoryDslFlowStore();
    }

    #region 1. FlowConfig 构建验证

    [Fact]
    public void FlowConfig_Build_OnlyBuildsOnce()
    {
        var config = new BuildCountingFlow();
        config.Build();
        config.Build();
        config.Build();
        Assert.Equal(1, config.BuildCount);
    }

    [Fact]
    public void FlowConfig_Steps_ReturnsCorrectCount()
    {
        var config = new ThreeStepFlow();
        config.Build();
        Assert.Equal(3, config.Steps.Count);
    }

    [Fact]
    public void FlowConfig_Name_ReturnsConfiguredName()
    {
        var config = new NamedFlow();
        config.Build();
        Assert.Equal("custom-flow-name", config.Name);
    }

    [Fact]
    public void FlowConfig_DefaultTimeout_ReturnsConfiguredValue()
    {
        var config = new TimeoutConfiguredFlow();
        config.Build();
        Assert.Equal(TimeSpan.FromSeconds(30), config.DefaultTimeout);
    }

    #endregion

    #region 2. 执行顺序严格验证

    [Fact]
    public async Task RunAsync_StepsExecuteInOrder()
    {
        var executionOrder = new List<int>();
        var config = new OrderTrackingFlow();
        var executor = new DslFlowExecutor<OrderTrackingState, OrderTrackingFlow>(_mediator, _store, config);
        var state = new OrderTrackingState { ExecutionOrder = executionOrder };

        _mediator.SendAsync(Arg.Any<Step1Cmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executionOrder.Add(1); return new ValueTask<CatgaResult>(CatgaResult.Success()); });
        _mediator.SendAsync(Arg.Any<Step2Cmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executionOrder.Add(2); return new ValueTask<CatgaResult>(CatgaResult.Success()); });
        _mediator.SendAsync(Arg.Any<Step3Cmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executionOrder.Add(3); return new ValueTask<CatgaResult>(CatgaResult.Success()); });

        await executor.RunAsync(state);
        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
    }

    [Fact]
    public async Task RunAsync_StopsOnFirstFailure()
    {
        var executionOrder = new List<int>();
        var config = new OrderTrackingFlow();
        var executor = new DslFlowExecutor<OrderTrackingState, OrderTrackingFlow>(_mediator, _store, config);
        var state = new OrderTrackingState { ExecutionOrder = executionOrder };

        _mediator.SendAsync(Arg.Any<Step1Cmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executionOrder.Add(1); return new ValueTask<CatgaResult>(CatgaResult.Success()); });
        _mediator.SendAsync(Arg.Any<Step2Cmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executionOrder.Add(2); return new ValueTask<CatgaResult>(CatgaResult.Failure("Step 2 failed")); });
        _mediator.SendAsync(Arg.Any<Step3Cmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executionOrder.Add(3); return new ValueTask<CatgaResult>(CatgaResult.Success()); });

        var result = await executor.RunAsync(state);
        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { 1, 2 }, executionOrder);
    }

    #endregion

    #region 3. 状态一致性验证

    [Fact]
    public async Task RunAsync_FlowIdIsAssignedIfNull()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);
        var state = new SimpleState { FlowId = null };

        _mediator.SendAsync(Arg.Any<NoOpCmd>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        var result = await executor.RunAsync(state);
        Assert.NotNull(result.State.FlowId);
        Assert.NotEmpty(result.State.FlowId);
    }

    [Fact]
    public async Task RunAsync_FlowIdIsPreservedIfProvided()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);
        var customFlowId = "my-custom-flow-id";
        var state = new SimpleState { FlowId = customFlowId };

        _mediator.SendAsync(Arg.Any<NoOpCmd>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        var result = await executor.RunAsync(state);
        Assert.Equal(customFlowId, result.State.FlowId);
    }

    #endregion

    #region 4. Optional 步骤验证

    [Fact]
    public async Task Optional_FailedStep_ContinuesExecution()
    {
        var executed = new List<string>();
        var config = new OptionalStepFlow();
        var executor = new DslFlowExecutor<SimpleState, OptionalStepFlow>(_mediator, _store, config);
        var state = new SimpleState();

        _mediator.SendAsync(Arg.Any<BeforeCmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executed.Add("before"); return new ValueTask<CatgaResult>(CatgaResult.Success()); });
        _mediator.SendAsync(Arg.Any<OptionalCmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executed.Add("optional"); return new ValueTask<CatgaResult>(CatgaResult.Failure("Optional failed")); });
        _mediator.SendAsync(Arg.Any<AfterCmd>(), Arg.Any<CancellationToken>())
            .Returns(ci => { executed.Add("after"); return new ValueTask<CatgaResult>(CatgaResult.Success()); });

        var result = await executor.RunAsync(state);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "before", "optional", "after" }, executed);
    }

    #endregion

    #region 5. Resume 验证

    [Fact]
    public async Task ResumeAsync_NonExistentFlow_ReturnsFailure()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);

        var result = await executor.ResumeAsync("non-existent-id");
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ResumeAsync_CompletedFlow_ReturnsCompleted()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);
        var state = new SimpleState();

        _mediator.SendAsync(Arg.Any<NoOpCmd>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        var runResult = await executor.RunAsync(state);
        var resumeResult = await executor.ResumeAsync(runResult.State.FlowId!);
        Assert.True(resumeResult.IsSuccess);
        Assert.Equal(DslFlowStatus.Completed, resumeResult.Status);
    }

    #endregion

    #region 6. GetAsync 验证

    [Fact]
    public async Task GetAsync_ExistingFlow_ReturnsSnapshot()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);
        var state = new SimpleState();

        _mediator.SendAsync(Arg.Any<NoOpCmd>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        var runResult = await executor.RunAsync(state);
        var snapshot = await executor.GetAsync(runResult.State.FlowId!);
        Assert.NotNull(snapshot);
        Assert.Equal(runResult.State.FlowId, snapshot.FlowId);
        Assert.Equal(DslFlowStatus.Completed, snapshot.Status);
    }

    [Fact]
    public async Task GetAsync_NonExistentFlow_ReturnsNull()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);

        var snapshot = await executor.GetAsync("non-existent-id");
        Assert.Null(snapshot);
    }

    #endregion

    #region 7. Publish 事件验证

    [Fact]
    public async Task Publish_PublishesEventToMediator()
    {
        var config = new PublishEventFlow();
        var executor = new DslFlowExecutor<EventState, PublishEventFlow>(_mediator, _store, config);
        var state = new EventState { EventData = "test-data" };

        var result = await executor.RunAsync(state);
        Assert.True(result.IsSuccess);
        await _mediator.Received(1).PublishAsync(
            Arg.Is<TestEvent>(e => e.Data == "test-data"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region 8. CancelAsync 验证

    [Fact]
    public async Task CancelAsync_CompletedFlow_ReturnsFalse()
    {
        var config = new SimpleFlow();
        var executor = new DslFlowExecutor<SimpleState, SimpleFlow>(_mediator, _store, config);
        var state = new SimpleState();

        _mediator.SendAsync(Arg.Any<NoOpCmd>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        var runResult = await executor.RunAsync(state);
        var cancelled = await executor.CancelAsync(runResult.State.FlowId!);
        Assert.False(cancelled);
    }

    #endregion
}


#region Test States

public class SimpleState : IFlowState
{
    private int _changedMask;
    public string? FlowId { get; set; }
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public class OrderTrackingState : IFlowState
{
    private int _changedMask;
    public string? FlowId { get; set; }
    public List<int> ExecutionOrder { get; set; } = [];
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public class EventState : IFlowState
{
    private int _changedMask;
    public string? FlowId { get; set; }
    public string EventData { get; set; } = "";
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() => [];
}

#endregion

#region Test Commands

[MemoryPackable]
public partial record NoOpCmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record Step1Cmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record Step2Cmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record Step3Cmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record BeforeCmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OptionalCmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record AfterCmd : IRequest
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record TestEvent(string Data) : IEvent
{
    public long MessageId { get; init; }
}

#endregion

#region Test Flow Configs

public class BuildCountingFlow : FlowConfig<SimpleState>
{
    public int BuildCount { get; private set; }

    protected override void Configure(IFlowBuilder<SimpleState> flow)
    {
        BuildCount++;
        flow.Send<SimpleState, NoOpCmd>(s => new NoOpCmd());
    }
}

public class ThreeStepFlow : FlowConfig<SimpleState>
{
    protected override void Configure(IFlowBuilder<SimpleState> flow)
    {
        flow.Send<SimpleState, Step1Cmd>(s => new Step1Cmd());
        flow.Send<SimpleState, Step2Cmd>(s => new Step2Cmd());
        flow.Send<SimpleState, Step3Cmd>(s => new Step3Cmd());
    }
}

public class NamedFlow : FlowConfig<SimpleState>
{
    protected override void Configure(IFlowBuilder<SimpleState> flow)
    {
        flow.Name("custom-flow-name");
        flow.Send<SimpleState, NoOpCmd>(s => new NoOpCmd());
    }
}

public class TimeoutConfiguredFlow : FlowConfig<SimpleState>
{
    protected override void Configure(IFlowBuilder<SimpleState> flow)
    {
        flow.Timeout(TimeSpan.FromSeconds(30));
        flow.Send<SimpleState, NoOpCmd>(s => new NoOpCmd());
    }
}

public class OrderTrackingFlow : FlowConfig<OrderTrackingState>
{
    protected override void Configure(IFlowBuilder<OrderTrackingState> flow)
    {
        flow.Send<OrderTrackingState, Step1Cmd>(s => new Step1Cmd());
        flow.Send<OrderTrackingState, Step2Cmd>(s => new Step2Cmd());
        flow.Send<OrderTrackingState, Step3Cmd>(s => new Step3Cmd());
    }
}

public class SimpleFlow : FlowConfig<SimpleState>
{
    protected override void Configure(IFlowBuilder<SimpleState> flow)
    {
        flow.Send<SimpleState, NoOpCmd>(s => new NoOpCmd());
    }
}

public class OptionalStepFlow : FlowConfig<SimpleState>
{
    protected override void Configure(IFlowBuilder<SimpleState> flow)
    {
        flow.Send<SimpleState, BeforeCmd>(s => new BeforeCmd());
        flow.Send<SimpleState, OptionalCmd>(s => new OptionalCmd()).Optional();
        flow.Send<SimpleState, AfterCmd>(s => new AfterCmd());
    }
}

public class PublishEventFlow : FlowConfig<EventState>
{
    protected override void Configure(IFlowBuilder<EventState> flow)
    {
        flow.Publish<EventState, TestEvent>(s => new TestEvent(s.EventData));
    }
}

#endregion
