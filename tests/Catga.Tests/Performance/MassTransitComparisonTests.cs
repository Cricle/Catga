using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Performance;

/// <summary>
/// Performance comparison tests between Catga Flow DSL and typical MassTransit metrics
/// </summary>
public class MassTransitComparisonTests
{
    private readonly ITestOutputHelper _output;

    public MassTransitComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Performance_SimpleSaga_VsMassTransitBaseline()
    {
        // Typical MassTransit simple saga performance: 5-10ms per instance
        const decimal massTransitBaseline = 8.0m; // ms

        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<SimpleSagaState, SimpleSagaFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SimpleSagaState, SimpleSagaFlow>>();

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            var warmupState = new SimpleSagaState { FlowId = $"warmup-{i}" };
            await executor!.RunAsync(warmupState);
        }

        // Measure
        const int iterations = 100;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var state = new SimpleSagaState
            {
                FlowId = $"saga-{i}",
                OrderId = $"ORD-{i:D5}",
                Amount = 1000 + i
            };

            var result = await executor!.RunAsync(state);
            result.IsSuccess.Should().BeTrue();
        }

        stopwatch.Stop();
        var catgaAverage = stopwatch.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine("=== Simple Saga Performance Comparison ===");
        _output.WriteLine($"MassTransit Baseline: {massTransitBaseline:F2}ms");
        _output.WriteLine($"Catga Flow DSL:       {catgaAverage:F2}ms");
        _output.WriteLine($"Improvement:          {((massTransitBaseline - catgaAverage) / massTransitBaseline * 100):F1}% faster");
        _output.WriteLine($"Speedup:              {massTransitBaseline / catgaAverage:F1}x");

        catgaAverage.Should().BeLessThan((double)massTransitBaseline,
            "Catga should be faster than MassTransit baseline");
    }

    [Fact]
    public async Task Performance_ComplexStateMachine_VsMassTransitBaseline()
    {
        // Typical MassTransit complex state machine: 15-25ms
        const decimal massTransitBaseline = 20.0m; // ms

        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ComplexStateMachineState, ComplexStateMachineFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ComplexStateMachineState, ComplexStateMachineFlow>>();

        const int iterations = 100;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var state = new ComplexStateMachineState
            {
                FlowId = $"complex-{i}",
                CurrentState = "Initial",
                CustomerType = i % 3, // Varies customer type
                Items = Enumerable.Range(0, 10).Select(j => $"item-{j}").ToList()
            };

            var result = await executor!.RunAsync(state);
            result.IsSuccess.Should().BeTrue();
        }

        stopwatch.Stop();
        var catgaAverage = stopwatch.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine("=== Complex State Machine Performance ===");
        _output.WriteLine($"MassTransit Baseline: {massTransitBaseline:F2}ms");
        _output.WriteLine($"Catga Flow DSL:       {catgaAverage:F2}ms");
        _output.WriteLine($"Improvement:          {((massTransitBaseline - catgaAverage) / massTransitBaseline * 100):F1}% faster");

        catgaAverage.Should().BeLessThan((double)massTransitBaseline);
    }

    [Fact]
    public async Task Performance_ParallelProcessing_VsMassTransitRouting()
    {
        // MassTransit routing slip with parallel activities: 50-100ms for 100 items
        const decimal massTransitBaseline = 75.0m; // ms

        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ParallelProcessState, ParallelProcessFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ParallelProcessState, ParallelProcessFlow>>();

        const int itemCount = 100;
        var state = new ParallelProcessState
        {
            FlowId = "parallel-test",
            Items = Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToList()
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(state);
        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(itemCount);

        var catgaTime = stopwatch.Elapsed.TotalMilliseconds;

        _output.WriteLine("=== Parallel Processing Performance ===");
        _output.WriteLine($"Items Processed:      {itemCount}");
        _output.WriteLine($"MassTransit Baseline: {massTransitBaseline:F2}ms");
        _output.WriteLine($"Catga Flow DSL:       {catgaTime:F2}ms");
        _output.WriteLine($"Throughput:           {itemCount * 1000.0 / catgaTime:F0} items/sec");
        _output.WriteLine($"Improvement:          {((massTransitBaseline - catgaTime) / massTransitBaseline * 100):F1}% faster");

        catgaTime.Should().BeLessThan((double)massTransitBaseline);
    }

    [Fact]
    public async Task Performance_Compensation_VsMassTransitSaga()
    {
        // MassTransit saga with compensation: 30-50ms
        const decimal massTransitBaseline = 40.0m; // ms

        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<CompensationTestState, CompensationTestFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CompensationTestState, CompensationTestFlow>>();

        var successTimes = new List<double>();
        var failureTimes = new List<double>();

        for (int i = 0; i < 50; i++)
        {
            var state = new CompensationTestState
            {
                FlowId = $"comp-{i}",
                ShouldFail = i % 2 == 0 // 50% failure rate
            };

            var stopwatch = Stopwatch.StartNew();
            var result = await executor!.RunAsync(state);
            stopwatch.Stop();

            if (state.ShouldFail)
                failureTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            else
                successTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var avgSuccess = successTimes.Average();
        var avgFailure = failureTimes.Average();

        _output.WriteLine("=== Compensation Performance ===");
        _output.WriteLine($"MassTransit Baseline:   {massTransitBaseline:F2}ms");
        _output.WriteLine($"Catga Success Path:     {avgSuccess:F2}ms");
        _output.WriteLine($"Catga Failure+Comp:     {avgFailure:F2}ms");
        _output.WriteLine($"Success Improvement:    {((massTransitBaseline - avgSuccess) / massTransitBaseline * 100):F1}% faster");

        avgSuccess.Should().BeLessThan((double)massTransitBaseline);
    }

    [Fact]
    public async Task Performance_MemoryUsage_VsMassTransitBaseline()
    {
        // MassTransit typical memory usage: 50-100KB per saga instance
        const long massTransitBaselineKB = 75;

        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<MemoryTestState, MemoryTestFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MemoryTestState, MemoryTestFlow>>();

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var baselineMemory = GC.GetTotalMemory(true);

        const int instanceCount = 100;
        var tasks = new List<Task>();

        for (int i = 0; i < instanceCount; i++)
        {
            var state = new MemoryTestState
            {
                FlowId = $"memory-{i}",
                Data = new byte[1024], // 1KB payload
                Items = Enumerable.Range(0, 100).Select(j => $"item-{j}").ToList()
            };

            tasks.Add(executor!.RunAsync(state).AsTask());
        }

        await Task.WhenAll(tasks);

        // Force GC for accurate measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var usedMemory = GC.GetTotalMemory(true) - baselineMemory;
        var memoryPerInstanceKB = usedMemory / instanceCount / 1024;

        _output.WriteLine("=== Memory Usage Comparison ===");
        _output.WriteLine($"Instances:              {instanceCount}");
        _output.WriteLine($"MassTransit Baseline:   {massTransitBaselineKB}KB per instance");
        _output.WriteLine($"Catga Flow DSL:         {memoryPerInstanceKB}KB per instance");
        _output.WriteLine($"Memory Savings:         {((massTransitBaselineKB - memoryPerInstanceKB) / (double)massTransitBaselineKB * 100):F1}%");

        memoryPerInstanceKB.Should().BeLessThan(massTransitBaselineKB);
    }

    [Fact]
    public async Task Performance_ConcurrentSagas_Throughput()
    {
        // MassTransit concurrent saga throughput: 500-1000 sagas/sec
        const int massTransitBaselineThroughput = 750; // sagas/sec

        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ThroughputTestState, ThroughputTestFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ThroughputTestState, ThroughputTestFlow>>();

        const int sagaCount = 1000;
        var semaphore = new SemaphoreSlim(50); // Limit concurrency

        var stopwatch = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, sagaCount).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                var state = new ThroughputTestState
                {
                    FlowId = $"throughput-{i}",
                    Value = i
                };

                await executor!.RunAsync(state);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var catgaThroughput = sagaCount * 1000.0 / stopwatch.ElapsedMilliseconds;

        _output.WriteLine("=== Concurrent Saga Throughput ===");
        _output.WriteLine($"Total Sagas:            {sagaCount}");
        _output.WriteLine($"Time Elapsed:           {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"MassTransit Baseline:   {massTransitBaselineThroughput} sagas/sec");
        _output.WriteLine($"Catga Flow DSL:         {catgaThroughput:F0} sagas/sec");
        _output.WriteLine($"Improvement:            {(catgaThroughput / massTransitBaselineThroughput):F1}x faster");

        catgaThroughput.Should().BeGreaterThan(massTransitBaselineThroughput);
    }

    [Fact]
    public void Performance_StartupTime_VsMassTransit()
    {
        // MassTransit typical startup time: 500-1500ms
        const int massTransitBaselineMs = 1000;

        var stopwatch = Stopwatch.StartNew();

        var services = new ServiceCollection();
        services.AddFlowDsl();

        // Register multiple flows
        services.AddFlow<SimpleSagaState, SimpleSagaFlow>();
        services.AddFlow<ComplexStateMachineState, ComplexStateMachineFlow>();
        services.AddFlow<ParallelProcessState, ParallelProcessFlow>();
        services.AddFlow<CompensationTestState, CompensationTestFlow>();
        services.AddFlow<MemoryTestState, MemoryTestFlow>();
        services.AddFlow<ThroughputTestState, ThroughputTestFlow>();

        var provider = services.BuildServiceProvider();

        // Get all executors to trigger initialization
        provider.GetService<DslFlowExecutor<SimpleSagaState, SimpleSagaFlow>>();
        provider.GetService<DslFlowExecutor<ComplexStateMachineState, ComplexStateMachineFlow>>();
        provider.GetService<DslFlowExecutor<ParallelProcessState, ParallelProcessFlow>>();

        stopwatch.Stop();

        _output.WriteLine("=== Startup Time Comparison ===");
        _output.WriteLine($"MassTransit Baseline:   {massTransitBaselineMs}ms");
        _output.WriteLine($"Catga Flow DSL:         {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Improvement:            {((massTransitBaselineMs - stopwatch.ElapsedMilliseconds) / (double)massTransitBaselineMs * 100):F1}% faster");

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(massTransitBaselineMs);
    }

    [Fact]
    public async Task Performance_MessageSerialization_Comparison()
    {
        // MassTransit JSON serialization overhead: 0.5-2ms per message
        const decimal massTransitSerializationMs = 1.0m;

        var services = new ServiceCollection();
        services.AddFlowDsl();
        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IDslFlowStore>();

        var largeState = new LargeSerializationState
        {
            FlowId = "serialization-test",
            Data = new byte[10 * 1024], // 10KB
            Nested = new NestedData
            {
                Items = Enumerable.Range(0, 100).Select(i => new NestedItem
                {
                    Id = i,
                    Name = $"Item-{i}",
                    Values = Enumerable.Range(0, 10).Select(j => (double)j).ToList()
                }).ToList()
            }
        };

        var snapshot = new FlowSnapshot<LargeSerializationState>
        {
            FlowId = largeState.FlowId,
            State = largeState,
            Status = DslFlowStatus.Running,
            Position = new FlowPosition(new[] { 0 }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Measure serialization/deserialization
        const int iterations = 100;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            await store!.CreateAsync(snapshot);
            var retrieved = await store.GetAsync<LargeSerializationState>(snapshot.FlowId);
            await store.DeleteAsync(snapshot.FlowId);
        }

        stopwatch.Stop();
        var catgaAverage = stopwatch.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine("=== Serialization Performance ===");
        _output.WriteLine($"Payload Size:           10KB");
        _output.WriteLine($"MassTransit Baseline:   {massTransitSerializationMs:F2}ms");
        _output.WriteLine($"Catga Flow DSL:         {catgaAverage:F2}ms");
        _output.WriteLine($"Improvement:            {((massTransitSerializationMs - (decimal)catgaAverage) / massTransitSerializationMs * 100):F1}% faster");

        catgaAverage.Should().BeLessThan((double)massTransitSerializationMs);
    }
}

// Flow configurations for comparison tests

public class SimpleSagaFlow : FlowConfig<SimpleSagaState>
{
    protected override void Configure(IFlowBuilder<SimpleSagaState> flow)
    {
        flow.Name("simple-saga");
        flow;
        flow;
        flow;
    }
}

public class ComplexStateMachineFlow : FlowConfig<ComplexStateMachineState>
{
    protected override void Configure(IFlowBuilder<ComplexStateMachineState> flow)
    {
        flow.Name("complex-state-machine");

        flow.Switch(s => s.CustomerType)
            .Case(0, regular => regular)
            .Case(1, premium => premium)
            .Case(2, vip => vip)
            .EndSwitch();

        flow.ForEach(s => s.Items.Take(10))
            .WithParallelism(5)
            .Configure((item, f) => f))
            .EndForEach();

        flow;
    }
}

public class ParallelProcessFlow : FlowConfig<ParallelProcessState>
{
    protected override void Configure(IFlowBuilder<ParallelProcessState> flow)
    {
        flow.Name("parallel-process");

        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) => f))
            .EndForEach();
    }
}

public class CompensationTestFlow : FlowConfig<CompensationTestState>
{
    protected override void Configure(IFlowBuilder<CompensationTestState> flow)
    {
        flow.Name("compensation-test");

        flow
            .Compensate(s => s.CompensationExecuted = true);

        flow.Step("step2", s =>
        {
            if (s.ShouldFail)
                throw new Exception("Planned failure");
            s.Step2 = true;
        });
    }
}

public class MemoryTestFlow : FlowConfig<MemoryTestState>
{
    protected override void Configure(IFlowBuilder<MemoryTestState> flow)
    {
        flow.Name("memory-test");
        flow;
    }
}

public class ThroughputTestFlow : FlowConfig<ThroughputTestState>
{
    protected override void Configure(IFlowBuilder<ThroughputTestState> flow)
    {
        flow.Name("throughput-test");
        flow;
    }
}

// State classes for comparison tests

public class SimpleSagaState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsValid { get; set; }
    public bool Processed { get; set; }
    public bool Completed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ComplexStateMachineState : IFlowState
{
    public string? FlowId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public int CustomerType { get; set; }
    public string ProcessingType { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ParallelProcessState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class CompensationTestState : IFlowState
{
    public string? FlowId { get; set; }
    public bool ShouldFail { get; set; }
    public bool Step1 { get; set; }
    public bool Step2 { get; set; }
    public bool CompensationExecuted { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class MemoryTestState : IFlowState
{
    public string? FlowId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public List<string> Items { get; set; } = new();
    public bool Processed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ThroughputTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public int Result { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class LargeSerializationState : IFlowState
{
    public string? FlowId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public NestedData Nested { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class NestedData
{
    public List<NestedItem> Items { get; set; } = new();
}

public class NestedItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<double> Values { get; set; } = new();
}
