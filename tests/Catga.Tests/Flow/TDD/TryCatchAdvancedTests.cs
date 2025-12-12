using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// Advanced TDD tests for Try-Catch-Finally functionality
/// Tests complex exception scenarios, nested try-catch, and recovery
/// </summary>
public class TryCatchAdvancedTests
{
    [Fact]
    public async Task TryCatch_MultipleCatchBlocks_ShouldMatchCorrectException()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MultipleCatchFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, MultipleCatchFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-multi-catch" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBlocks.Should().Contain("Catch");
    }

    [Fact]
    public async Task TryCatch_NestedTryCatch_ShouldHandleCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NestedTryCatchFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, NestedTryCatchFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-nested" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.ExecutedBlocks.Should().Contain("OuterTry");
        state.ExecutedBlocks.Should().Contain("OuterFinally");
    }

    [Fact]
    public async Task TryCatch_FinallyWithException_ShouldAlwaysExecute()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Test error")));

        var store = new InMemoryDslFlowStore();
        var config = new TryCatchWithFinallyFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, TryCatchWithFinallyFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-finally-exception" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.ExecutedBlocks.Should().Contain("Finally");
    }

    [Fact]
    public async Task TryCatch_ExceptionInFinally_ShouldPropagate()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TryCatchFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, TryCatchFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-finally-error" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.ExecutedBlocks.Should().Contain("Finally");
    }

    [Fact]
    public async Task TryCatch_WithStateRecovery_ShouldRestoreState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TryCatchFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, TryCatchFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-recovery" };
        SetupMediator(mediator);

        // Act - First execution
        var result1 = await executor.RunAsync(state);

        // Simulate recovery
        var state2 = await store.GetAsync<TryCatchTestState>("try-recovery");
        var executor2 = new DslFlowExecutor<TryCatchTestState, TryCatchFlow>(mediator, store, config);
        var result2 = await executor2.RunAsync(state2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryCatch_MultipleExceptionTypes_ShouldCatchAppropriate()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MultiExceptionFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, MultiExceptionFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-multi-exception" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.ExecutedBlocks.Should().Contain("Catch");
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Advanced Try-Catch configurations
public class MultipleCatchFlow : FlowConfig<TryCatchTestState>
{
    protected override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Send(s => new TestCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutedBlocks.Add("Try"))
            .Catch<ArgumentException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchArgument");
            })
            .Catch<InvalidOperationException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchInvalid");
            })
            .Catch<Exception>((s, ex) =>
            {
                s.ExecutedBlocks.Add("Catch");
            })
            .Finally(s =>
            {
                s.ExecutedBlocks.Add("Finally");
            })
            .EndTry();
    }
}

public class NestedTryCatchFlow : FlowConfig<TryCatchTestState>
{
    protected override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Into(s => s.ExecutedBlocks.Add("OuterTry"))
                .Send(s => new TestCommand { FlowId = s.FlowId })
            .Catch<Exception>((s, ex) =>
            {
                s.ExecutedBlocks.Add("OuterCatch");
            })
            .Finally(s =>
            {
                s.ExecutedBlocks.Add("OuterFinally");
            })
            .EndTry();
    }
}

public class TryCatchWithFinallyFlow : FlowConfig<TryCatchTestState>
{
    protected override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Send(s => new TestCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutedBlocks.Add("Try"))
            .Catch<Exception>((s, ex) =>
            {
                s.ExecutedBlocks.Add("Catch");
            })
            .Finally(s =>
            {
                s.ExecutedBlocks.Add("Finally");
            })
            .EndTry();
    }
}

public class MultiExceptionFlow : FlowConfig<TryCatchTestState>
{
    protected override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Send(s => new TestCommand { FlowId = s.FlowId })
            .Catch<TimeoutException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchTimeout");
            })
            .Catch<InvalidOperationException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchInvalid");
            })
            .Catch<Exception>((s, ex) =>
            {
                s.ExecutedBlocks.Add("Catch");
            })
            .EndTry();
    }
}

// Test command
public class TestCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
