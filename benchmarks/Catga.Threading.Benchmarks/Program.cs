using BenchmarkDotNet.Running;
using Catga.Benchmarks;

Console.WriteLine("🚀 Catga.Threading Benchmarks");
Console.WriteLine("==============================");
Console.WriteLine();
Console.WriteLine("Comparing:");
Console.WriteLine("  1. .NET ThreadPool (Task.Run)");
Console.WriteLine("  2. Catga WorkStealingThreadPool (Task API)");
Console.WriteLine("  3. Catga WorkStealingThreadPool (CatgaTask - Zero Allocation)");
Console.WriteLine();

BenchmarkRunner.Run<ThreadPoolBenchmarks>();
