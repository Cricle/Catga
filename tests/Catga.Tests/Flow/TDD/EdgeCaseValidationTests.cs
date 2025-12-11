using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for edge cases, boundary conditions, and state validation
/// </summary>
public class EdgeCaseValidationTests
{
    [Fact]
    public async Task Flow_EmptyCollection_ShouldHandleGracefully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EmptyCollectionFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, EmptyCollectionFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "empty-collection",
            Items = [] // Empty collection
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("empty collection should not cause failure");
        state.ProcessedCount.Should().Be(0);
    }

    [Fact]
    public async Task Flow_NullCollection_ShouldHandleOrFail()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NullCollectionFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, NullCollectionFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "null-collection",
            Items = null! // Null collection
        };

        // Act
        var act = () => executor.RunAsync(state);

        // Assert - Should either handle gracefully or throw appropriate exception
        var result = await act();
        if (result != null)
        {
            result.IsSuccess.Should().BeTrue("null collection should be handled gracefully");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task Flow_VariousCollectionSizes_ShouldScale(int size)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ScalableCollectionFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, ScalableCollectionFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = $"scale-{size}",
            Items = Enumerable.Range(1, size).Select(i => $"Item{i}").ToList()
        };

        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                state.CurrentDepth++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok"));
            });

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        sw.Stop();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ProcessedCount.Should().Be(size);

        // Performance should be reasonable
        var timePerItem = sw.Elapsed.TotalMilliseconds / size;
        timePerItem.Should().BeLessThan(10, "processing should be efficient");
    }

    [Fact]
    public async Task Flow_CircularReference_ShouldDetectAndPrevent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new CircularReferenceFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, CircularReferenceFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "circular-ref",
            MaxDepth = 100,
            CurrentDepth = 0
        };

        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.CurrentDepth.Should().BeLessThanOrEqualTo(state.MaxDepth, "should prevent infinite recursion");
    }

    [Fact]
    public async Task Flow_LargeStateObject_ShouldHandleEfficiently()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new LargeStateFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, LargeStateFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "large-state",
            LargeData = new string('x', 1_000_000), // 1MB string
            Items = Enumerable.Range(1, 100).Select(i => $"Item{i}").ToList()
        };

        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));

        // Act
        var memoryBefore = GC.GetTotalMemory(true);
        var result = await executor.RunAsync(state);
        var memoryAfter = GC.GetTotalMemory(false);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();

        var memoryIncrease = memoryAfter - memoryBefore;
        memoryIncrease.Should().BeLessThan(10_000_000, "should not leak excessive memory");
    }

    [Fact]
    public async Task Flow_SpecialCharactersInData_ShouldHandleCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SpecialCharactersFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, SpecialCharactersFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "special-chars",
            Items =
            [
                "Normal",
                "With Space",
                "With\tTab",
                "With\nNewline",
                "With\"Quote",
                "With'Apostrophe",
                "With\\Backslash",
                "With/Slash",
                "With<>Brackets",
                "With&Ampersand",
                "😀Emoji",
                "中文",
                "Русский",
                "العربية"
            ]
        };

        var processedItems = new List<string>();
        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ItemCommand>();
                processedItems.Add(cmd.Item);
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(cmd.Item));
            });

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        processedItems.Should().BeEquivalentTo(state.Items, "all special characters should be processed correctly");
    }

    [Fact]
    public async Task Flow_ConcurrentStateModification_ShouldBeThreadSafe()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ConcurrentModificationFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, ConcurrentModificationFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "concurrent-mod",
            Items = Enumerable.Range(1, 100).Select(i => $"Item{i}").ToList(),
            ConcurrentCounter = 0
        };

        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Simulate concurrent modification
                Interlocked.Increment(ref state.ConcurrentCounter);
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok"));
            });

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ConcurrentCounter.Should().Be(100, "all concurrent modifications should be accounted for");
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task Flow_BoundaryValues_ShouldHandleCorrectly(int value)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new BoundaryValueFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, BoundaryValueFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = $"boundary-{value}",
            NumericValue = value
        };

        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                state.ProcessedValues.Add($"Value: {value}");
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{value}"));
            });

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ProcessedValues.Should().Contain($"Value: {value}");
    }

    [Fact]
    public async Task Flow_DeepNesting_ShouldHandleWithoutStackOverflow()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DeepNestingFlow();
        var executor = new DslFlowExecutor<EdgeCaseState, DeepNestingFlow>(mediator, store, config);

        var state = new EdgeCaseState
        {
            FlowId = "deep-nesting",
            NestingLevel = 0,
            MaxNesting = 50
        };

        mediator.SendAsync<ItemCommand, string>(Arg.Any<ItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ProcessedValues.Should().HaveCount(state.MaxNesting, "all nesting levels should be processed");
    }
}

// Test State
public class EdgeCaseState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string>? Items { get; set; }
    public int ProcessedCount { get; set; }
    public List<string> ProcessedValues { get; set; } = [];
    public int NumericValue { get; set; }
    public string? LargeData { get; set; }
    public int MaxDepth { get; set; }
    public int CurrentDepth { get; set; }
    public int ConcurrentCounter;
    public int NestingLevel { get; set; }
    public int MaxNesting { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Test Command
public record ItemCommand(string Item) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// Test Flow Configurations
public class EmptyCollectionFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("empty-collection-flow");

        flow.ForEach(s => s.Items ?? [])
            .Configure((item, f) =>
            {
                f.Send(s => new ItemCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedCount++)
            .EndForEach();
    }
}

public class NullCollectionFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("null-collection-flow");

        flow.ForEach(s => s.Items ?? [])
            .Configure((item, f) =>
            {
                f.Send(s => new ItemCommand(item));
            })
            .EndForEach();
    }
}

public class ScalableCollectionFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("scalable-collection-flow");

        flow.ForEach(s => s.Items ?? [])
            .WithParallelism(Environment.ProcessorCount)
            .WithBatchSize(100)
            .Configure((item, f) =>
            {
                f.Send(s => new ItemCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedCount++)
            .EndForEach();
    }
}

public class CircularReferenceFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("circular-reference-flow");

        flow.If(s => s.CurrentDepth < s.MaxDepth)
            .Send(s => new ItemCommand($"Depth-{s.CurrentDepth}"))
            .If(s => s.CurrentDepth < s.MaxDepth) // Nested condition
                .Send(s => new ItemCommand($"Nested-{s.CurrentDepth}"))
            .EndIf()
        .EndIf();
    }
}

public class LargeStateFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("large-state-flow");

        flow.ForEach(s => s.Items ?? [])
            .Configure((item, f) =>
            {
                f.Send(s => new ItemCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedCount++)
            .EndForEach();
    }
}

public class SpecialCharactersFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("special-characters-flow");

        flow.ForEach(s => s.Items ?? [])
            .Configure((item, f) =>
            {
                f.Send(s => new ItemCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedValues.Add(item))
            .EndForEach();
    }
}

public class ConcurrentModificationFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("concurrent-modification-flow");

        flow.ForEach(s => s.Items ?? [])
            .WithParallelism(10)
            .Configure((item, f) =>
            {
                f.Send(s => new ItemCommand(item));
            })
            .EndForEach();
    }
}

public class BoundaryValueFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("boundary-value-flow");

        flow.Switch(s => s.NumericValue)
            .Case(int.MinValue, f => f.Send(s => new ItemCommand("MinValue")))
            .Case(-1, f => f.Send(s => new ItemCommand("NegativeOne")))
            .Case(0, f => f.Send(s => new ItemCommand("Zero")))
            .Case(1, f => f.Send(s => new ItemCommand("One")))
            .Case(int.MaxValue, f => f.Send(s => new ItemCommand("MaxValue")))
            .Default(f => f.Send(s => new ItemCommand($"Other-{s.NumericValue}")))
            .EndSwitch();
    }
}

public class DeepNestingFlow : FlowConfig<EdgeCaseState>
{
    protected override void Configure(IFlowBuilder<EdgeCaseState> flow)
    {
        flow.Name("deep-nesting-flow");

        var builder = flow.If(s => s.NestingLevel < s.MaxNesting);

        for (int i = 0; i < 50; i++)
        {
            var level = i;
            builder = builder
                .Send(s => new ItemCommand($"Level-{level}"))
                .If(s => s.NestingLevel < s.MaxNesting);
        }

        // Close all nested Ifs
        for (int i = 0; i < 50; i++)
        {
            builder.EndIf();
        }
    }
}
