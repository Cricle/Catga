using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Simple flow scenarios for basic workflow patterns.
/// Tests fundamental step execution, conditions, and state management.
/// </summary>
public class SimpleFlowTests
{
    #region Test State

    public class SimpleState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Counter { get; set; }
        public string Message { get; set; } = "";
        public bool Flag { get; set; }
        public List<string> Log { get; set; } = new();
    }

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SimpleFlow_SingleStep_Executes()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("single-step")
            .Step("only-step", async (state, ct) =>
            {
                state.Message = "Hello";
                return true;
            })
            .Build();

        var state = new SimpleState { FlowId = "test-1" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Message.Should().Be("Hello");
    }

    [Fact]
    public async Task SimpleFlow_TwoSteps_ExecutesInOrder()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("two-steps")
            .Step("step-1", async (state, ct) =>
            {
                state.Log.Add("step-1");
                state.Counter = 1;
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                state.Log.Add("step-2");
                state.Counter = 2;
                return true;
            })
            .Build();

        var state = new SimpleState { FlowId = "test-2" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(2);
        result.State.Log.Should().Equal("step-1", "step-2");
    }

    [Fact]
    public async Task SimpleFlow_StepReturnsFalse_StopsExecution()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("stop-on-false")
            .Step("step-1", async (state, ct) =>
            {
                state.Log.Add("step-1");
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                state.Log.Add("step-2");
                return false; // Stop here
            })
            .Step("step-3", async (state, ct) =>
            {
                state.Log.Add("step-3");
                return true;
            })
            .Build();

        var state = new SimpleState { FlowId = "test-3" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.Log.Should().Equal("step-1", "step-2");
        result.State.Log.Should().NotContain("step-3");
    }

    [Fact]
    public async Task SimpleFlow_SimpleIf_ExecutesThenBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("simple-if")
            .If(s => s.Flag)
                .Then(f => f.Step("then-step", async (state, ct) =>
                {
                    state.Message = "Then executed";
                    return true;
                }))
                .Else(f => f.Step("else-step", async (state, ct) =>
                {
                    state.Message = "Else executed";
                    return true;
                }))
            .EndIf()
            .Build();

        // Test Then branch
        var thenState = new SimpleState { FlowId = "then-test", Flag = true };
        var thenResult = await executor.ExecuteAsync(flow, thenState);
        thenResult.State.Message.Should().Be("Then executed");

        // Test Else branch
        var elseState = new SimpleState { FlowId = "else-test", Flag = false };
        var elseResult = await executor.ExecuteAsync(flow, elseState);
        elseResult.State.Message.Should().Be("Else executed");
    }

    [Fact]
    public async Task SimpleFlow_CounterLoop_ExecutesNTimes()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("counter-loop")
            .While(s => s.Counter < 5)
                .Do(f => f.Step("increment", async (state, ct) =>
                {
                    state.Counter++;
                    state.Log.Add($"count-{state.Counter}");
                    return true;
                }))
            .EndWhile()
            .Build();

        var state = new SimpleState { FlowId = "loop-test", Counter = 0 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(5);
        result.State.Log.Should().HaveCount(5);
    }

    [Fact]
    public async Task SimpleFlow_SingleCompensation_Executes()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var compensated = false;

        var flow = FlowBuilder.Create<SimpleState>("single-compensation")
            .Step("step-1", async (state, ct) =>
            {
                state.Log.Add("step-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                compensated = true;
                state.Log.Add("compensate-1");
            })
            .Step("step-2-fail", async (state, ct) =>
            {
                state.Log.Add("step-2");
                throw new InvalidOperationException("Fail");
            })
            .Build();

        var state = new SimpleState { FlowId = "comp-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        compensated.Should().BeTrue();
    }

    [Fact]
    public async Task SimpleFlow_EmptyFlow_Succeeds()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("empty-flow").Build();
        var state = new SimpleState { FlowId = "empty-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SimpleFlow_StateModification_Persists()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SimpleState>("state-mod")
            .Step("modify-all", async (state, ct) =>
            {
                state.Counter = 42;
                state.Message = "Modified";
                state.Flag = true;
                state.Log.Add("item1");
                state.Log.Add("item2");
                return true;
            })
            .Build();

        var state = new SimpleState { FlowId = "state-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.State.Counter.Should().Be(42);
        result.State.Message.Should().Be("Modified");
        result.State.Flag.Should().BeTrue();
        result.State.Log.Should().HaveCount(2);
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
