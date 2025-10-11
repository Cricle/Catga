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

    /// <summary>
    /// Enable dynamic thread scaling (default: true)
    /// Automatically adjusts thread count based on workload
    /// </summary>
    public bool EnableDynamicScaling { get; set; } = true;

    /// <summary>
    /// Interval for checking and adjusting thread count (default: 1 second)
    /// Faster checks for better responsiveness to load spikes
    /// </summary>
    public TimeSpan ScalingCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Threshold for aggressive scale-up: pending tasks per thread (default: 20)
    /// Triggers immediate multi-thread addition for sudden spikes
    /// </summary>
    public int AggressiveScaleUpThreshold { get; set; } = 20;

    /// <summary>
    /// Threshold for normal scale-up: pending tasks per thread (default: 10)
    /// Triggers gradual single-thread addition
    /// </summary>
    public int NormalScaleUpThreshold { get; set; } = 10;

    /// <summary>
    /// Threshold for scale-down: idle ratio (default: 0.7)
    /// If idle threads / total threads > this value, consider removing threads
    /// </summary>
    public double ScaleDownThreshold { get; set; } = 0.7;

    /// <summary>
    /// Minimum time a thread must be idle before removal (default: 60 seconds)
    /// Longer wait prevents premature removal during periodic spikes
    /// </summary>
    public TimeSpan MinIdleTimeBeforeRemoval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Cooldown period after scale-up before allowing scale-down (default: 30 seconds)
    /// Prevents thrashing during oscillating workloads
    /// </summary>
    public TimeSpan ScaleDownCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum threads to add per scale-up operation (default: 4)
    /// Limits aggressive scaling to prevent over-allocation
    /// </summary>
    public int MaxThreadsPerScaleUp { get; set; } = 4;

    /// <summary>
    /// History window for load pattern detection (default: 60 seconds)
    /// Used to detect periodic spikes and adjust strategy
    /// </summary>
    public TimeSpan LoadHistoryWindow { get; set; } = TimeSpan.FromSeconds(60);
}

