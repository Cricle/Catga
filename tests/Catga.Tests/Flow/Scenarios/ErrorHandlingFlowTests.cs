using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Error handling flow scenario tests.
/// Tests exception handling, error recovery, and failure strategies.
/// </summary>
public class ErrorHandlingFlowTests
{
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
    public async Task Error_StepThrowsException_FlowFails()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ErrorState>("error-flow")
            .Step("step-1", async (state, ct) =>
            {
                state.Steps.Add("step-1");
                return true;
            })
            .Step("step-2-error", async (state, ct) =>
            {
                state.Steps.Add("step-2");
                throw new InvalidOperationException("Simulated error");
            })
            .Step("step-3", async (state, ct) =>
            {
                state.Steps.Add("step-3");
                return true;
            })
            .Build();

        var state = new ErrorState { FlowId = "error-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.State.Steps.Should().Contain("step-1");
        result.State.Steps.Should().Contain("step-2");
        result.State.Steps.Should().NotContain("step-3");
    }

    [Fact]
    public async Task Error_StepReturnsFalse_FlowStops()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ErrorState>("false-return")
            .Step("step-1", async (state, ct) =>
            {
                state.Steps.Add("step-1");
                return true;
            })
            .Step("step-2-false", async (state, ct) =>
            {
                state.Steps.Add("step-2");
                return false; // Intentionally return false
            })
            .Step("step-3", async (state, ct) =>
            {
                state.Steps.Add("step-3");
                return true;
            })
            .Build();

        var state = new ErrorState { FlowId = "false-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.Steps.Should().NotContain("step-3");
    }

    [Fact]
    public async Task Error_SpecificExceptionType_CapturedCorrectly()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ErrorState>("specific-exception")
            .Step("throw-argument", async (state, ct) =>
            {
                throw new ArgumentNullException("paramName", "Parameter cannot be null");
            })
            .Build();

        var state = new ErrorState { FlowId = "specific-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ArgumentNullException>();
    }

    [Fact]
    public async Task Error_InNestedBranch_BubblesUp()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ErrorState>("nested-error")
            .Step("before", async (state, ct) =>
            {
                state.Steps.Add("before");
                return true;
            })
            .If(s => true)
                .Then(f => f
                    .Step("nested-1", async (state, ct) =>
                    {
                        state.Steps.Add("nested-1");
                        return true;
                    })
                    .If(s => true)
                        .Then(inner => inner.Step("deep-error", async (state, ct) =>
                        {
                            state.Steps.Add("deep-error");
                            throw new InvalidOperationException("Deep nested error");
                        }))
                    .EndIf())
            .EndIf()
            .Step("after", async (state, ct) =>
            {
                state.Steps.Add("after");
                return true;
            })
            .Build();

        var state = new ErrorState { FlowId = "nested-error-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.Steps.Should().Contain("deep-error");
        result.State.Steps.Should().NotContain("after");
    }

    [Fact]
    public async Task Error_InLoop_StopsIteration()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var iterationCount = 0;

        var flow = FlowBuilder.Create<ErrorState>("loop-error")
            .While(s => s.Counter < 10)
                .Do(f => f.Step("iterate", async (state, ct) =>
                {
                    iterationCount++;
                    state.Counter++;
                    state.Steps.Add($"iter-{state.Counter}");

                    if (state.Counter == 5)
                    {
                        throw new InvalidOperationException("Error at iteration 5");
                    }
                    return true;
                }))
            .EndWhile()
            .Build();

        var state = new ErrorState { FlowId = "loop-error-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.Counter.Should().Be(5);
        iterationCount.Should().Be(5);
    }

    [Fact]
    public async Task Error_InForEach_StopsProcessing()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ItemErrorState>("foreach-error")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    state.ProcessedItems.Add(item);

                    if (item == "C")
                    {
                        throw new InvalidOperationException($"Error processing {item}");
                    }
                    return true;
                }))
            .StopOnFirstFailure()
            .Build();

        var state = new ItemErrorState
        {
            FlowId = "foreach-error-test",
            Items = new List<string> { "A", "B", "C", "D", "E" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.ProcessedItems.Should().Contain("A");
        result.State.ProcessedItems.Should().Contain("B");
        result.State.ProcessedItems.Should().Contain("C");
        result.State.ProcessedItems.Should().NotContain("D");
        result.State.ProcessedItems.Should().NotContain("E");
    }

    [Fact]
    public async Task Error_WithContinueOnFailure_ProcessesAll()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ItemErrorState>("continue-on-error")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    if (item == "C")
                    {
                        state.Errors.Add($"Error at {item}");
                        return true; // Continue despite error
                    }
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .ContinueOnFailure()
            .Build();

        var state = new ItemErrorState
        {
            FlowId = "continue-test",
            Items = new List<string> { "A", "B", "C", "D", "E" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(4); // A, B, D, E
        result.State.Errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task Error_MultipleExceptions_FirstOneReported()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ErrorState>("multi-error")
            .Step("first-error", async (state, ct) =>
            {
                throw new ArgumentException("First error");
            })
            .Step("second-error", async (state, ct) =>
            {
                throw new InvalidOperationException("Second error");
            })
            .Build();

        var state = new ErrorState { FlowId = "multi-error-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task Error_WithErrorState_CapturesDetails()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DetailedErrorState>("detailed-error")
            .Step("operation", async (state, ct) =>
            {
                state.OperationStarted = DateTime.UtcNow;
                state.CurrentOperation = "DataProcessing";

                throw new InvalidOperationException("Processing failed");
            })
            .Build();

        var state = new DetailedErrorState { FlowId = "detailed-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.CurrentOperation.Should().Be("DataProcessing");
        result.State.OperationStarted.Should().NotBeNull();
    }

    [Fact]
    public async Task Error_AggregateException_HandledCorrectly()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ErrorState>("aggregate-error")
            .Step("throw-aggregate", async (state, ct) =>
            {
                var exceptions = new List<Exception>
                {
                    new ArgumentException("Error 1"),
                    new InvalidOperationException("Error 2"),
                    new NullReferenceException("Error 3")
                };
                throw new AggregateException("Multiple errors", exceptions);
            })
            .Build();

        var state = new ErrorState { FlowId = "aggregate-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<AggregateException>();
    }

    public class ErrorState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Counter { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public class ItemErrorState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class DetailedErrorState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? CurrentOperation { get; set; }
        public DateTime? OperationStarted { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
