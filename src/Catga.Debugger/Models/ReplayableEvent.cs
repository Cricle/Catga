namespace Catga.Debugger.Models;

/// <summary>Event types that can be captured and replayed</summary>
public enum EventType
{
    StateSnapshot,
    MessageReceived,
    MessageSent,
    StepStarted,
    StepCompleted,
    ExceptionThrown,
    PerformanceMetric,
    VariableChanged
}

/// <summary>Replayable event - core data structure for time-travel</summary>
public sealed class ReplayableEvent
{
    /// <summary>Event unique ID</summary>
    public required string Id { get; init; }

    /// <summary>Correlation ID for flow tracking</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Event type</summary>
    public required EventType Type { get; init; }

    /// <summary>Event timestamp (UTC)</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Event data (polymorphic)</summary>
    public required object Data { get; init; }

    /// <summary>Parent event ID (for causality tracking)</summary>
    public string? ParentEventId { get; init; }

    /// <summary>Service/component name</summary>
    public string? ServiceName { get; init; }

    /// <summary>Message type (Request/Event name)</summary>
    public string? MessageType { get; init; }

    /// <summary>Execution duration in milliseconds</summary>
    public double Duration { get; init; }

    /// <summary>Completion timestamp (UTC) - for calculating duration</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Memory allocated during execution (bytes)</summary>
    public long? MemoryAllocated { get; init; }

    /// <summary>Thread ID where event was captured</summary>
    public int? ThreadId { get; init; }

    /// <summary>CPU time consumed (milliseconds)</summary>
    public double? CpuTime { get; init; }

    /// <summary>Exception message if error occurred</summary>
    public string? Exception { get; init; }

    /// <summary>Additional metadata</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>State snapshot at a specific point in time</summary>
public sealed class StateSnapshot
{
    /// <summary>Snapshot timestamp</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Execution stage (BeforeExecution, AfterExecution, etc.)</summary>
    public required string Stage { get; init; }

    /// <summary>Correlation ID</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Variables and their values</summary>
    public Dictionary<string, object?> Variables { get; set; } = new();

    /// <summary>Call stack</summary>
    public List<CallFrame> CallStack { get; set; } = new();

    /// <summary>Memory state (optional, for deep debugging)</summary>
    public MemoryState? MemoryState { get; set; }
}

/// <summary>Call frame in the call stack</summary>
public sealed class CallFrame
{
    public required string MethodName { get; init; }
    public string? FileName { get; init; }
    public int? LineNumber { get; init; }
    public Dictionary<string, object?>? LocalVariables { get; init; }
}

/// <summary>Memory state snapshot</summary>
public sealed class MemoryState
{
    public long AllocatedBytes { get; init; }
    public long Gen0Collections { get; init; }
    public long Gen1Collections { get; init; }
    public long Gen2Collections { get; init; }
}

/// <summary>Event batch for efficient storage</summary>
public sealed class EventBatch
{
    public required List<ReplayableEvent> Events { get; init; }
    public required DateTime Timestamp { get; init; }
    public int CompressedSize { get; set; }
}

/// <summary>Delta snapshot - only changed variables</summary>
public sealed class DeltaSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required string CorrelationId { get; init; }
    public required string BaseSnapshotId { get; init; }
    public Dictionary<string, object?> ChangedVariables { get; init; } = new();
}

