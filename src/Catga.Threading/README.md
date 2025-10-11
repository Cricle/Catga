# Catga.Threading

High-performance thread pool library with better performance than .NET ThreadPool.

## Features

- ✅ **Work-Stealing Algorithm**: Automatic load balancing
- ✅ **Per-Core Queues**: Better cache locality
- ✅ **Instance-Based**: Inject via DI, fully testable
- ✅ **Task Integration**: Full async/await support
- ✅ **Lock-Free**: Zero lock contention
- ✅ **Priority Support**: Critical tasks first

## Quick Start

### 1. Register in DI

```csharp
// Startup.cs or Program.cs
services.AddWorkStealingThreadPool(options =>
{
    options.MinThreads = Environment.ProcessorCount;
    options.EnableWorkStealing = true;
});
```

### 2. Inject and Use

```csharp
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

    // CatgaTask API - Zero allocation, high performance
    public async CatgaTask ProcessDataZeroAllocAsync()
    {
        await _threadPool.RunCatgaAsync(() => 
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

    // Work with result (CatgaTask - Zero Allocation)
    public async CatgaTask<int> CalculateZeroAllocAsync()
    {
        return await _threadPool.RunCatgaAsync(() => 
        {
            return ComputeValue();
        });
    }

    // High priority work
    public async CatgaTask UrgentWorkAsync()
    {
        await _threadPool.RunCatgaAsync(() => 
        {
            // Critical task
        }, priority: 10);
    }

    // Parallel processing with zero allocation
    public async Task ProcessBatchZeroAllocAsync(List<Item> items)
    {
        var tasks = items.Select(item => 
            _threadPool.RunCatgaAsync(() => ProcessItem(item))
        ).ToArray();
        
        // WhenAll for CatgaTask (future enhancement)
        foreach (var task in tasks)
        {
            await task;
        }
    }
}
```

## API Reference

### WorkStealingThreadPool

#### Standard Task API (Compatible with async/await)

```csharp
// Queue work and return Task
Task RunAsync(Action action, int priority = 0, CancellationToken cancellationToken = default)

// Queue work with result and return Task<T>
Task<T> RunAsync<T>(Func<T> func, int priority = 0, CancellationToken cancellationToken = default)

// Queue async work and return Task
Task RunAsync(Func<Task> asyncFunc, int priority = 0, CancellationToken cancellationToken = default)

// Queue async work with result and return Task<T>
Task<T> RunAsync<T>(Func<Task<T>> asyncFunc, int priority = 0, CancellationToken cancellationToken = default)
```

#### CatgaTask API (Zero-Allocation, inspired by UniTask)

```csharp
// Zero-allocation awaitable task (no GC allocation)
CatgaTask RunCatgaAsync(Action action, int priority = 0)

// Zero-allocation awaitable task with result
CatgaTask<T> RunCatgaAsync<T>(Func<T> func, int priority = 0)
```

**Benefits of CatgaTask**:
- ✅ **Zero GC Allocation**: Struct-based design like [UniTask](https://github.com/Cysharp/UniTask)
- ✅ **Full async/await Support**: Can be awaited like Task
- ✅ **Object Pooling**: Automatic pooling of completion sources
- ✅ **High Performance**: Perfect for hot paths in high-throughput scenarios

### Configuration

```csharp
services.AddWorkStealingThreadPool(options =>
{
    options.MinThreads = 8;                           // Minimum worker threads
    options.MaxThreads = 32;                          // Maximum worker threads
    options.ThreadIdleTimeout = TimeSpan.FromSeconds(60);
    options.EnableWorkStealing = true;                // Enable work-stealing
    options.ThreadPriority = ThreadPriority.Normal;   // Thread priority
});
```

## Performance Comparison

| Feature | .NET ThreadPool | Catga WorkStealingThreadPool |
|---------|----------------|------------------------------|
| Queue Type | Single global queue | Per-core local queues |
| Load Balancing | Manual | Automatic work-stealing |
| Cache Locality | Poor | Excellent |
| Priority | ❌ | ✅ |
| Instance-Based | ❌ | ✅ |
| Task Integration | Basic | Full async/await |

## When to Use

### ✅ Use WorkStealingThreadPool for:
- CPU-intensive parallel tasks
- Batch processing with many small tasks
- High-throughput scenarios (1M+ ops/sec)
- When you need priority scheduling

### ❌ Don't Use for:
- Single tasks (just use `Task.Run`)
- Long-running background services (use `BackgroundService`)
- Tasks with blocking I/O

## Examples

### Parallel Data Processing

```csharp
public async Task ProcessBatchAsync(List<DataItem> items)
{
    var tasks = items.Select(item => 
        _threadPool.RunAsync(() => ProcessItem(item))
    );
    
    await Task.WhenAll(tasks);
}
```

### Priority Queue

```csharp
// High priority
await _threadPool.RunAsync(() => CriticalWork(), priority: 10);

// Normal priority
await _threadPool.RunAsync(() => NormalWork(), priority: 0);

// Low priority
await _threadPool.RunAsync(() => BackgroundWork(), priority: -10);
```

### Cancellation Support

```csharp
public async Task ProcessWithCancellationAsync(CancellationToken cancellationToken)
{
    await _threadPool.RunAsync(() => 
    {
        // Work here
    }, cancellationToken: cancellationToken);
}
```

## Architecture

```
┌─────────────────────────────────────────┐
│      WorkStealingThreadPool             │
│  ┌───────────────────────────────────┐  │
│  │   Global Queue (Fallback)         │  │
│  └───────────────────────────────────┘  │
│                                          │
│  ┌──────────┐  ┌──────────┐  ┌────────┐│
│  │ Worker 0 │  │ Worker 1 │  │Worker N││
│  │ ┌──────┐ │  │ ┌──────┐ │  │┌──────┐││
│  │ │Local │ │  │ │Local │ │  ││Local │││
│  │ │Queue │ │  │ │Queue │ │  ││Queue │││
│  │ └──────┘ │  │ └──────┘ │  │└──────┘││
│  │    ↓     │  │    ↓     │  │   ↓    ││
│  │ Execute  │  │ Execute  │  │Execute ││
│  │  Work    │  │  Work    │  │ Work   ││
│  │    ↓     │  │    ↓     │  │   ↓    ││
│  │  Steal ──┼──┼→ Work ←──┼──┼─ Steal ││
│  └──────────┘  └──────────┘  └────────┘│
└─────────────────────────────────────────┘
```

**Work-Stealing**: When a worker's local queue is empty, it steals tasks from other busy workers.

## License

MIT

