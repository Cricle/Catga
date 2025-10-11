using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.Threading;
using Cysharp.Threading.Tasks;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark comparing .NET ThreadPool vs Catga WorkStealingThreadPool
/// Tests both standard Task API and zero-allocation UniTask API
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
    public async Task DotNetThreadPool_TaskRun_CpuBound(int count)
    {
        var tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = Task.Run(() => {
                // Simulate CPU-bound work
                Thread.SpinWait(100);
            });
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task CatgaThreadPool_RunAsync_CpuBound(int count)
    {
        var tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = _catgaPool!.RunAsync(() => {
                // Simulate CPU-bound work
                Thread.SpinWait(100);
            });
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async UniTask CatgaThreadPool_RunUniTaskAsync_CpuBound(int count)
    {
        var tasks = new UniTask[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = _catgaPool!.RunUniTaskAsync(() => {
                // Simulate CPU-bound work
                Thread.SpinWait(100);
            });
        }
        // Use UniTask.WhenAll for zero-allocation parallel execution
        await UniTask.WhenAll(tasks);
    }

    // ===== Work with result =====

    [Benchmark(Baseline = true)]
    [Arguments(IterationCount)]
    public async Task<int> DotNetThreadPool_TaskRun_WithResult(int count)
    {
        var tasks = new Task<int>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = Task.Run(() => {
                Thread.SpinWait(10);
                return 1;
            });
        }
        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task<int> CatgaThreadPool_RunAsync_WithResult(int count)
    {
        var tasks = new Task<int>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = _catgaPool!.RunAsync(() => {
                Thread.SpinWait(10);
                return 1;
            });
        }
        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    [Benchmark]
    [Arguments(IterationCount)]
    public async UniTask<int> CatgaThreadPool_RunUniTaskAsync_WithResult(int count)
    {
        var tasks = new UniTask<int>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = _catgaPool!.RunUniTaskAsync(() => {
                Thread.SpinWait(10);
                return 1;
            });
        }
        // Use UniTask.WhenAll for zero-allocation parallel execution
        var results = await UniTask.WhenAll(tasks);
        return results.Sum();
    }

    // ===== Priority work (simple comparison) =====

    [Benchmark(Baseline = true)]
    [Arguments(100)]
    public async Task DotNetThreadPool_PriorityWork(int count)
    {
        var highPriorityTasks = new List<Task>();
        var lowPriorityTasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            lowPriorityTasks.Add(Task.Run(() => Thread.SpinWait(100)));
            highPriorityTasks.Add(Task.Run(() => Thread.SpinWait(10)));
        }
        await Task.WhenAll(highPriorityTasks.Concat(lowPriorityTasks));
    }

    [Benchmark]
    [Arguments(100)]
    public async Task CatgaThreadPool_PriorityWork(int count)
    {
        var highPriorityTasks = new List<Task>();
        var lowPriorityTasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            lowPriorityTasks.Add(_catgaPool!.RunAsync(() => Thread.SpinWait(100), priority: 0));
            highPriorityTasks.Add(_catgaPool!.RunAsync(() => Thread.SpinWait(10), priority: 10));
        }
        await Task.WhenAll(highPriorityTasks.Concat(lowPriorityTasks));
    }
}
