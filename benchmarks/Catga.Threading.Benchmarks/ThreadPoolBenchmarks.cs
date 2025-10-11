using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.Threading;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark comparing .NET ThreadPool vs Catga WorkStealingThreadPool
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[MarkdownExporter]
public class ThreadPoolBenchmarks
{
    private WorkStealingThreadPool? _catgaPool;
    private const int IterationCount = 1000;

    [GlobalSetup]
    public void Setup()
    {
        _catgaPool = new WorkStealingThreadPool(new ThreadPoolOptions
        {
            MinThreads = Environment.ProcessorCount,
            MaxThreads = Environment.ProcessorCount * 2,
            EnableWorkStealing = true
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _catgaPool?.Dispose();
    }

    // ===== CPU-bound work =====

    [Benchmark(Baseline = true)]
    [Arguments(IterationCount)]
    public async Task DotNetThreadPool_CpuBound(int iterations)
    {
        var tasks = new Task[iterations];
        for (int i = 0; i < iterations; i++)
        {
            tasks[i] = Task.Run(() => CpuBoundWork());
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task CatgaThreadPool_CpuBound(int iterations)
    {
        var tasks = new Task[iterations];
        for (int i = 0; i < iterations; i++)
        {
            tasks[i] = _catgaPool!.RunAsync(() => CpuBoundWork());
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task CatgaThreadPool_CpuBound_ZeroAlloc(int iterations)
    {
        var tasks = new CatgaTask[iterations];
        for (int i = 0; i < iterations; i++)
        {
            tasks[i] = _catgaPool!.RunCatgaAsync(() => CpuBoundWork());
        }
        
        // Await all CatgaTasks
        foreach (var task in tasks)
        {
            await task;
        }
    }

    // ===== Work with result =====

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task DotNetThreadPool_WithResult(int iterations)
    {
        var tasks = new Task<int>[iterations];
        for (int i = 0; i < iterations; i++)
        {
            int value = i;
            tasks[i] = Task.Run(() => ComputeValue(value));
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task CatgaThreadPool_WithResult(int iterations)
    {
        var tasks = new Task<int>[iterations];
        for (int i = 0; i < iterations; i++)
        {
            int value = i;
            tasks[i] = _catgaPool!.RunAsync(() => ComputeValue(value));
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task CatgaThreadPool_WithResult_ZeroAlloc(int iterations)
    {
        var tasks = new CatgaTask<int>[iterations];
        for (int i = 0; i < iterations; i++)
        {
            int value = i;
            tasks[i] = _catgaPool!.RunCatgaAsync(() => ComputeValue(value));
        }
        
        // Await all CatgaTasks
        long sum = 0;
        foreach (var task in tasks)
        {
            sum += await task;
        }
    }

    // ===== Priority work =====

    [Benchmark]
    [Arguments(100)]
    public async Task CatgaThreadPool_PriorityWork(int iterations)
    {
        var tasks = new Task[iterations];
        for (int i = 0; i < iterations; i++)
        {
            int priority = i % 10;
            tasks[i] = _catgaPool!.RunAsync(() => CpuBoundWork(), priority);
        }
        await Task.WhenAll(tasks);
    }

    // ===== Helper methods =====

    private static void CpuBoundWork()
    {
        // Simulate some CPU work
        double result = 0;
        for (int i = 0; i < 100; i++)
        {
            result += Math.Sqrt(i);
        }
    }

    private static int ComputeValue(int input)
    {
        // Simulate computation
        int result = input;
        for (int i = 0; i < 100; i++)
        {
            result += (int)Math.Sqrt(i);
        }
        return result;
    }
}

