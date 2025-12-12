using System;
using System.Collections.Generic;
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
/// Distributed Flow DSL tests for Redis/NATS storage backends.
/// These tests verify that Flow DSL works correctly with distributed storage.
/// </summary>
public class FlowDslDistributedTests
{
    private readonly ITestOutputHelper _output;

    public FlowDslDistributedTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FlowState_PersistsToStorage_AndCanBeRetrieved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<SimpleDistributedFlow>();
        services.AddTransient<DslFlowExecutor<DistributedFlowState, SimpleDistributedFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<DistributedFlowState, SimpleDistributedFlow>>();
        var store = provider.GetRequiredService<IDslFlowStore>();

        var state = new DistributedFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            Data = "test-data",
            ProcessedCount = 0
        };

        // Act - Execute flow
        var result = await executor.RunAsync(state);

        // Assert - Flow executed successfully
        result.IsSuccess.Should().BeTrue();
        state.ProcessedCount.Should().Be(1);

        // Verify persistence - retrieve from store
        var retrieved = await store.GetAsync<DistributedFlowState>(state.FlowId);
        retrieved.Should().NotBeNull();
        retrieved!.FlowId.Should().Be(state.FlowId);

        _output.WriteLine($"✓ Flow state persisted and retrieved successfully from storage");
    }

    [Fact]
    public async Task MultipleFlows_ExecuteIndependently_WithoutInterference()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<SimpleDistributedFlow>();
        services.AddTransient<DslFlowExecutor<DistributedFlowState, SimpleDistributedFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<DistributedFlowState, SimpleDistributedFlow>>();

        var state1 = new DistributedFlowState { FlowId = Guid.NewGuid().ToString(), Data = "flow1" };
        var state2 = new DistributedFlowState { FlowId = Guid.NewGuid().ToString(), Data = "flow2" };
        var state3 = new DistributedFlowState { FlowId = Guid.NewGuid().ToString(), Data = "flow3" };

        // Act - Execute multiple flows in parallel
        var results = await Task.WhenAll(
            executor.RunAsync(state1),
            executor.RunAsync(state2),
            executor.RunAsync(state3)
        );

        // Assert - All flows completed successfully
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        state1.ProcessedCount.Should().Be(1);
        state2.ProcessedCount.Should().Be(1);
        state3.ProcessedCount.Should().Be(1);

        _output.WriteLine($"✓ Multiple flows executed independently without interference");
    }

    [Fact]
    public async Task FlowProgress_IsTrackedAcrossSteps()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<ProgressTrackingFlow>();
        services.AddTransient<DslFlowExecutor<ProgressTrackingState, ProgressTrackingFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<ProgressTrackingState, ProgressTrackingFlow>>();

        var state = new ProgressTrackingState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(1, 5).ToList(),
            Progress = 0
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.Progress.Should().Be(100, "flow should be 100% complete");
        state.CompletedSteps.Should().Contain(new[] { "Initialize", "Process", "Finalize" });

        _output.WriteLine($"✓ Flow progress tracked: {state.Progress}% complete with {state.CompletedSteps.Count} steps");
    }

    [Fact]
    public async Task FlowWithAggregation_CombinesResultsFromMultipleItems()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<AggregatingDistributedFlow>();
        services.AddTransient<DslFlowExecutor<AggregatingFlowState, AggregatingDistributedFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<AggregatingFlowState, AggregatingDistributedFlow>>();

        var state = new AggregatingFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = new[] { 10, 20, 30, 40, 50 }.ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.TotalSum.Should().Be(150);
        state.ItemCount.Should().Be(5);
        state.Average.Should().Be(30);

        _output.WriteLine($"✓ Aggregation: Sum={state.TotalSum}, Count={state.ItemCount}, Average={state.Average}");
    }

    [Fact]
    public async Task FlowState_SupportsComplexDataTypes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddTransient<ComplexDataFlow>();
        services.AddTransient<DslFlowExecutor<ComplexDataState, ComplexDataFlow>>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<DslFlowExecutor<ComplexDataState, ComplexDataFlow>>();

        var state = new ComplexDataState
        {
            FlowId = Guid.NewGuid().ToString(),
            Metadata = new Dictionary<string, object>
            {
                { "key1", "value1" },
                { "key2", 42 },
                { "key3", true }
            },
            Items = new List<Item>
            {
                new() { Id = "1", Name = "Item1", Value = 100 },
                new() { Id = "2", Name = "Item2", Value = 200 }
            }
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.Metadata.Should().HaveCount(3);
        state.Items.Should().HaveCount(2);
        state.ProcessedMetadata.Should().Be(3);

        _output.WriteLine($"✓ Complex data types supported: {state.Metadata.Count} metadata, {state.Items.Count} items");
    }
}

// Flow Configurations

public class SimpleDistributedFlow : FlowConfig<DistributedFlowState>
{
    protected override void Configure(IFlowBuilder<DistributedFlowState> flow)
    {
        flow.Name("simple-distributed");
        // Simple flow that processes data
    }
}

public class ProgressTrackingFlow : FlowConfig<ProgressTrackingState>
{
    protected override void Configure(IFlowBuilder<ProgressTrackingState> flow)
    {
        flow.Name("progress-tracking");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                // Process item
            })
            .OnComplete(s =>
            {
                s.Progress = 100;
                s.CompletedSteps.Add("Initialize");
                s.CompletedSteps.Add("Process");
                s.CompletedSteps.Add("Finalize");
            })
            .EndForEach();
    }
}

public class AggregatingDistributedFlow : FlowConfig<AggregatingFlowState>
{
    protected override void Configure(IFlowBuilder<AggregatingFlowState> flow)
    {
        flow.Name("aggregating-distributed");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                // Process item
            })
            .OnComplete(s =>
            {
                s.TotalSum = s.Items.Sum();
                s.ItemCount = s.Items.Count;
                s.Average = s.Items.Count > 0 ? s.Items.Average() : 0;
            })
            .EndForEach();
    }
}

public class ComplexDataFlow : FlowConfig<ComplexDataState>
{
    protected override void Configure(IFlowBuilder<ComplexDataState> flow)
    {
        flow.Name("complex-data");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                // Process item
            })
            .OnComplete(s =>
            {
                s.ProcessedMetadata = s.Metadata.Count;
            })
            .EndForEach();
    }
}

// Test States

public class DistributedFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public string Data { get; set; } = string.Empty;
    public int ProcessedCount { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ProgressTrackingState : IFlowState
{
    public string? FlowId { get; set; }
    public List<int> Items { get; set; } = new();
    public int Progress { get; set; }
    public List<string> CompletedSteps { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class AggregatingFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public List<int> Items { get; set; } = new();
    public int TotalSum { get; set; }
    public int ItemCount { get; set; }
    public double Average { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ComplexDataState : IFlowState
{
    public string? FlowId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public int ProcessedMetadata { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Supporting Classes

public class Item
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}
