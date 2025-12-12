using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

/// <summary>
/// Advanced Flow DSL feature tests covering ForEach, If/Else, and error handling.
/// </summary>
public class FlowDslAdvancedFeaturesTests
{
    private readonly ITestOutputHelper _output;

    public FlowDslAdvancedFeaturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ForEach_WithParallelism_ProcessesItemsInParallel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<ParallelProcessingFlow>();
        services.AddTransient<DslFlowExecutor<ParallelProcessingState, ParallelProcessingFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<ParallelProcessingState, ParallelProcessingFlow>>();

        var state = new ParallelProcessingState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(1, 10).ToList()
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await executor.RunAsync(state);

        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.ProcessedItems.Should().Be(10);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "parallel processing should be faster");

        _output.WriteLine($"✓ ForEach with parallelism processed 10 items in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ForEach_WithOnComplete_ProcessesAllItems()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<ErrorHandlingFlow>();
        services.AddTransient<DslFlowExecutor<ErrorHandlingState, ErrorHandlingFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<ErrorHandlingState, ErrorHandlingFlow>>();

        var state = new ErrorHandlingState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = new[] { "valid1", "invalid", "valid2", "invalid", "valid3" }.ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue("flow should complete successfully");
        state.SuccessfulItems.Count.Should().Be(3);
        state.FailedItems.Count.Should().Be(2);

        _output.WriteLine($"✓ ForEach with OnComplete: {state.SuccessfulItems.Count} valid, {state.FailedItems.Count} invalid");
    }

    [Fact]
    public async Task IfElse_ConditionalBranching_ExecutesCorrectBranch()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<ConditionalBranchingFlow>();
        services.AddTransient<DslFlowExecutor<ConditionalState, ConditionalBranchingFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<ConditionalState, ConditionalBranchingFlow>>();

        // Test Case 1: High value (If branch)
        var highValueState = new ConditionalState
        {
            FlowId = $"high-{Guid.NewGuid()}",
            Value = 100
        };

        var highResult = await executor.RunAsync(highValueState);
        highResult.IsSuccess.Should().BeTrue();
        highValueState.ExecutedBranch.Should().Be("HighValue");

        // Test Case 2: Medium value (ElseIf branch)
        var mediumValueState = new ConditionalState
        {
            FlowId = $"medium-{Guid.NewGuid()}",
            Value = 50
        };

        var mediumResult = await executor.RunAsync(mediumValueState);
        mediumResult.IsSuccess.Should().BeTrue();
        mediumValueState.ExecutedBranch.Should().Be("MediumValue");

        // Test Case 3: Low value (Else branch)
        var lowValueState = new ConditionalState
        {
            FlowId = $"low-{Guid.NewGuid()}",
            Value = 10
        };

        var lowResult = await executor.RunAsync(lowValueState);
        lowResult.IsSuccess.Should().BeTrue();
        lowValueState.ExecutedBranch.Should().Be("LowValue");

        _output.WriteLine("✓ If/ElseIf/Else conditional branching works correctly");
    }

    [Fact]
    public async Task ForEach_WithOnComplete_AggregatesResults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<AggregationFlow>();
        services.AddTransient<DslFlowExecutor<AggregationState, AggregationFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<AggregationState, AggregationFlow>>();

        var state = new AggregationState
        {
            FlowId = Guid.NewGuid().ToString(),
            Values = new[] { 10, 20, 30, 40, 50 }.ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.Sum.Should().Be(150);
        state.Count.Should().Be(5);
        state.Average.Should().Be(30);

        _output.WriteLine($"✓ ForEach with OnComplete aggregation: Sum={state.Sum}, Count={state.Count}, Average={state.Average}");
    }

    [Fact]
    public async Task NestedForEach_ProcessesNestedCollections()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<NestedForEachFlow>();
        services.AddTransient<DslFlowExecutor<NestedForEachState, NestedForEachFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<NestedForEachState, NestedForEachFlow>>();

        var state = new NestedForEachState
        {
            FlowId = Guid.NewGuid().ToString(),
            Groups = new List<ItemGroup>
            {
                new() { Name = "Group1", Items = new[] { "A", "B", "C" }.ToList() },
                new() { Name = "Group2", Items = new[] { "D", "E" }.ToList() },
                new() { Name = "Group3", Items = new[] { "F", "G", "H", "I" }.ToList() }
            }
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.TotalProcessed.Should().Be(9); // 3 + 2 + 4

        _output.WriteLine($"✓ Nested ForEach processed {state.TotalProcessed} items across {state.Groups.Count} groups");
    }

    [Fact]
    public async Task ComplexFlow_CombinesMultipleFeatures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<ComplexFlow>();
        services.AddTransient<DslFlowExecutor<ComplexState, ComplexFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<ComplexState, ComplexFlow>>();

        var state = new ComplexState
        {
            FlowId = Guid.NewGuid().ToString(),
            Orders = new List<Order>
            {
                new() { Id = "O1", Amount = 100, Priority = "High" },
                new() { Id = "O2", Amount = 50, Priority = "Low" },
                new() { Id = "O3", Amount = 200, Priority = "High" }
            }
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.HighPriorityCount.Should().Be(2);
        state.TotalAmount.Should().Be(350);

        _output.WriteLine($"✓ Complex flow: {state.HighPriorityCount} high-priority orders, total amount: {state.TotalAmount}");
    }
}

// Flow Configurations

public class ParallelProcessingFlow : FlowConfig<ParallelProcessingState>
{
    protected override void Configure(IFlowBuilder<ParallelProcessingState> flow)
    {
        flow.Name("parallel-processing");

        flow.ForEach(s => s.Items)
            .WithParallelism(5)
            .Configure((item, f) =>
            {
                // Simulate processing
            })
            .OnComplete(s =>
            {
                s.ProcessedItems = s.Items.Count;
            })
            .EndForEach();
    }
}

public class ErrorHandlingFlow : FlowConfig<ErrorHandlingState>
{
    protected override void Configure(IFlowBuilder<ErrorHandlingState> flow)
    {
        flow.Name("error-handling");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                // Process items - separate valid from invalid
            })
            .OnComplete(s =>
            {
                // Separate items into successful and failed
                s.SuccessfulItems = s.Items.Where(i => !i.Contains("invalid")).ToList();
                s.FailedItems = s.Items.Where(i => i.Contains("invalid")).ToList();
            })
            .ContinueOnFailure()
            .EndForEach();
    }
}

public class ConditionalBranchingFlow : FlowConfig<ConditionalState>
{
    protected override void Configure(IFlowBuilder<ConditionalState> flow)
    {
        flow.Name("conditional-branching");

        flow.If(s => s.Value > 75)
            .EndIf();

        flow.If(s => s.Value > 75)
            .EndIf();

        flow.If(s => s.Value > 75)
            .EndIf();
    }
}

public class AggregationFlow : FlowConfig<AggregationState>
{
    protected override void Configure(IFlowBuilder<AggregationState> flow)
    {
        flow.Name("aggregation");

        flow.ForEach(s => s.Values)
            .Configure((value, f) =>
            {
                // Process each value
            })
            .OnComplete(s =>
            {
                s.Sum = s.Values.Sum();
                s.Count = s.Values.Count;
                s.Average = s.Values.Count > 0 ? s.Values.Average() : 0;
            })
            .EndForEach();
    }
}

public class NestedForEachFlow : FlowConfig<NestedForEachState>
{
    protected override void Configure(IFlowBuilder<NestedForEachState> flow)
    {
        flow.Name("nested-foreach");

        flow.ForEach(s => s.Groups)
            .Configure((group, f) =>
            {
                // Process each group
            })
            .OnComplete(s =>
            {
                s.TotalProcessed = s.Groups.Sum(g => g.Items.Count);
            })
            .EndForEach();
    }
}

public class ComplexFlow : FlowConfig<ComplexState>
{
    protected override void Configure(IFlowBuilder<ComplexState> flow)
    {
        flow.Name("complex-flow");

        flow.ForEach(s => s.Orders)
            .Configure((order, f) =>
            {
                // Process each order
            })
            .OnComplete(s =>
            {
                s.HighPriorityCount = s.Orders.Count(o => o.Priority == "High");
                s.TotalAmount = s.Orders.Sum(o => o.Amount);
            })
            .EndForEach();
    }
}

// Test States

public class ParallelProcessingState : IFlowState
{
    public string? FlowId { get; set; }
    public List<int> Items { get; set; } = new();
    public int ProcessedItems { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ErrorHandlingState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = new();
    public List<string> SuccessfulItems { get; set; } = new();
    public List<string> FailedItems { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ConditionalState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public string ExecutedBranch { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class AggregationState : IFlowState
{
    public string? FlowId { get; set; }
    public List<int> Values { get; set; } = new();
    public int Sum { get; set; }
    public int Count { get; set; }
    public double Average { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class NestedForEachState : IFlowState
{
    public string? FlowId { get; set; }
    public List<ItemGroup> Groups { get; set; } = new();
    public int TotalProcessed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ComplexState : IFlowState
{
    public string? FlowId { get; set; }
    public List<Order> Orders { get; set; } = new();
    public int HighPriorityCount { get; set; }
    public decimal TotalAmount { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Supporting Classes

public class ItemGroup
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
}

public class Order
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Priority { get; set; } = string.Empty;
}
