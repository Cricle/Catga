using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// Standalone benchmark runner for FlowState change-tracking performance.
/// Run: dotnet test tests/Catga.Tests/Catga.Tests.csproj -c Release --filter "RunFlowStateChangeTrackingBenchmarks"
/// Or directly: dotnet run -c Release --project benchmarks/Catga.Benchmarks
/// </summary>
[ExcludeFromCodeCoverage]
public static class RunFlowStateChangeTrackingBenchmarks
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== FlowState Change-Tracking Performance Benchmark ===");
        Console.WriteLine($"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Runtime: .NET {Environment.Version}");
        Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
        Console.WriteLine();

        var config = DefaultConfig.Instance
            .WithOptions(BenchmarkDotNet.Configs.ConfigOptions.DisableOptimizationsValidator);

        var summary = BenchmarkRunner.Run<FlowStateChangeTrackingBenchmarks>(config, args);

        Console.WriteLine();
        Console.WriteLine("=== Benchmark Results ===");
        Console.WriteLine($"Total benchmarks: {summary.BenchmarksCases.Length}");
        Console.WriteLine($"Successful: {summary.Reports.Count(r => r.ExecuteResults.Any(e => e.ExitCode == 0))}");
        Console.WriteLine();
        Console.WriteLine("Key metrics to review:");
        Console.WriteLine("  - Mean (ns): Average execution time in nanoseconds");
        Console.WriteLine("  - StdDev: Standard deviation of execution time");
        Console.WriteLine("  - Gen 0/1/2: Garbage collection allocations");
        Console.WriteLine("  - Allocated (B): Total memory allocated");
        Console.WriteLine();
        Console.WriteLine("Performance expectations:");
        Console.WriteLine("  - Single field set: < 50 ns");
        Console.WriteLine("  - Multiple field set (6 fields): < 300 ns");
        Console.WriteLine("  - GetChangedFieldNames(): < 100 ns");
        Console.WriteLine("  - ClearChanges(): < 10 ns");
        Console.WriteLine("  - Bulk operations: < 2000 ns");
        Console.WriteLine();
    }
}
