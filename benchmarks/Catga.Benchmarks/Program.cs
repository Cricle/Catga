using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Catga.Benchmarks;

/// <summary>
/// Main benchmark program entry point
/// Run with: dotnet run -c Release --project benchmarks/Catga.Benchmarks
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

        // Print summary
        Console.WriteLine();
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("Benchmark Summary");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();
        Console.WriteLine("To view detailed results, open:");
        Console.WriteLine("  BenchmarkDotNet.Artifacts/results/*.html");
        Console.WriteLine();
        Console.WriteLine("To run specific benchmark:");
        Console.WriteLine("  dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter *DistributedId*");
        Console.WriteLine("  dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter *ReflectionOptimization*");
        Console.WriteLine("  dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter *MessageRouting*");
        Console.WriteLine();
    }
}
