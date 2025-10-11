namespace Catga.Threading;

/// <summary>
/// High-performance thread pool interface
/// </summary>
public interface IThreadPool : IDisposable
{
    /// <summary>
    /// Gets the number of worker threads in the pool
    /// </summary>
    int WorkerCount { get; }

    /// <summary>
    /// Gets the number of pending work items
    /// </summary>
    int PendingWorkCount { get; }

    /// <summary>
    /// Gets the total number of completed work items
    /// </summary>
    long CompletedWorkCount { get; }

    /// <summary>
    /// Queues a work item for execution
    /// </summary>
    bool QueueWorkItem(IWorkItem workItem);

    /// <summary>
    /// Queues an action for execution
    /// </summary>
    bool QueueWorkItem(Action action, int priority = 0);

    /// <summary>
    /// Queues an async operation for execution
    /// </summary>
    bool QueueWorkItem(Func<Task> asyncAction, int priority = 0);
}

/// <summary>
/// Configuration options for the thread pool
/// </summary>
public sealed class ThreadPoolOptions
{
    /// <summary>
    /// Minimum number of worker threads (default: CPU count)
    /// </summary>
    public int MinThreads { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum number of worker threads (default: CPU count * 4)
    /// </summary>
    public int MaxThreads { get; set; } = Environment.ProcessorCount * 4;

    /// <summary>
    /// Thread idle timeout before termination (default: 60 seconds)
    /// </summary>
    public TimeSpan ThreadIdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Enable work-stealing algorithm (default: true)
    /// </summary>
    public bool EnableWorkStealing { get; set; } = true;

    /// <summary>
    /// Thread priority for worker threads (default: Normal)
    /// </summary>
    public ThreadPriority ThreadPriority { get; set; } = ThreadPriority.Normal;

    /// <summary>
    /// Use dedicated threads (no ThreadPool) (default: true)
    /// </summary>
    public bool UseDedicatedThreads { get; set; } = true;
}

