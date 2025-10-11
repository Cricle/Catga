using System;

namespace Catga.Threading;

/// <summary>
/// Thread pool performance metrics
/// </summary>
public sealed class ThreadPoolMetrics
{
    /// <summary>
    /// Total number of tasks queued
    /// </summary>
    public long TotalTasksQueued { get; internal set; }

    /// <summary>
    /// Total number of tasks completed
    /// </summary>
    public long TotalTasksCompleted { get; internal set; }

    /// <summary>
    /// Total number of tasks failed
    /// </summary>
    public long TotalTasksFailed { get; internal set; }

    /// <summary>
    /// Current number of pending tasks
    /// </summary>
    public int PendingTaskCount { get; internal set; }

    /// <summary>
    /// Number of active worker threads
    /// </summary>
    public int ActiveWorkerCount { get; internal set; }

    /// <summary>
    /// Number of idle worker threads
    /// </summary>
    public int IdleWorkerCount { get; internal set; }

    /// <summary>
    /// Total number of work stealing operations
    /// </summary>
    public long TotalWorkStealCount { get; internal set; }

    /// <summary>
    /// Average task execution time (milliseconds)
    /// </summary>
    public double AverageExecutionTimeMs { get; internal set; }

    /// <summary>
    /// Peak pending task count
    /// </summary>
    public int PeakPendingTaskCount { get; internal set; }

    /// <summary>
    /// Thread pool utilization (0.0 to 1.0)
    /// </summary>
    public double Utilization => ActiveWorkerCount / (double)Math.Max(1, ActiveWorkerCount + IdleWorkerCount);

    /// <summary>
    /// Task completion rate (tasks/second)
    /// </summary>
    public double TaskCompletionRate { get; internal set; }

    /// <summary>
    /// Task failure rate (0.0 to 1.0)
    /// </summary>
    public double TaskFailureRate => TotalTasksCompleted > 0
        ? TotalTasksFailed / (double)(TotalTasksCompleted + TotalTasksFailed)
        : 0.0;

    /// <summary>
    /// Timestamp of last metric update
    /// </summary>
    public DateTimeOffset LastUpdated { get; internal set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Create a snapshot of current metrics
    /// </summary>
    public ThreadPoolMetrics Clone()
    {
        return new ThreadPoolMetrics
        {
            TotalTasksQueued = TotalTasksQueued,
            TotalTasksCompleted = TotalTasksCompleted,
            TotalTasksFailed = TotalTasksFailed,
            PendingTaskCount = PendingTaskCount,
            ActiveWorkerCount = ActiveWorkerCount,
            IdleWorkerCount = IdleWorkerCount,
            TotalWorkStealCount = TotalWorkStealCount,
            AverageExecutionTimeMs = AverageExecutionTimeMs,
            PeakPendingTaskCount = PeakPendingTaskCount,
            TaskCompletionRate = TaskCompletionRate,
            LastUpdated = LastUpdated
        };
    }

    public override string ToString()
    {
        return $"ThreadPoolMetrics {{ " +
               $"Queued: {TotalTasksQueued}, " +
               $"Completed: {TotalTasksCompleted}, " +
               $"Failed: {TotalTasksFailed}, " +
               $"Pending: {PendingTaskCount}, " +
               $"Active: {ActiveWorkerCount}/{ActiveWorkerCount + IdleWorkerCount}, " +
               $"Utilization: {Utilization:P2}, " +
               $"AvgTime: {AverageExecutionTimeMs:F2}ms, " +
               $"CompletionRate: {TaskCompletionRate:F2}/s " +
               $"}}";
    }
}

