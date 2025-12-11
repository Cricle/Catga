using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Benchmarks;

/// <summary>
/// Benchmarks comparing Catga with multiple workflow/messaging frameworks
/// Simulating equivalent functionality of other frameworks for fair comparison
/// </summary>
[Config(typeof(FrameworkComparisonConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 5, iterationCount: 15)]
[RankColumn]
[MinColumn, MaxColumn]
[AllStatisticsColumn]
public class FrameworkComparisonBenchmarks
{
    private IServiceProvider? _catgaProvider;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddFlowDsl();

        // Register flows simulating different framework patterns
        services.AddFlow<WorkflowState, SimpleWorkflowFlow>();
        services.AddFlow<SagaState, SagaPatternFlow>();
        services.AddFlow<OrchestrationState, OrchestrationFlow>();
        services.AddFlow<StateMachineState, StateMachineFlow>();
        services.AddFlow<ChoreographyState, ChoreographyFlow>();
        services.AddFlow<ActivityState, ActivityBasedFlow>();

        _catgaProvider = services.BuildServiceProvider();
    }

    #region Simple Workflow Comparison (vs Elsa, Windows Workflow Foundation)

    [Benchmark(Description = "Catga - Simple Workflow")]
    [BenchmarkCategory("SimpleWorkflow")]
    public async Task<WorkflowState> Catga_SimpleWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<WorkflowState, SimpleWorkflowFlow>>();
        var state = new WorkflowState { FlowId = Guid.NewGuid().ToString() };
        var result = await executor!.RunAsync(state);
        return result.State;
    }

    [Benchmark(Description = "Simulated Elsa-style Workflow")]
    [BenchmarkCategory("SimpleWorkflow")]
    public async Task<WorkflowState> ElsaStyle_Workflow()
    {
        // Simulating Elsa's activity-based approach
        var state = new WorkflowState { FlowId = Guid.NewGuid().ToString() };

        // Activity 1
        await Task.Yield();
        state.Step1Complete = true;

        // Activity 2
        await Task.Yield();
        state.Step2Complete = true;

        // Activity 3
        await Task.Yield();
        state.Step3Complete = true;

        state.IsComplete = true;
        return state;
    }

    [Benchmark(Description = "Simulated WWF-style Workflow")]
    [BenchmarkCategory("SimpleWorkflow")]
    public async Task<WorkflowState> WWFStyle_Workflow()
    {
        // Simulating Windows Workflow Foundation approach
        var state = new WorkflowState { FlowId = Guid.NewGuid().ToString() };
        var activities = new List<Func<WorkflowState, Task>>
        {
            async s => { await Task.Yield(); s.Step1Complete = true; },
            async s => { await Task.Yield(); s.Step2Complete = true; },
            async s => { await Task.Yield(); s.Step3Complete = true; }
        };

        foreach (var activity in activities)
        {
            await activity(state);
        }

        state.IsComplete = true;
        return state;
    }

    #endregion

    #region Saga Pattern Comparison (vs MassTransit, NServiceBus Sagas)

    [Benchmark(Description = "Catga - Saga Pattern")]
    [BenchmarkCategory("Saga")]
    public async Task<SagaState> Catga_SagaPattern()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<SagaState, SagaPatternFlow>>();
        var state = new SagaState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = Guid.NewGuid().ToString()
        };
        var result = await executor!.RunAsync(state);
        return result.State;
    }

    [Benchmark(Description = "Simulated MassTransit Saga")]
    [BenchmarkCategory("Saga")]
    public async Task<SagaState> MassTransitStyle_Saga()
    {
        // Simulating MassTransit's message-driven saga
        var state = new SagaState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = Guid.NewGuid().ToString()
        };

        // Simulate message handling with state machine
        var messages = new[] { "OrderSubmitted", "PaymentReceived", "OrderShipped" };
        foreach (var message in messages)
        {
            await ProcessMessage(state, message);
        }

        return state;
    }

    [Benchmark(Description = "Simulated NServiceBus Saga")]
    [BenchmarkCategory("Saga")]
    public async Task<SagaState> NServiceBusStyle_Saga()
    {
        // Simulating NServiceBus saga with handlers
        var state = new SagaState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = Guid.NewGuid().ToString()
        };

        // Handle events
        await HandleOrderSubmitted(state);
        await HandlePaymentReceived(state);
        await HandleOrderShipped(state);

        return state;
    }

    #endregion

    #region Orchestration Comparison (vs Azure Durable Functions, Temporal)

    [Benchmark(Description = "Catga - Orchestration")]
    [BenchmarkCategory("Orchestration")]
    public async Task<OrchestrationState> Catga_Orchestration()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<OrchestrationState, OrchestrationFlow>>();
        var state = new OrchestrationState
        {
            FlowId = Guid.NewGuid().ToString(),
            Input = GenerateTestData(100)
        };
        var result = await executor!.RunAsync(state);
        return result.State;
    }

    [Benchmark(Description = "Simulated Durable Functions")]
    [BenchmarkCategory("Orchestration")]
    public async Task<OrchestrationState> DurableFunctionsStyle_Orchestration()
    {
        // Simulating Azure Durable Functions orchestration
        var state = new OrchestrationState
        {
            FlowId = Guid.NewGuid().ToString(),
            Input = GenerateTestData(100)
        };

        // Activity calls
        state.ProcessedData = await CallActivity("ProcessData", state.Input);
        state.ValidationResult = await CallActivity("Validate", state.ProcessedData);

        // Sub-orchestration
        if (state.ValidationResult)
        {
            state.FinalResult = await CallSubOrchestration("Finalize", state.ProcessedData);
        }

        return state;
    }

    [Benchmark(Description = "Simulated Temporal Workflow")]
    [BenchmarkCategory("Orchestration")]
    public async Task<OrchestrationState> TemporalStyle_Workflow()
    {
        // Simulating Temporal's workflow approach
        var state = new OrchestrationState
        {
            FlowId = Guid.NewGuid().ToString(),
            Input = GenerateTestData(100)
        };

        // Activities with retry logic
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                state.ProcessedData = await ExecuteActivity("ProcessData", state.Input);
                break;
            }
            catch
            {
                if (retry == 2) throw;
                await Task.Delay(10);
            }
        }

        state.ValidationResult = await ExecuteActivity("Validate", state.ProcessedData);
        state.FinalResult = state.ValidationResult ? await ExecuteActivity("Finalize", state.ProcessedData) : null;

        return state;
    }

    #endregion

    #region State Machine Comparison (vs Stateless, Automatonymous)

    [Benchmark(Description = "Catga - State Machine")]
    [BenchmarkCategory("StateMachine")]
    public async Task<StateMachineState> Catga_StateMachine()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<StateMachineState, StateMachineFlow>>();
        var state = new StateMachineState
        {
            FlowId = Guid.NewGuid().ToString(),
            CurrentState = "Initial"
        };
        var result = await executor!.RunAsync(state);
        return result.State;
    }

    [Benchmark(Description = "Simulated Stateless")]
    [BenchmarkCategory("StateMachine")]
    public async Task<StateMachineState> StatelessStyle_StateMachine()
    {
        // Simulating Stateless library approach
        var state = new StateMachineState
        {
            FlowId = Guid.NewGuid().ToString(),
            CurrentState = "Initial"
        };

        var transitions = new Dictionary<(string, string), string>
        {
            [("Initial", "Start")] = "Processing",
            [("Processing", "Process")] = "Validated",
            [("Validated", "Complete")] = "Completed"
        };

        var triggers = new[] { "Start", "Process", "Complete" };
        foreach (var trigger in triggers)
        {
            if (transitions.TryGetValue((state.CurrentState, trigger), out var newState))
            {
                state.CurrentState = newState;
                state.Transitions.Add($"{state.CurrentState}->{newState}");
                await Task.Yield();
            }
        }

        return state;
    }

    #endregion

    #region Parallel Processing Comparison

    [Benchmark(Description = "Catga - Parallel ForEach")]
    [BenchmarkCategory("Parallel")]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task<int> Catga_ParallelProcessing(int itemCount)
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ActivityState, ActivityBasedFlow>>();
        var state = new ActivityState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToList()
        };

        var result = await executor!.RunAsync(state);
        return result.State.ProcessedItems.Count;
    }

    [Benchmark(Description = "Task Parallel Library")]
    [BenchmarkCategory("Parallel")]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task<int> TPL_ParallelProcessing(int itemCount)
    {
        var items = Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToList();
        var processed = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (item, ct) =>
            {
                await Task.Yield();
                processed.Add(item);
            });

        return processed.Count;
    }

    [Benchmark(Description = "PLINQ Processing")]
    [BenchmarkCategory("Parallel")]
    [Arguments(100)]
    [Arguments(1000)]
    public Task<int> PLINQ_Processing(int itemCount)
    {
        var items = Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToList();

        var processed = items.AsParallel()
            .WithDegreeOfParallelism(10)
            .Select(item => ProcessItem(item))
            .ToList();

        return Task.FromResult(processed.Count);
    }

    #endregion

    #region Memory and Allocation Comparison

    [Benchmark(Description = "Catga - Memory Efficiency")]
    [BenchmarkCategory("Memory")]
    public async Task<long> Catga_MemoryEfficiency()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalMemory(false);

        var executor = _catgaProvider!.GetService<DslFlowExecutor<WorkflowState, SimpleWorkflowFlow>>();
        for (int i = 0; i < 100; i++)
        {
            var state = new WorkflowState { FlowId = $"mem-{i}" };
            await executor!.RunAsync(state);
        }

        var after = GC.GetTotalMemory(false);
        return (after - before) / 100;
    }

    #endregion

    // Helper methods
    private async Task ProcessMessage(SagaState state, string message)
    {
        await Task.Yield();
        switch (message)
        {
            case "OrderSubmitted":
                state.OrderSubmitted = true;
                break;
            case "PaymentReceived":
                state.PaymentReceived = true;
                break;
            case "OrderShipped":
                state.OrderShipped = true;
                break;
        }
    }

    private async Task HandleOrderSubmitted(SagaState state)
    {
        await Task.Yield();
        state.OrderSubmitted = true;
    }

    private async Task HandlePaymentReceived(SagaState state)
    {
        await Task.Yield();
        state.PaymentReceived = true;
    }

    private async Task HandleOrderShipped(SagaState state)
    {
        await Task.Yield();
        state.OrderShipped = true;
    }

    private List<string> GenerateTestData(int count)
    {
        return Enumerable.Range(0, count).Select(i => $"data-{i}").ToList();
    }

    private async Task<T> CallActivity<T>(string name, object input)
    {
        await Task.Yield();
        return default(T)!;
    }

    private async Task<T> CallSubOrchestration<T>(string name, object input)
    {
        await Task.Yield();
        return default(T)!;
    }

    private async Task<T> ExecuteActivity<T>(string name, object input)
    {
        await Task.Yield();
        return default(T)!;
    }

    private string ProcessItem(string item)
    {
        Thread.SpinWait(100);
        return item;
    }
}

public class FrameworkComparisonConfig : ManualConfig
{
    public FrameworkComparisonConfig()
    {
        AddColumn(TargetMethodColumn.Method);
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
        AddColumn(StatisticColumn.P95);
        AddColumn(BaselineRatioColumn.RatioMean);
        AddColumn(RankColumn.Arabic);

        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);

        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory);
    }
}

// Flow configurations
public class SimpleWorkflowFlow : FlowConfig<WorkflowState>
{
    protected override void Configure(IFlowBuilder<WorkflowState> flow)
    {
        flow;
        flow;
        flow;
        flow;
    }
}

public class SagaPatternFlow : FlowConfig<SagaState>
{
    protected override void Configure(IFlowBuilder<SagaState> flow)
    {
        flow;
        flow;
        flow;
        flow;
    }
}

public class OrchestrationFlow : FlowConfig<OrchestrationState>
{
    protected override void Configure(IFlowBuilder<OrchestrationState> flow)
    {
        flow;
        flow;
        flow.If(s => s.ValidationResult)
            
        .EndIf();
    }
}

public class StateMachineFlow : FlowConfig<StateMachineState>
{
    protected override void Configure(IFlowBuilder<StateMachineState> flow)
    {
        flow; });
        flow; });
        flow; });
    }
}

public class ChoreographyFlow : FlowConfig<ChoreographyState>
{
    protected override void Configure(IFlowBuilder<ChoreographyState> flow)
    {
        flow);
        flow);
        flow);
    }
}

public class ActivityBasedFlow : FlowConfig<ActivityState>
{
    protected override void Configure(IFlowBuilder<ActivityState> flow)
    {
        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) => f))
            .EndForEach();
    }
}

// State classes
public class WorkflowState : IFlowState
{
    public string? FlowId { get; set; }
    public bool Step1Complete { get; set; }
    public bool Step2Complete { get; set; }
    public bool Step3Complete { get; set; }
    public bool IsComplete { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SagaState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public bool OrderSubmitted { get; set; }
    public bool PaymentReceived { get; set; }
    public bool OrderShipped { get; set; }
    public bool IsComplete { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class OrchestrationState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Input { get; set; } = new();
    public List<string>? ProcessedData { get; set; }
    public bool ValidationResult { get; set; }
    public List<string>? FinalResult { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class StateMachineState : IFlowState
{
    public string? FlowId { get; set; }
    public string CurrentState { get; set; } = "Initial";
    public List<string> Transitions { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ChoreographyState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> EmittedEvents { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ActivityState : IFlowState
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
