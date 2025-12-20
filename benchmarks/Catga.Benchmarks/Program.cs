using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Catga.Benchmarks;

/// <summary>
/// Catga Framework Benchmark Suite
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""

        ╔═══════════════════════════════════════════════════════════════════╗
        ║              Catga Framework Benchmark Suite                      ║
        ╠═══════════════════════════════════════════════════════════════════╣
        ║                                                                   ║
        ║  Usage: dotnet run -c Release -- --filter <pattern>               ║
        ║                                                                   ║
        ║  Available benchmarks:                                            ║
        ║                                                                   ║
        ║  1. Core CQRS:           --filter *Core*                          ║
        ║  2. Business Scenarios:  --filter *Business*                      ║
        ║  3. Concurrency:         --filter *Concurrency*                   ║
        ║  4. Event Sourcing:      --filter *EventSourcing*                 ║
        ║  5. Framework Compare:   --filter *FrameworkComparison*           ║
        ║  6. Transport (Docker):  --filter *Transport*                     ║
        ║                                                                   ║
        ║  Run all:                --filter *                               ║
        ║                                                                   ║
        ║  Note: Transport benchmarks require Docker running!               ║
        ║                                                                   ║
        ╚═══════════════════════════════════════════════════════════════════╝

        """);
    }
}
