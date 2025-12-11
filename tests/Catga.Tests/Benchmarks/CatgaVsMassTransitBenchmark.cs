using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Benchmarks;

/// <summary>
/// Performance benchmark comparing Catga Flow DSL with MassTransit Saga
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CatgaVsMassTransitBenchmark
{
    private IServiceProvider? _catgaProvider;
    private IServiceProvider? _massTransitProvider;
    private DslFlowExecutor<OrderSagaState, OrderSagaFlow>? _catgaExecutor;
    // MassTransit saga would be initialized here if available

    [GlobalSetup]
    public void Setup()
    {
        // Setup Catga
        var catgaServices = new ServiceCollection();
        catgaServices.AddFlowDsl();
        catgaServices.AddFlow<OrderSagaState, OrderSagaFlow>();
        _catgaProvider = catgaServices.BuildServiceProvider();
        _catgaExecutor = _catgaProvider.GetService<DslFlowExecutor<OrderSagaState, OrderSagaFlow>>();

        // MassTransit setup would go here if available
        // var massTransitServices = new ServiceCollection();
        // massTransitServices.AddMassTransit(...);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_catgaProvider as IDisposable)?.Dispose();
        (_massTransitProvider as IDisposable)?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task CatgaFlowDsl_SimpleWorkflow()
    {
        var state = new OrderSagaState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = "ORD-" + Random.Shared.Next(10000, 99999),
            Amount = 1000.00m
        };

        var result = await _catgaExecutor!.RunAsync(state);
        if (!result.IsSuccess)
            throw new Exception("Flow failed");
    }

    [Benchmark]
    public async Task CatgaFlowDsl_ComplexWorkflowWithBranching()
    {
        var state = new OrderSagaState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = "ORD-" + Random.Shared.Next(10000, 99999),
            Amount = Random.Shared.Next(100, 10000),
            CustomerType = Random.Shared.Next(0, 3)
        };

        var executor = _catgaProvider!.GetService<DslFlowExecutor<OrderSagaState, ComplexOrderFlow>>();
        var result = await executor!.RunAsync(state);
        if (!result.IsSuccess)
            throw new Exception("Flow failed");
    }

    [Benchmark]
    public async Task CatgaFlowDsl_ParallelProcessing()
    {
        var state = new BatchProcessingState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(0, 100).Select(i => $"item-{i}").ToList()
        };

        var executor = _catgaProvider!.GetService<DslFlowExecutor<BatchProcessingState, ParallelBatchFlow>>();
        var result = await executor!.RunAsync(state);
        if (!result.IsSuccess)
            throw new Exception("Flow failed");
    }

    // Note: MassTransit benchmarks would be here if the library was available
    // They would follow similar patterns but use MassTransit's StateMachine API

    [Benchmark]
    public async Task CatgaFlowDsl_CompensationFlow()
    {
        var state = new CompensationState
        {
            FlowId = Guid.NewGuid().ToString(),
            ShouldFail = Random.Shared.Next(0, 10) > 5 // 50% failure rate
        };

        var executor = _catgaProvider!.GetService<DslFlowExecutor<CompensationState, CompensationFlow>>();
        var result = await executor!.RunAsync(state);
        // Don't throw on failure - compensation is expected
    }

    [Benchmark]
    public async Task CatgaFlowDsl_LargeStateTransfer()
    {
        var state = new LargeDataState
        {
            FlowId = Guid.NewGuid().ToString(),
            Data = new byte[10 * 1024], // 10KB
            Items = Enumerable.Range(0, 1000).Select(i => new DataItem
            {
                Id = i,
                Value = $"Value-{i}",
                Timestamp = DateTime.UtcNow
            }).ToList()
        };

        var executor = _catgaProvider!.GetService<DslFlowExecutor<LargeDataState, LargeDataFlow>>();
        var result = await executor!.RunAsync(state);
        if (!result.IsSuccess)
            throw new Exception("Flow failed");
    }
}

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddColumn(TargetMethodColumn.Method);
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.P95);
        AddColumn(BaselineRatioColumn.RatioMean);
        AddColumn(RankColumn.Arabic);

        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);

        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}

// Benchmark Flow Configurations

public class OrderSagaFlow : FlowConfig<OrderSagaState>
{
    protected override void Configure(IFlowBuilder<OrderSagaState> flow)
    {
        flow.Name("order-saga");

        flow;
        flow;
        flow;
        flow;
        flow;
    }
}

public class ComplexOrderFlow : FlowConfig<OrderSagaState>
{
    protected override void Configure(IFlowBuilder<OrderSagaState> flow)
    {
        flow.Name("complex-order");

        flow;

        // Customer type based processing
        flow.Switch(s => s.CustomerType)
            .Case(0, regular =>
            {
                regular;
                regular;
            })
            .Case(1, premium =>
            {
                premium;
                premium;
            })
            .Case(2, vip =>
            {
                vip;
                vip;
                vip;
            })
            .Default(unknown =>
            {
                unknown;
            })
            .EndSwitch();

        // Parallel operations
        flow.WhenAll(
            f => f,
            f => f,
            f => f
        );

        flow;
    }
}

public class ParallelBatchFlow : FlowConfig<BatchProcessingState>
{
    protected override void Configure(IFlowBuilder<BatchProcessingState> flow)
    {
        flow.Name("parallel-batch");

        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) =>
            {
                f.Step($"process-{item}", s =>
                {
                    s.ProcessedItems.Add(item);
                    s.ProcessedCount++;
                });
            })
            .EndForEach();
    }
}

public class CompensationFlow : FlowConfig<CompensationState>
{
    protected override void Configure(IFlowBuilder<CompensationState> flow)
    {
        flow.Name("compensation");

        flow
            .Compensate(s => s.CompensationSteps.Add("Undo Step1"));

        flow
            .Compensate(s => s.CompensationSteps.Add("Undo Step2"));

        flow.Step("step3", s =>
        {
            if (s.ShouldFail)
                throw new Exception("Simulated failure");
            s.Step3Completed = true;
        })
            .Compensate(s => s.CompensationSteps.Add("Undo Step3"));

        flow;
    }
}

public class LargeDataFlow : FlowConfig<LargeDataState>
{
    protected override void Configure(IFlowBuilder<LargeDataState> flow)
    {
        flow.Name("large-data");

        flow.Step("process-data", s =>
        {
            s.DataProcessed = true;
            s.ProcessingTime = DateTime.UtcNow;
        });

        flow.Step("transform", s =>
        {
            s.ItemCount = s.Items.Count;
            s.DataSize = s.Data.Length;
        });

        flow;
    }
}

// State Classes

public class OrderSagaState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal FinalAmount { get; set; }
    public int CustomerType { get; set; }
    public string ShippingMethod { get; set; } = string.Empty;
    public bool VipPerks { get; set; }
    public bool IsValid { get; set; }
    public bool InventoryReserved { get; set; }
    public bool PaymentProcessed { get; set; }
    public bool NotificationSent { get; set; }
    public bool Shipped { get; set; }
    public bool Completed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class BatchProcessingState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();
    public int ProcessedCount { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class CompensationState : IFlowState
{
    public string? FlowId { get; set; }
    public bool ShouldFail { get; set; }
    public bool Step1Completed { get; set; }
    public bool Step2Completed { get; set; }
    public bool Step3Completed { get; set; }
    public bool Completed { get; set; }
    public List<string> CompensationSteps { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class LargeDataState : IFlowState
{
    public string? FlowId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public List<DataItem> Items { get; set; } = new();
    public bool DataProcessed { get; set; }
    public DateTime ProcessingTime { get; set; }
    public int ItemCount { get; set; }
    public int DataSize { get; set; }
    public bool Completed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class DataItem
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
