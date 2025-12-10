using Catga.Flow.Dsl;

namespace ForEachDemo;

/// <summary>
/// 🎯 Catga ForEach Advanced Features Demo
///
/// This example demonstrates all the new ForEach capabilities:
/// - ⚡ Streaming processing for large datasets
/// - 📈 Performance metrics collection
/// - 🔄 Parallel processing with configurable concurrency
/// - 📦 Batch processing for memory efficiency
/// - 🛡️ Circuit breaker for fault tolerance
/// - 🔄 Flexible failure handling strategies
/// </summary>

// Example state for data processing workflow
public class DataProcessingState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> DataItems { get; set; } = [];
    public Dictionary<string, string> ProcessedResults { get; set; } = [];
    public List<string> FailedItems { get; set; } = [];
    public int TotalProcessed { get; set; }
    public int TotalFailed { get; set; }

    // IFlowState implementation
    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

/// <summary>
/// Example Flow Configuration showcasing all ForEach advanced features
/// </summary>
public class AdvancedDataProcessingFlow : FlowConfig<DataProcessingState>
{
    protected override void Configure(IFlowBuilder<DataProcessingState> flow)
    {
        flow.Name("advanced-data-processing");

        // 🚀 ForEach with ALL new advanced features
        flow.ForEach<string>(s => s.DataItems)

            // 📝 Configure processing steps for each item
            .Configure((item, f) =>
            {
                // This is where you would define the processing logic
                // For now, this is a simplified placeholder
                Console.WriteLine($"Configuring processing for: {item}");
            })

            // ⚡ STREAMING: Handle large or infinite collections efficiently
            .WithStreaming(true)

            // 📈 METRICS: Enable comprehensive performance monitoring
            .WithMetrics(true)

            // 🔄 PARALLELISM: Process multiple items concurrently
            .WithParallelism(3)

            // 📦 BATCHING: Process items in batches for memory efficiency
            .WithBatchSize(5)

            // 🛡️ CIRCUIT BREAKER: Protect against cascading failures
            .WithCircuitBreaker(
                failureThreshold: 3,                    // Open after 3 failures
                breakDuration: TimeSpan.FromSeconds(30) // Stay open for 30 seconds
            )

            // 🔄 FAILURE HANDLING: Continue processing despite individual failures
            .ContinueOnFailure()

            // ✅ SUCCESS CALLBACK: Execute when an item processes successfully
            .OnItemSuccess((state, item, result) =>
            {
                state.TotalProcessed++;
                state.ProcessedResults[item] = result?.ToString() ?? "success";
                Console.WriteLine($"✅ Processed: {item}");
            })

            // ❌ FAILURE CALLBACK: Execute when an item fails to process
            .OnItemFail((state, item, error) =>
            {
                state.TotalFailed++;
                state.FailedItems.Add(item);
                Console.WriteLine($"❌ Failed: {item} - {error}");
            })

            // 🎉 COMPLETION CALLBACK: Execute when all items are processed
            .OnComplete(state =>
            {
                Console.WriteLine($"🎉 Complete! Processed: {state.TotalProcessed}, Failed: {state.TotalFailed}");
            })

        .EndForEach();
    }
}

/// <summary>
/// Example demonstrating streaming ForEach for large datasets
/// </summary>
public class StreamingDataFlow : FlowConfig<DataProcessingState>
{
    protected override void Configure(IFlowBuilder<DataProcessingState> flow)
    {
        flow.Name("streaming-data-processing");

        flow.ForEach<string>(s => s.DataItems)
            .WithStreaming(true)        // 🌊 Enable streaming mode
            .WithBatchSize(100)         // 📦 Large batches for throughput
            .WithParallelism(10)        // 🚀 High concurrency
            .WithMetrics(true)          // 📊 Monitor performance
            .ContinueOnFailure()        // 🔄 Resilient processing
            .OnItemSuccess((state, item, result) => state.TotalProcessed++)
        .EndForEach();
    }
}

/// <summary>
/// Example demonstrating circuit breaker for external API calls
/// </summary>
public class ResilientApiFlow : FlowConfig<DataProcessingState>
{
    protected override void Configure(IFlowBuilder<DataProcessingState> flow)
    {
        flow.Name("resilient-api-processing");

        flow.ForEach<string>(s => s.DataItems)
            .WithCircuitBreaker(
                failureThreshold: 5,                    // 🛡️ Open after 5 failures
                breakDuration: TimeSpan.FromMinutes(2)  // ⏱️ Stay open for 2 minutes
            )
            .WithParallelism(3)         // 🔄 Limited concurrency for external APIs
            .ContinueOnFailure()        // 🔄 Continue despite failures
            .OnItemFail((state, item, error) =>
            {
                Console.WriteLine($"🚨 Circuit breaker may have opened for: {item}");
                state.FailedItems.Add(item);
            })
        .EndForEach();
    }
}

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("🚀 Catga ForEach Advanced Features Demo");
        Console.WriteLine("=======================================");
        Console.WriteLine();

        Console.WriteLine("📋 Available ForEach Features:");
        Console.WriteLine("  ⚡ WithStreaming(true)     - Handle large/infinite collections");
        Console.WriteLine("  📈 WithMetrics(true)       - Performance monitoring & analytics");
        Console.WriteLine("  🔄 WithParallelism(n)      - Concurrent processing");
        Console.WriteLine("  📦 WithBatchSize(n)        - Memory-efficient batching");
        Console.WriteLine("  🛡️ WithCircuitBreaker()    - Fault tolerance & resilience");
        Console.WriteLine("  🔄 ContinueOnFailure()     - Flexible error handling");
        Console.WriteLine("  ✅ OnItemSuccess()         - Success callbacks");
        Console.WriteLine("  ❌ OnItemFail()            - Failure callbacks");
        Console.WriteLine("  🎉 OnComplete()            - Completion callbacks");
        Console.WriteLine();

        Console.WriteLine("💡 Example Usage Patterns:");
        Console.WriteLine();

        Console.WriteLine("🌊 High-Volume Streaming:");
        Console.WriteLine("  .WithStreaming(true)");
        Console.WriteLine("  .WithBatchSize(1000)");
        Console.WriteLine("  .WithParallelism(10)");
        Console.WriteLine();

        Console.WriteLine("🛡️ Resilient API Processing:");
        Console.WriteLine("  .WithCircuitBreaker(5, TimeSpan.FromMinutes(2))");
        Console.WriteLine("  .WithParallelism(3)");
        Console.WriteLine("  .ContinueOnFailure()");
        Console.WriteLine();

        Console.WriteLine("📊 Performance Monitoring:");
        Console.WriteLine("  .WithMetrics(true)");
        Console.WriteLine("  .OnItemSuccess((state, item, result) => /* track success */)");
        Console.WriteLine("  .OnItemFail((state, item, error) => /* track failures */)");
        Console.WriteLine();

        Console.WriteLine("🎯 All features are production-ready and can be combined!");
        Console.WriteLine("📖 See documentation for complete examples and best practices.");

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}
