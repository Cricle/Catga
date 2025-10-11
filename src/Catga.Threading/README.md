# Catga.Threading

A high-performance, work-stealing thread pool library for .NET, with direct **UniTask** integration for zero-allocation asynchronous operations.

## Features

- **Work-Stealing Thread Pool**: Optimized for CPU-bound tasks, featuring per-core local queues and a global queue for efficient load balancing.
- **IO Thread Pool**: Designed for IO-bound asynchronous operations using `System.Threading.Channels`.
- **Zero-Allocation Async/Await (UniTask)**: Direct integration with [Cysharp/UniTask](https://github.com/Cysharp/UniTask) for high-performance, GC-free asynchronous programming.
- **Task Integration**: Seamlessly integrates with .NET's `Task` and `Task<T>` for standard async/await patterns.
- **Dependency Injection**: Easy setup and management of thread pool instances via `Microsoft.Extensions.DependencyInjection`.
- **Priority Scheduling**: Support for prioritizing work items.
- **Cancellation Support**: Integrated `CancellationToken` for managing task lifecycle.
- **Exception Propagation**: Proper handling and propagation of exceptions.

## Getting Started

### 1. Installation

```bash
dotnet add package Catga.Threading
```

### 2. Configuration (Program.cs or Startup.cs)

Register the thread pools with your DI container:

```csharp
using Catga.Threading.DependencyInjection;
using Catga.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCatgaThreading(options =>
{
    options.MinThreads = Environment.ProcessorCount;
    options.MaxThreads = Environment.ProcessorCount * 2;
    options.EnableWorkStealing = true;
});

var app = builder.Build();
app.Run();
```

### 3. Inject and Use

```csharp
using Catga.Threading;
using Cysharp.Threading.Tasks; // UniTask

public class MyService
{
    private readonly WorkStealingThreadPool _threadPool;

    public MyService(WorkStealingThreadPool threadPool)
    {
        _threadPool = threadPool;
    }

    // Standard Task API - Works with existing code
    public async Task ProcessDataAsync()
    {
        await _threadPool.RunAsync(() => 
        {
            // CPU-intensive work
            HeavyComputation();
        });
    }

    // UniTask API - Zero allocation, high performance
    public async UniTask ProcessDataZeroAllocAsync()
    {
        await _threadPool.RunUniTaskAsync(() => 
        {
            // CPU-intensive work - zero GC allocation!
            HeavyComputation();
        });
    }

    // Work with result (Standard Task)
    public async Task<int> CalculateAsync()
    {
        return await _threadPool.RunAsync(() => 
        {
            return ComputeValue();
        });
    }

    // Work with result (UniTask - Zero Allocation)
    public async UniTask<int> CalculateZeroAllocAsync()
    {
        return await _threadPool.RunUniTaskAsync(() => 
        {
            return ComputeValue();
        });
    }

    // High priority work
    public async UniTask UrgentWorkAsync()
    {
        await _threadPool.RunUniTaskAsync(() => 
        {
            // Critical task
        }, priority: 10);
    }

    // Parallel processing with zero allocation
    public async Task ProcessBatchZeroAllocAsync(List<Item> items)
    {
        var tasks = items.Select(item => 
            _threadPool.RunUniTaskAsync(() => ProcessItem(item))
        ).ToArray();
        
        // Use UniTask.WhenAll for zero-allocation parallel execution
        await UniTask.WhenAll(tasks);
    }

    private void HeavyComputation() { /* ... */ }
    private int ComputeValue() { return 42; }
    private void ProcessItem(Item item) { /* ... */ }
    public class Item { }
}
```

## API Comparison

### Standard Task API (Compatible with existing code)

```csharp
// For existing code, return Task
Task RunAsync(Action action, int priority = 0, CancellationToken ct = default)
Task<T> RunAsync<T>(Func<T> func, int priority = 0, CancellationToken ct = default)
```

### UniTask API (Zero-Allocation, from Cysharp.Threading.Tasks)

```csharp
// For high-performance hot paths, return UniTask
UniTask RunUniTaskAsync(Action action, int priority = 0)
UniTask<T> RunUniTaskAsync<T>(Func<T> func, int priority = 0)
```

### Performance Comparison

```csharp
// ❌ Standard Task - allocates Task object
for (int i = 0; i < 1000000; i++)
{
    await pool.RunAsync(() => Work());  // GC pressure!
}

// ✅ UniTask - zero allocation (from UniTask library)
for (int i = 0; i < 1000000; i++)
{
    await pool.RunUniTaskAsync(() => Work());  // No GC!
}
```

## Why Use Catga.Threading over System.Threading.ThreadPool?

| Feature | System.Threading.ThreadPool | Catga.Threading |
|---------|----------------------------|-----------------|
| **Work-Stealing** | ❌ No | ✅ Yes - better load balancing |
| **Priority Support** | ❌ No | ✅ Yes - high priority tasks first |
| **Isolation** | ❌ Global shared pool | ✅ Dedicated isolated pools |
| **Zero-Allocation** | ❌ Task allocation | ✅ UniTask integration |
| **Thread Injection** | ⚠️ 500ms delay | ✅ Immediate scaling |
| **Per-Core Optimization** | ❌ No | ✅ Yes - better cache locality |

## Performance Benchmarks

See `benchmarks/Catga.Threading.Benchmarks` for detailed performance comparisons:

```bash
cd benchmarks/Catga.Threading.Benchmarks
dotnet run -c Release
```

**Expected Results:**
- **Zero GC allocation** with UniTask API
- **Faster task creation** (~50ns vs ~100ns)
- **Better throughput** for CPU-bound parallel tasks
- **Immediate thread scaling** (no 500ms injection delay)

## Advanced Usage

### Dedicated Pools for Different Workloads

```csharp
// CPU-intensive pool
var cpuPool = new WorkStealingThreadPool(new ThreadPoolOptions
{
    MinThreads = Environment.ProcessorCount,
    MaxThreads = Environment.ProcessorCount * 2,
    EnableWorkStealing = true
});

// IO-bound pool
var ioPool = new IOThreadPool(maxConcurrency: 100);

// No interference between pools!
await cpuPool.RunUniTaskAsync(() => CpuHeavyWork());
await ioPool.RunAsync(() => IoWork());
```

### Priority Scheduling

```csharp
// High priority tasks execute first
await pool.RunUniTaskAsync(() => CriticalTask(), priority: 10);
await pool.RunUniTaskAsync(() => NormalTask(), priority: 5);
await pool.RunUniTaskAsync(() => BackgroundTask(), priority: 1);
```

## References

- [Cysharp/UniTask](https://github.com/Cysharp/UniTask) - Zero allocation async/await for Unity and .NET
- [Work-Stealing Algorithm](https://en.wikipedia.org/wiki/Work_stealing) - Efficient task scheduling

## License

MIT License - See LICENSE file for details
