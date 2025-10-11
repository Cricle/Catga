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
    private readonly IThreadPool _threadPool;

    public MyService(IThreadPool threadPool)
    {
        _threadPool = threadPool;
    }

    // Sync work with Task result
    public async Task ProcessDataAsync()
    {
        await _threadPool.RunAsync(() => 
        {
            // CPU-intensive work
            var result = HeavyComputation();
        });
    }

    // Async work with result
    public async Task<int> CalculateAsync()
    {
        return await _threadPool.RunAsync(async () =>
        {
            // Some async operation
            await Task.Delay(100);
            return 42;
        });
    }

    // Work with result
    public async Task<string> GetResultAsync()
    {
        return await _threadPool.RunAsync(() => 
        {
            return "Hello from thread pool!";
        });
    }

    // High priority work
    public async Task UrgentWorkAsync()
    {
        await _threadPool.RunAsync(() => 
        {
            // Critical task
        }, priority: 10);
    }
}
```

## API Reference

### WorkStealingThreadPool

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

