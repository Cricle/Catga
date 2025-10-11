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
            MinThreads = Environment.ProcessorCount * 2,  // 增加初始线程数
            MaxThreads = Environment.ProcessorCount * 4,
            EnableWorkStealing = true,
            EnableDynamicScaling = false  // 禁用动态扩缩容以获得稳定性能
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
                int sum = 0;
                for (int j = 0; j < 1000; j++)
                {
                    sum += j * j;
                }
                return sum;
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
                int sum = 0;
                for (int j = 0; j < 1000; j++)
                {
                    sum += j * j;
                }
                return sum;
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
                int sum = 0;
                for (int j = 0; j < 1000; j++)
                {
                    sum += j * j;
                }
            });
        }
        // Use UniTask.WhenAll for zero-allocation parallel execution
        await UniTask.WhenAll(tasks);
    }

    // ===== Work with result =====

    [Benchmark]
    [Arguments(IterationCount)]
    public async Task<int> DotNetThreadPool_TaskRun_WithResult(int count)
    {
        var tasks = new Task<int>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = Task.Run(() => {
                int sum = 0;
                for (int j = 0; j < 100; j++)
                {
                    sum += j;
                }
                return sum;
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
                int sum = 0;
                for (int j = 0; j < 100; j++)
                {
                    sum += j;
                }
                return sum;
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
                int sum = 0;
                for (int j = 0; j < 100; j++)
                {
                    sum += j;
                }
                return sum;
            });
        }
        // Use UniTask.WhenAll for zero-allocation parallel execution
        var results = await UniTask.WhenAll(tasks);
        return results.Sum();
    }

    // ===== Priority work (simple comparison) =====

    [Benchmark]
    [Arguments(100)]
    public async Task DotNetThreadPool_PriorityWork(int count)
    {
        var highPriorityTasks = new List<Task>();
        var lowPriorityTasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            lowPriorityTasks.Add(Task.Run(() => {
                int sum = 0;
                for (int j = 0; j < 1000; j++) sum += j;
            }));
            highPriorityTasks.Add(Task.Run(() => {
                int sum = 0;
                for (int j = 0; j < 100; j++) sum += j;
            }));
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
            lowPriorityTasks.Add(_catgaPool!.RunAsync(() => {
                int sum = 0;
                for (int j = 0; j < 1000; j++) sum += j;
            }, priority: 0));
            highPriorityTasks.Add(_catgaPool!.RunAsync(() => {
                int sum = 0;
                for (int j = 0; j < 100; j++) sum += j;
            }, priority: 10));
        }
        await Task.WhenAll(highPriorityTasks.Concat(lowPriorityTasks));
    }
}
