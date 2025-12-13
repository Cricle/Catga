using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Loop flow scenario tests.
/// Tests While, ForEach, DoWhile, and Repeat loop patterns.
/// </summary>
public class LoopFlowTests
{
    #region Test State

    public class LoopState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Counter { get; set; }
        public int MaxIterations { get; set; } = 5;
        public List<int> IterationLog { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
        public bool BreakCondition { get; set; }
        public int Sum { get; set; }
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
    public async Task While_SimpleCounter_IteratesCorrectTimes()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoopState>("while-counter")
            .While(s => s.Counter < s.MaxIterations)
                .Do(f => f.Step("increment", async (state, ct) =>
                {
                    state.Counter++;
                    state.IterationLog.Add(state.Counter);
                    return true;
                }))
            .EndWhile()
            .Build();

        var state = new LoopState { FlowId = "while-test", MaxIterations = 5 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(5);
        result.State.IterationLog.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task While_WithBreakCondition_ExitsEarly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoopState>("while-break")
            .While(s => s.Counter < 100 && !s.BreakCondition)
                .Do(f => f.Step("check-break", async (state, ct) =>
                {
                    state.Counter++;
                    state.IterationLog.Add(state.Counter);

                    // Break at 5
                    if (state.Counter >= 5)
                    {
                        state.BreakCondition = true;
                    }
                    return true;
                }))
            .EndWhile()
            .Build();

        var state = new LoopState { FlowId = "break-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(5);
        result.State.BreakCondition.Should().BeTrue();
    }

    [Fact]
    public async Task While_ZeroIterations_SkipsLoop()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoopState>("while-zero")
            .Step("before", async (state, ct) =>
            {
                state.ProcessedItems.Add("before");
                return true;
            })
            .While(s => s.Counter < 0) // Never true
                .Do(f => f.Step("never-run", async (state, ct) =>
                {
                    state.ProcessedItems.Add("loop");
                    return true;
                }))
            .EndWhile()
            .Step("after", async (state, ct) =>
            {
                state.ProcessedItems.Add("after");
                return true;
            })
            .Build();

        var state = new LoopState { FlowId = "zero-test", Counter = 5 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().Equal("before", "after");
    }

    [Fact]
    public async Task ForEach_ProcessesList_AllItems()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ItemState>("foreach-list")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    state.ProcessedItems.Add($"{item}-done");
                    return true;
                }))
            .Build();

        var state = new ItemState
        {
            FlowId = "foreach-test",
            Items = new List<string> { "A", "B", "C", "D", "E" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(5);
        result.State.ProcessedItems.Should().Contain("A-done", "B-done", "C-done", "D-done", "E-done");
    }

    [Fact]
    public async Task ForEach_WithIndex_TracksPosition()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var indexLog = new List<int>();
        var currentIndex = 0;

        var flow = FlowBuilder.Create<ItemState>("foreach-index")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process", async (state, ct) =>
                {
                    indexLog.Add(currentIndex);
                    state.ProcessedItems.Add($"{currentIndex}:{item}");
                    currentIndex++;
                    return true;
                }))
            .Build();

        var state = new ItemState
        {
            FlowId = "index-test",
            Items = new List<string> { "X", "Y", "Z" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().Equal("0:X", "1:Y", "2:Z");
    }

    [Fact]
    public async Task ForEach_EmptyList_SkipsLoop()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ItemState>("foreach-empty")
            .Step("before", async (state, ct) =>
            {
                state.ProcessedItems.Add("before");
                return true;
            })
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .Step("after", async (state, ct) =>
            {
                state.ProcessedItems.Add("after");
                return true;
            })
            .Build();

        var state = new ItemState { FlowId = "empty-test", Items = new List<string>() };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().Equal("before", "after");
    }

    [Fact]
    public async Task NestedLoops_WhileInForEach_ExecutesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<NestedLoopState>("nested-loops")
            .ForEach(
                s => s.OuterItems,
                (outer, f) => f
                    .Step($"start-outer-{outer}", async (state, ct) =>
                    {
                        state.Log.Add($"outer-start:{outer}");
                        state.InnerCounter = 0;
                        return true;
                    })
                    .While(s => s.InnerCounter < 3)
                        .Do(inner => inner.Step("inner-loop", async (state, ct) =>
                        {
                            state.InnerCounter++;
                            state.Log.Add($"inner:{outer}-{state.InnerCounter}");
                            return true;
                        }))
                    .EndWhile()
                    .Step($"end-outer-{outer}", async (state, ct) =>
                    {
                        state.Log.Add($"outer-end:{outer}");
                        return true;
                    }))
            .Build();

        var state = new NestedLoopState
        {
            FlowId = "nested-test",
            OuterItems = new List<string> { "A", "B" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Log.Should().Contain("outer-start:A");
        result.State.Log.Should().Contain("inner:A-1");
        result.State.Log.Should().Contain("inner:A-2");
        result.State.Log.Should().Contain("inner:A-3");
        result.State.Log.Should().Contain("outer-end:A");
        result.State.Log.Should().Contain("outer-start:B");
    }

    [Fact]
    public async Task While_WithAccumulator_ComputesSum()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoopState>("while-accumulator")
            .While(s => s.Counter < 10)
                .Do(f => f.Step("accumulate", async (state, ct) =>
                {
                    state.Counter++;
                    state.Sum += state.Counter;
                    return true;
                }))
            .EndWhile()
            .Build();

        var state = new LoopState { FlowId = "sum-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Sum.Should().Be(55); // 1+2+3+4+5+6+7+8+9+10
    }

    [Fact]
    public async Task ForEach_WithCondition_FiltersItems()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<NumberState>("foreach-filter")
            .ForEach(
                s => s.Numbers,
                (num, f) => f
                    .If(s => num % 2 == 0)
                        .Then(inner => inner.Step($"even-{num}", async (state, ct) =>
                        {
                            state.EvenNumbers.Add(num);
                            return true;
                        }))
                        .Else(inner => inner.Step($"odd-{num}", async (state, ct) =>
                        {
                            state.OddNumbers.Add(num);
                            return true;
                        }))
                    .EndIf())
            .Build();

        var state = new NumberState
        {
            FlowId = "filter-test",
            Numbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.EvenNumbers.Should().Equal(2, 4, 6, 8, 10);
        result.State.OddNumbers.Should().Equal(1, 3, 5, 7, 9);
    }

    [Fact]
    public async Task While_MultipleStepsInBody_ExecutesAll()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoopState>("while-multi-step")
            .While(s => s.Counter < 3)
                .Do(f => f
                    .Step("step-a", async (state, ct) =>
                    {
                        state.ProcessedItems.Add($"a-{state.Counter}");
                        return true;
                    })
                    .Step("step-b", async (state, ct) =>
                    {
                        state.ProcessedItems.Add($"b-{state.Counter}");
                        return true;
                    })
                    .Step("increment", async (state, ct) =>
                    {
                        state.Counter++;
                        return true;
                    }))
            .EndWhile()
            .Build();

        var state = new LoopState { FlowId = "multi-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().Equal(
            "a-0", "b-0",
            "a-1", "b-1",
            "a-2", "b-2");
    }

    public class ItemState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
    }

    public class NestedLoopState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> OuterItems { get; set; } = new();
        public int InnerCounter { get; set; }
        public List<string> Log { get; set; } = new();
    }

    public class NumberState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<int> Numbers { get; set; } = new();
        public List<int> EvenNumbers { get; set; } = new();
        public List<int> OddNumbers { get; set; } = new();
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
