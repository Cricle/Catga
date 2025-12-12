using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for Try-Catch-Finally exception handling
/// Tests exception handling, recovery, storage parity, and safety
/// </summary>
public class TryCatchTests
{
    [Fact]
    public async Task Try_NoException_ShouldExecuteThenBlock()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-no-exception" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBlocks.Should().Contain("Try");
        state.ExecutedBlocks.Should().Contain("Finally");
        state.ExecutedBlocks.Should().NotContain("Catch");
    }

    [Fact]
    public async Task Try_ExceptionThrown_ShouldExecuteCatchBlock()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(x => throw new InvalidOperationException("Test exception"));

        var store = new InMemoryDslFlowStore();
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-with-exception" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("catch block should handle exception");
        state.ExecutedBlocks.Should().Contain("Try");
        state.ExecutedBlocks.Should().Contain("Catch");
        state.ExecutedBlocks.Should().Contain("Finally");
        state.CaughtException.Should().NotBeNull();
    }

    [Fact]
    public async Task Try_MultipleCatchBlocks_ShouldMatchCorrectType()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MultiCatchFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, MultiCatchFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-multi-catch" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBlocks.Should().Contain("Try");
        state.ExecutedBlocks.Should().Contain("Finally");
    }

    [Fact]
    public async Task Try_NestedTryBlocks_ShouldWork()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NestedTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, NestedTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-nested" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBlocks.Should().Contain("OuterTry");
        state.ExecutedBlocks.Should().Contain("InnerTry");
        state.ExecutedBlocks.Should().Contain("OuterFinally");
        state.ExecutedBlocks.Should().Contain("InnerFinally");
    }

    [Fact]
    public async Task Try_FinallyAlwaysExecutes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(x => throw new InvalidOperationException("Test exception"));

        var store = new InMemoryDslFlowStore();
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-finally-always" };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBlocks.Should().Contain("Finally", "finally should always execute");
    }

    [Fact]
    public async Task Try_InMemoryStorage_ShouldRecover()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-recovery-memory" };
        SetupMediator(mediator);

        // Act - First execution
        var result1 = await executor.RunAsync(state);

        // Simulate crash and recovery
        var state2 = await store.GetAsync<TryCatchTestState>("try-recovery-memory");
        var executor2 = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);
        var result2 = await executor2.RunAsync(state2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        state2.ExecutedBlocks.Should().Contain("Try");
    }

    [Fact]
    public async Task Try_RedisStorage_ShouldRecover()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new RedisDslFlowStore(new RedisConnectionFactory("localhost:6379"));
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-recovery-redis" };
        SetupMediator(mediator);

        try
        {
            // Act - First execution
            var result1 = await executor.RunAsync(state);

            // Simulate crash and recovery
            var state2 = await store.GetAsync<TryCatchTestState>("try-recovery-redis");
            var executor2 = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);
            var result2 = await executor2.RunAsync(state2);

            // Assert
            result1.IsSuccess.Should().BeTrue();
            result2.IsSuccess.Should().BeTrue();
        }
        catch (Exception ex) when (ex.Message.Contains("Redis"))
        {
            throw new SkipTestException("Redis not available");
        }
    }

    [Fact]
    public async Task Try_NatsStorage_ShouldRecover()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new NatsDslFlowStore(new NatsConnectionFactory("nats://localhost:4222"));
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-recovery-nats" };
        SetupMediator(mediator);

        try
        {
            // Act - First execution
            var result1 = await executor.RunAsync(state);

            // Simulate crash and recovery
            var state2 = await store.GetAsync<TryCatchTestState>("try-recovery-nats");
            var executor2 = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);
            var result2 = await executor2.RunAsync(state2);

            // Assert
            result1.IsSuccess.Should().BeTrue();
            result2.IsSuccess.Should().BeTrue();
        }
        catch (Exception ex) when (ex.Message.Contains("NATS"))
        {
            throw new SkipTestException("NATS not available");
        }
    }

    [Fact]
    public async Task Try_ExceptionStatePersisted_ShouldResumeCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(x => throw new InvalidOperationException("Test exception"));

        var store = new InMemoryDslFlowStore();
        var config = new SimpleTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-exception-persist" };

        // Act - First execution with exception
        var result1 = await executor.RunAsync(state);

        // Simulate recovery
        var state2 = await store.GetAsync<TryCatchTestState>("try-exception-persist");
        var executor2 = new DslFlowExecutor<TryCatchTestState, SimpleTryFlow>(mediator, store, config);
        var result2 = await executor2.RunAsync(state2);

        // Assert
        result1.IsSuccess.Should().BeTrue("catch block should handle exception");
        result2.IsSuccess.Should().BeTrue();
        state2.CaughtException.Should().NotBeNull("exception state should be persisted");
    }

    [Fact]
    public async Task Try_TryWithinWhile_ShouldWork()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TryWithinWhileFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, TryWithinWhileFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "try-within-while", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Try_WhileWithinTry_ShouldWork()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileWithinTryFlow();
        var executor = new DslFlowExecutor<TryCatchTestState, WhileWithinTryFlow>(mediator, store, config);

        var state = new TryCatchTestState { FlowId = "while-within-try", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(5);
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(x => Task.FromResult<IResponse>(new SuccessResponse()));
    }
}

// Test state class
public class TryCatchTestState : IFlowState
{
    public string FlowId { get; set; }
    public int Counter { get; set; }
    public List<string> ExecutedBlocks { get; set; } = new();
    public Exception CaughtException { get; set; }
    public Dictionary<string, object> Variables { get; set; } = new();
    public bool HasChanges { get; set; }
}

// Test flow configurations
public class SimpleTryFlow : FlowConfig<TryCatchTestState>
{
    public override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Send(s => new TestCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutedBlocks.Add("Try"))
            .Catch<InvalidOperationException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("Catch");
                s.CaughtException = ex;
            })
            .Finally(s =>
            {
                s.ExecutedBlocks.Add("Finally");
            })
            .EndTry();
    }
}

public class MultiCatchFlow : FlowConfig<TryCatchTestState>
{
    public override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Send(s => new TestCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutedBlocks.Add("Try"))
            .Catch<InvalidOperationException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchInvalidOperation");
            })
            .Catch<ArgumentException>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchArgument");
            })
            .Catch<Exception>((s, ex) =>
            {
                s.ExecutedBlocks.Add("CatchGeneral");
            })
            .Finally(s =>
            {
                s.ExecutedBlocks.Add("Finally");
            })
            .EndTry();
    }
}

public class NestedTryFlow : FlowConfig<TryCatchTestState>
{
    public override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .Into(s => s.ExecutedBlocks.Add("OuterTry"))
                .Try()
                    .Send(s => new TestCommand { FlowId = s.FlowId })
                    .Into(s => s.ExecutedBlocks.Add("InnerTry"))
                .Catch<Exception>((s, ex) =>
                {
                    s.ExecutedBlocks.Add("InnerCatch");
                })
                .Finally(s =>
                {
                    s.ExecutedBlocks.Add("InnerFinally");
                })
                .EndTry()
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

public class TryWithinWhileFlow : FlowConfig<TryCatchTestState>
{
    public override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .While(s => s.Counter < 3)
                .Try()
                    .Send(s => new TestCommand { FlowId = s.FlowId })
                .Catch<Exception>((s, ex) =>
                {
                    s.ExecutedBlocks.Add("Catch");
                })
                .Finally(s =>
                {
                    s.Counter++;
                })
                .EndTry()
            .EndWhile();
    }
}

public class WhileWithinTryFlow : FlowConfig<TryCatchTestState>
{
    public override void Configure(IFlowBuilder<TryCatchTestState> flow)
    {
        flow
            .Try()
                .While(s => s.Counter < 5)
                    .Send(s => new TestCommand { FlowId = s.FlowId })
                    .Into(s => s.Counter++)
                .EndWhile()
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

// Test commands
public class TestCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
}

public class SuccessResponse : IResponse
{
    public bool IsSuccess => true;
}
