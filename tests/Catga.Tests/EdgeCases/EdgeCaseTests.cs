using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.EdgeCases;

/// <summary>
/// Tests for edge cases, boundary conditions, and unusual scenarios
/// </summary>
public class EdgeCaseTests
{
    private readonly ITestOutputHelper _output;

    public EdgeCaseTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Null and Empty Tests

    [Fact]
    public async Task EdgeCase_NullFlowId_HandledGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SimpleFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SimpleFlow>>();

        var state = new EdgeCaseState { FlowId = null };

        // Act & Assert
        var result = await executor!.RunAsync(state);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("FlowId");
    }

    [Fact]
    public async Task EdgeCase_EmptyCollectionInForEach_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, ForEachFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, ForEachFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = "empty-foreach",
            Items = new List<string>() // Empty collection
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(0);
        result.State.ForEachCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task EdgeCase_NullCollectionInForEach_HandledGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, ForEachFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, ForEachFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = "null-foreach",
            Items = null! // Null collection
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("null");
    }

    #endregion

    #region Extreme Values Tests

    [Fact]
    public async Task EdgeCase_VeryLargeCollection_HandledEfficiently()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, ForEachFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, ForEachFlow>>();

        const int itemCount = 100_000;
        var state = new EdgeCaseState
        {
            FlowId = "large-collection",
            Items = Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToList()
        };

        // Act
        var startMemory = GC.GetTotalMemory(false);
        var result = await executor!.RunAsync(state);
        var endMemory = GC.GetTotalMemory(false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(itemCount);

        var memoryUsed = endMemory - startMemory;
        var memoryPerItem = memoryUsed / itemCount;
        _output.WriteLine($"Memory per item: {memoryPerItem} bytes");
        memoryPerItem.Should().BeLessThan(1000, "should use less than 1KB per item");
    }

    [Fact]
    public async Task EdgeCase_DeeplyNestedBranching_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, DeeplyNestedFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, DeeplyNestedFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = "deep-nesting",
            Level1 = true,
            Level2 = true,
            Level3 = true,
            Level4 = true,
            Level5 = true
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.MaxDepthReached.Should().Be(5);
    }

    [Fact]
    public async Task EdgeCase_MaxIntegerValues_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SimpleFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SimpleFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = "max-values",
            IntValue = int.MaxValue,
            LongValue = long.MaxValue,
            DoubleValue = double.MaxValue
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IntValue.Should().Be(int.MaxValue);
        result.State.LongValue.Should().Be(long.MaxValue);
        result.State.DoubleValue.Should().Be(double.MaxValue);
    }

    #endregion

    #region Unicode and Special Characters Tests

    [Fact]
    public async Task EdgeCase_UnicodeInFlowId_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SimpleFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SimpleFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = "测试-テスト-🚀-❤️-Тест",
            StringValue = "Unicode: 中文 日本語 한국어 العربية עברית"
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.FlowId.Should().Be("测试-テスト-🚀-❤️-Тест");
        result.State.StringValue.Should().Contain("中文");
    }

    [Fact]
    public async Task EdgeCase_SpecialCharactersInData_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SimpleFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SimpleFlow>>();
        var store = provider.GetService<IDslFlowStore>();

        var specialChars = @"!@#$%^&*()_+-=[]{}|;':"",./<>?\`~" + "\r\n\t";
        var state = new EdgeCaseState
        {
            FlowId = "special-chars",
            StringValue = specialChars
        };

        // Act - Test persistence
        var snapshot = new FlowSnapshot<EdgeCaseState>
        {
            FlowId = state.FlowId,
            State = state,
            Status = DslFlowStatus.Running,
            Position = new FlowPosition(new[] { 0 }),
            Version = 1
        };

        await store!.CreateAsync(snapshot);
        var retrieved = await store.GetAsync<EdgeCaseState>(state.FlowId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.State.StringValue.Should().Be(specialChars);
    }

    #endregion

    #region Race Conditions and Threading Tests

    [Fact]
    public async Task EdgeCase_ConcurrentStateModification_HandledSafely()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, ConcurrentModificationFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, ConcurrentModificationFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = "concurrent-mod",
            Counter = 0
        };

        // Act - Run multiple flows concurrently modifying shared state
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            var localState = new EdgeCaseState
            {
                FlowId = $"concurrent-{i}",
                Counter = 0
            };
            await executor!.RunAsync(localState);
            return localState.Counter;
        }));

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(counter => counter.Should().Be(100));
    }

    [Fact]
    public async Task EdgeCase_RapidFlowCreationAndDeletion_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IDslFlowStore>();

        const int iterations = 1000;
        var errors = 0;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var flowId = $"rapid-{i}";
            var snapshot = new FlowSnapshot<EdgeCaseState>
            {
                FlowId = flowId,
                State = new EdgeCaseState { FlowId = flowId },
                Status = DslFlowStatus.Running,
                Position = new FlowPosition(new[] { 0 }),
                Version = 1
            };

            var createResult = await store!.CreateAsync(snapshot);
            if (!createResult.IsSuccess) errors++;

            var deleteResult = await store.DeleteAsync(flowId);
            if (!deleteResult) errors++;
        }

        // Assert
        errors.Should().Be(0);
    }

    #endregion

    #region Timeout and Cancellation Tests

    [Fact]
    public async Task EdgeCase_ImmediateTimeout_HandledGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IDslFlowStore>();

        var flowId = "immediate-timeout";

        // Set wait condition with already expired timeout
        await store!.SetWaitConditionAsync(
            flowId,
            WaitConditionType.WhenAll,
            new[] { "signal1", "signal2" },
            DateTime.UtcNow.AddSeconds(-1)); // Already expired

        // Act
        var timedOut = await store.GetTimedOutWaitConditionsAsync();

        // Assert
        timedOut.Should().Contain(c => c.FlowId == flowId);
    }

    [Fact]
    public async Task EdgeCase_CancellationDuringExecution_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SlowFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SlowFlow>>();

        var state = new EdgeCaseState { FlowId = "cancellation-test" };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        Func<Task> act = async () =>
        {
            await executor!.RunAsync(state, cts.Token);
        };

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Boundary Conditions Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task EdgeCase_BoundaryIntegerValues_HandledCorrectly(int value)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SimpleFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SimpleFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = $"boundary-{value}",
            IntValue = value
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IntValue.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("A very long string that exceeds normal expectations and contains many characters to test the system's ability to handle large strings without issues or performance degradation")]
    public async Task EdgeCase_BoundaryStringValues_HandledCorrectly(string value)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<EdgeCaseState, SimpleFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<EdgeCaseState, SimpleFlow>>();

        var state = new EdgeCaseState
        {
            FlowId = $"string-boundary-{value.GetHashCode()}",
            StringValue = value
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.StringValue.Should().Be(value);
    }

    #endregion

    #region Recursive and Circular Reference Tests

    [Fact]
    public async Task EdgeCase_CircularReference_HandledWithoutStackOverflow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<CircularState, CircularFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CircularState, CircularFlow>>();

        var state = new CircularState
        {
            FlowId = "circular",
            MaxIterations = 1000
        };
        state.Self = state; // Circular reference

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IterationCount.Should().Be(1000);
    }

    #endregion
}

// Test flows
public class SimpleFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow;
    }
}

public class ForEachFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.ForEach(s => s.Items ?? new List<string>())
            .Configure((item, f) => f)
            .EndForEach();

        flow;
    }
}

public class DeeplyNestedFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.If(s => s.Level1)

            .If(s => s.Level2)

                .If(s => s.Level3)

                    .If(s => s.Level4)

                        .If(s => s.Level5)

                        .EndIf()
                    .EndIf()
                .EndIf()
            .EndIf()
        .EndIf();
    }
}

public class ConcurrentModificationFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        // Simulate concurrent modifications
        flow.WhenAll(
            Enumerable.Range(0, 100).Select(i =>
                (Action<IFlowBuilder<EdgeCaseState>>)(f =>
                {
                    // Empty flow builder for stress test
                }))
                .ToArray()
        );
    }
}

public class SlowFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Step("slow-step", async s =>
        {
            await Task.Delay(1000);
            s.Processed = true;
        });
    }
}

public class CircularFlow : FlowConfig<CircularState>
{
    protected override void Configure(IFlowBuilder<CircularState> flow)
    {
        flow.Step("iterate", s =>
        {
            while (s.IterationCount < s.MaxIterations)
            {
                s.IterationCount++;
            }
        });
    }
}

// Test states
public class EdgeCaseState : IFlowState
{
    public string? FlowId { get; set; }
    public bool Processed { get; set; }
    public List<string>? Items { get; set; }
    public int ProcessedCount { get; set; }
    public bool ForEachCompleted { get; set; }
    public int IntValue { get; set; }
    public long LongValue { get; set; }
    public double DoubleValue { get; set; }
    public string StringValue { get; set; } = string.Empty;
    public int Counter { get; set; }
    public bool Level1 { get; set; }
    public bool Level2 { get; set; }
    public bool Level3 { get; set; }
    public bool Level4 { get; set; }
    public bool Level5 { get; set; }
    public int MaxDepthReached { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class CircularState : IFlowState
{
    public string? FlowId { get; set; }
    public CircularState? Self { get; set; }
    public int IterationCount { get; set; }
    public int MaxIterations { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
