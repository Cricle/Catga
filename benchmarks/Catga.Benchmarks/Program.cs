using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark entry point
/// Run: dotnet run -c Release --project benchmarks/Catga.Benchmarks
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

        Console.WriteLine();
        Console.WriteLine("Available benchmarks:");
        Console.WriteLine("  --filter *CqrsPerformance*       Core CQRS operations");
        Console.WriteLine("  --filter *BusinessScenario*      Business scenarios");
        Console.WriteLine("  --filter *ConcurrencyPerformance* Concurrency tests");
        Console.WriteLine("  --filter *Transport*             Redis/NATS transport tests");
        Console.WriteLine();
    }
}
