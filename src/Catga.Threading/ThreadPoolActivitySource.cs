using System.Diagnostics;

namespace Catga.Threading;

/// <summary>
/// ActivitySource for thread pool telemetry (OpenTelemetry compatible)
/// </summary>
public static class ThreadPoolActivitySource
{
    /// <summary>
    /// Activity source name for thread pool operations
    /// </summary>
    public const string SourceName = "Catga.Threading";

    /// <summary>
    /// Activity source version
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// Shared ActivitySource instance for thread pool telemetry
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    // Activity names
    public const string TaskExecutionActivity = "threadpool.task.execute";
    public const string WorkStealActivity = "threadpool.worksteal";
    public const string QueueTaskActivity = "threadpool.task.queue";

    // Tag names
    public const string TaskPriorityTag = "task.priority";
    public const string WorkerIdTag = "worker.id";
    public const string QueueSizeTag = "queue.size";
    public const string ExecutionTimeTag = "execution.time.ms";
    public const string TaskStatusTag = "task.status";
    public const string ExceptionTypeTag = "exception.type";
    public const string SourceWorkerTag = "source.worker.id";
    public const string TargetWorkerTag = "target.worker.id";
    public const string StolenCountTag = "stolen.count";
}

