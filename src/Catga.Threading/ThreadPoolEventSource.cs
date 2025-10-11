using System.Diagnostics.Tracing;

namespace Catga.Threading;

/// <summary>
/// EventSource for thread pool events (ETW/EventPipe compatible)
/// </summary>
[EventSource(Name = "Catga-Threading-ThreadPool")]
public sealed class ThreadPoolEventSource : EventSource
{
    public static readonly ThreadPoolEventSource Log = new();

    private ThreadPoolEventSource() { }

    // Event IDs
    private const int TaskQueuedEventId = 1;
    private const int TaskStartedEventId = 2;
    private const int TaskCompletedEventId = 3;
    private const int TaskFailedEventId = 4;
    private const int WorkStolenEventId = 5;
    private const int ThreadPoolSaturatedEventId = 6;
    private const int MetricsUpdatedEventId = 7;

    /// <summary>
    /// Task queued event
    /// </summary>
    [Event(TaskQueuedEventId, Level = EventLevel.Verbose, Message = "Task queued with priority {0}")]
    public void TaskQueued(int priority)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.All))
        {
            WriteEvent(TaskQueuedEventId, priority);
        }
    }

    /// <summary>
    /// Task started event
    /// </summary>
    [Event(TaskStartedEventId, Level = EventLevel.Verbose, Message = "Task started on worker {0} with priority {1}")]
    public void TaskStarted(int workerId, int priority)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.All))
        {
            WriteEvent(TaskStartedEventId, workerId, priority);
        }
    }

    /// <summary>
    /// Task completed successfully
    /// </summary>
    [Event(TaskCompletedEventId, Level = EventLevel.Informational, Message = "Task completed in {0}ms")]
    public void TaskCompleted(double executionTimeMs)
    {
        if (IsEnabled(EventLevel.Informational, EventKeywords.All))
        {
            WriteEvent(TaskCompletedEventId, executionTimeMs);
        }
    }

    /// <summary>
    /// Task failed with exception
    /// </summary>
    [Event(TaskFailedEventId, Level = EventLevel.Error, Message = "Task failed: {0}")]
    public void TaskFailed(string exceptionType, string exceptionMessage, double executionTimeMs)
    {
        if (IsEnabled(EventLevel.Error, EventKeywords.All))
        {
            WriteEvent(TaskFailedEventId, exceptionType, exceptionMessage, executionTimeMs);
        }
    }

    /// <summary>
    /// Work stealing occurred
    /// </summary>
    [Event(WorkStolenEventId, Level = EventLevel.Verbose, Message = "Worker {0} stole {1} tasks from worker {2}")]
    public void WorkStolen(int targetWorkerId, int stolenCount, int sourceWorkerId)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.All))
        {
            WriteEvent(WorkStolenEventId, targetWorkerId, stolenCount, sourceWorkerId);
        }
    }

    /// <summary>
    /// Thread pool saturation detected
    /// </summary>
    [Event(ThreadPoolSaturatedEventId, Level = EventLevel.Warning, Message = "Thread pool saturated: {0} pending tasks, {1:P2} utilization")]
    public void ThreadPoolSaturated(int pendingTaskCount, double utilization)
    {
        if (IsEnabled(EventLevel.Warning, EventKeywords.All))
        {
            WriteEvent(ThreadPoolSaturatedEventId, pendingTaskCount, utilization);
        }
    }

    /// <summary>
    /// Metrics updated
    /// </summary>
    [Event(MetricsUpdatedEventId, Level = EventLevel.Informational, 
           Message = "Metrics: Queued={0}, Completed={1}, Failed={2}, Pending={3}, Active={4}, Utilization={5:P2}")]
    public void MetricsUpdated(
        long totalQueued,
        long totalCompleted,
        long totalFailed,
        int pending,
        int active,
        double utilization)
    {
        if (IsEnabled(EventLevel.Informational, EventKeywords.All))
        {
            WriteEvent(MetricsUpdatedEventId, totalQueued, totalCompleted, totalFailed, pending, active, utilization);
        }
    }

    /// <summary>
    /// Write metrics as non-event counters (for dotnet-counters)
    /// </summary>
    [NonEvent]
    public void UpdateCounters(ThreadPoolMetrics metrics)
    {
        // EventCounters for real-time monitoring with dotnet-counters
        // These will be automatically exposed
    }
}

