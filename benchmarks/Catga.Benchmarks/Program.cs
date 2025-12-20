using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Catga.Benchmarks;

/// <summary>
/// Catga Framework Benchmark Suite
/// 
/// Run all: dotnet run -c Release --project benchmarks/Catga.Benchmarks
/// Run specific: dotnet run -c Release --filter *ClassName*
/// 
/// Available benchmarks:
///   1. CoreBenchmarks          - Core CQRS operations (Command/Query/Event)
///   2. BusinessBenchmarks      - Real-world business scenarios
///   3. ConcurrencyBenchmarks   - High-concurrency stress tests
///   4. EventSourcingBenchmarks - Event store and time travel
///   5. MediatRComparison       - Comparison with MediatR
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
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           Catga Framework Benchmark Suite                    ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Usage: dotnet run -c Release -- --filter <pattern>          ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Available benchmarks:                                       ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  1. Core CQRS Performance:                                   ║");
        Console.WriteLine("║     --filter *Core*                                          ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  2. Business Scenarios:                                      ║");
        Console.WriteLine("║     --filter *Business*                                      ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  3. Concurrency Tests:                                       ║");
        Console.WriteLine("║     --filter *Concurrency*                                   ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  4. Event Sourcing:                                          ║");
        Console.WriteLine("║     --filter *EventSourcing*                                 ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  5. MediatR Comparison:                                      ║");
        Console.WriteLine("║     --filter *MediatR*                                       ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Run all benchmarks:                                         ║");
        Console.WriteLine("║     --filter *                                               ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
