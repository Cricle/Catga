using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Edge case and boundary condition tests for Flow DSL.
/// Tests unusual scenarios, limits, and error conditions.
/// </summary>
public class EdgeCaseFlowTests
{
    #region Test State

    public class EdgeCaseState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Counter { get; set; }
        public string? NullableValue { get; set; }
        public List<string> Log { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
        public bool Flag { get; set; }
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
    public async Task EdgeCase_EmptyForEach_SkipsGracefully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EmptyCollectionState>("empty-foreach")
            .Step("before", async (state, ct) =>
            {
                state.Log.Add("before");
                return true;
            })
            .ForEach(
                s => s.Items, // Empty list
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    state.Log.Add($"processed-{item}");
                    return true;
                }))
            .Step("after", async (state, ct) =>
            {
                state.Log.Add("after");
                return true;
            })
            .Build();

        var state = new EmptyCollectionState { FlowId = "empty-test", Items = new List<string>() };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Equal("before", "after");
    }

    [Fact]
    public async Task EdgeCase_NullConditionValue_HandlesGracefully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("null-condition")
            .If(s => s.NullableValue != null)
                .Then(f => f.Step("not-null", async (state, ct) =>
                {
                    state.Log.Add("not-null");
                    return true;
                }))
                .Else(f => f.Step("is-null", async (state, ct) =>
                {
                    state.Log.Add("is-null");
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new EdgeCaseState { FlowId = "null-test", NullableValue = null };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Contain("is-null");
    }

    [Fact]
    public async Task EdgeCase_ZeroIterationWhile_SkipsLoop()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("zero-while")
            .Step("before", async (state, ct) =>
            {
                state.Log.Add("before");
                return true;
            })
            .While(s => s.Counter > 0) // Counter is 0, so condition is false immediately
                .Do(f => f.Step("loop-body", async (state, ct) =>
                {
                    state.Log.Add("loop");
                    state.Counter--;
                    return true;
                }))
            .EndWhile()
            .Step("after", async (state, ct) =>
            {
                state.Log.Add("after");
                return true;
            })
            .Build();

        var state = new EdgeCaseState { FlowId = "zero-while-test", Counter = 0 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Equal("before", "after");
        result.State.Log.Should().NotContain("loop");
    }

    [Fact]
    public async Task EdgeCase_DeeplyNestedBranches_ExecutesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("deep-nesting")
            .If(s => true) // Level 1
                .Then(f => f
                    .Step("level-1", async (state, ct) => { state.Log.Add("L1"); return true; })
                    .If(s => true) // Level 2
                        .Then(inner => inner
                            .Step("level-2", async (state, ct) => { state.Log.Add("L2"); return true; })
                            .If(s => true) // Level 3
                                .Then(inner2 => inner2
                                    .Step("level-3", async (state, ct) => { state.Log.Add("L3"); return true; })
                                    .If(s => true) // Level 4
                                        .Then(inner3 => inner3.Step("level-4", async (state, ct) =>
                                        {
                                            state.Log.Add("L4");
                                            return true;
                                        }))
                                    .EndIf())
                            .EndIf())
                    .EndIf())
            .EndIf()
            .Build();

        var state = new EdgeCaseState { FlowId = "deep-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Equal("L1", "L2", "L3", "L4");
    }

    [Fact]
    public async Task EdgeCase_SwitchWithNoMatchingCase_ExecutesDefault()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("switch-no-match")
            .Switch(s => s.Counter)
                .Case(1, f => f.Step("case-1", async (state, ct) => { state.Log.Add("case-1"); return true; }))
                .Case(2, f => f.Step("case-2", async (state, ct) => { state.Log.Add("case-2"); return true; }))
                .Case(3, f => f.Step("case-3", async (state, ct) => { state.Log.Add("case-3"); return true; }))
                .Default(f => f.Step("default", async (state, ct) => { state.Log.Add("default"); return true; }))
            .EndSwitch()
            .Build();

        var state = new EdgeCaseState { FlowId = "switch-test", Counter = 999 }; // No matching case

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Contain("default");
    }

    [Fact]
    public async Task EdgeCase_LargeNumberOfSteps_ExecutesAll()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var builder = FlowBuilder.Create<EdgeCaseState>("many-steps");
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            builder = builder.Step($"step-{i}", async (state, ct) =>
            {
                state.Counter++;
                return true;
            });
        }
        var flow = builder.Build();

        var state = new EdgeCaseState { FlowId = "many-steps-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(100);
    }

    [Fact]
    public async Task EdgeCase_RapidStateChanges_MaintainsConsistency()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("rapid-changes")
            .Step("rapid-1", async (state, ct) =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    state.Counter++;
                }
                return true;
            })
            .Step("rapid-2", async (state, ct) =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    state.Counter--;
                }
                return true;
            })
            .Step("rapid-3", async (state, ct) =>
            {
                state.Counter = 42;
                return true;
            })
            .Build();

        var state = new EdgeCaseState { FlowId = "rapid-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(42);
    }

    [Fact]
    public async Task EdgeCase_StepThrowsSpecificException_PropagatesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("exception-test")
            .Step("throw-specific", async (state, ct) =>
            {
                throw new ArgumentException("Specific exception message");
            })
            .Build();

        var state = new EdgeCaseState { FlowId = "exception-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task EdgeCase_AllElseIfConditionsFalse_ExecutesElse()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("elseif-chain")
            .If(s => s.Counter == 1)
                .Then(f => f.Step("if-1", async (state, ct) => { state.Log.Add("if-1"); return true; }))
            .ElseIf(s => s.Counter == 2)
                .Then(f => f.Step("if-2", async (state, ct) => { state.Log.Add("if-2"); return true; }))
            .ElseIf(s => s.Counter == 3)
                .Then(f => f.Step("if-3", async (state, ct) => { state.Log.Add("if-3"); return true; }))
            .ElseIf(s => s.Counter == 4)
                .Then(f => f.Step("if-4", async (state, ct) => { state.Log.Add("if-4"); return true; }))
            .Else(f => f.Step("else", async (state, ct) => { state.Log.Add("else"); return true; }))
            .EndIf()
            .Build();

        var state = new EdgeCaseState { FlowId = "elseif-test", Counter = 100 }; // None match

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Contain("else");
        result.State.Log.Should().HaveCount(1);
    }

    [Fact]
    public async Task EdgeCase_CompensationThrows_HandlesGracefully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("comp-throws")
            .Step("step-1", async (state, ct) =>
            {
                state.Log.Add("step-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.Log.Add("comp-1-throws");
                throw new InvalidOperationException("Compensation failed");
            })
            .Step("step-2-fail", async (state, ct) =>
            {
                throw new InvalidOperationException("Step failed");
            })
            .Build();

        var state = new EdgeCaseState { FlowId = "comp-throw-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        // Compensation was attempted
        result.State.Log.Should().Contain("comp-1-throws");
    }

    [Fact]
    public async Task EdgeCase_SameStepName_ExecutesBoth()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EdgeCaseState>("duplicate-names")
            .Step("duplicate", async (state, ct) =>
            {
                state.Counter++;
                state.Log.Add("first");
                return true;
            })
            .Step("duplicate", async (state, ct) =>
            {
                state.Counter++;
                state.Log.Add("second");
                return true;
            })
            .Build();

        var state = new EdgeCaseState { FlowId = "dup-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(2);
        result.State.Log.Should().HaveCount(2);
    }

    [Fact]
    public async Task EdgeCase_ConcurrentStateAccess_InForEach_IsThreadSafe()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ConcurrentState>("concurrent-foreach")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    Interlocked.Increment(ref state._processedCount);
                    lock (state.ProcessedItems)
                    {
                        state.ProcessedItems.Add(item);
                    }
                    return true;
                }))
            .WithParallelism(5)
            .Build();

        var state = new ConcurrentState
        {
            FlowId = "concurrent-test",
            Items = Enumerable.Range(1, 20).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(20);
        result.State.ProcessedItems.Should().HaveCount(20);
    }

    public class EmptyCollectionState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> Log { get; set; } = new();
    }

    public class ConcurrentState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
        internal int _processedCount;
        public int ProcessedCount => _processedCount;
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
