using BenchmarkDotNet.Running;

namespace CatCat.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("  Catga 性能基准测试");
        Console.WriteLine("===========================================");
        Console.WriteLine();
        Console.WriteLine("测试项目:");
        Console.WriteLine("  1. CQRS 性能测试 (命令、查询、事件)");
        Console.WriteLine("  2. CatGa 性能测试 (分布式事务)");
        Console.WriteLine("  3. 并发控制性能测试");
        Console.WriteLine();
        Console.WriteLine("开始运行基准测试...");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        // 运行所有基准测试
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
