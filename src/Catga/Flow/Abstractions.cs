using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow.Dsl;

#region Flow State Interface

/// <summary>
/// Interface for flow state with change tracking.
/// Implemented by source generator for [FlowState] attributed classes.
/// </summary>
public interface IFlowState
{
    /// <summary>Flow instance ID (infrastructure, not tracked).</summary>
    string? FlowId { get; set; }

    /// <summary>Whether any field has changed since last ClearChanges().</summary>
    bool HasChanges { get; }

    /// <summary>Get bitmask of changed fields.</summary>
    int GetChangedMask();

    /// <summary>Check if specific field has changed.</summary>
    bool IsFieldChanged(int fieldIndex);

    /// <summary>Clear all change flags.</summary>
    void ClearChanges();

    /// <summary>Mark a field as changed.</summary>
    void MarkChanged(int fieldIndex);

    /// <summary>Get names of changed fields.</summary>
    IEnumerable<string> GetChangedFieldNames();
}

#endregion

#region Flow Snapshot

/// <summary>
/// Persistent flow snapshot.
/// </summary>
public record FlowSnapshot<TState>(
    string FlowId,
    TState State,
    int CurrentStep,
    DslFlowStatus Status,
    string? Error,
    WaitCondition? WaitCondition,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version) where TState : class, IFlowState;

#endregion

#region Flow Store Interface

/// <summary>
/// Flow state persistence interface for DSL flows.
/// </summary>
public interface IDslFlowStore
{
    /// <summary>Create a new flow snapshot.</summary>
    Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState;

    /// <summary>Get flow snapshot by ID.</summary>
    Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(string flowId, CancellationToken ct = default)
        where TState : class, IFlowState;

    /// <summary>Update flow snapshot. Only updates if state.HasChanges.</summary>
    Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState;

    /// <summary>Delete flow snapshot.</summary>
    Task<bool> DeleteAsync(string flowId, CancellationToken ct = default);

    /// <summary>Set wait condition for WhenAll/WhenAny.</summary>
    Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default);

    /// <summary>Get wait condition.</summary>
    Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default);

    /// <summary>Update wait condition.</summary>
    Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default);

    /// <summary>Clear wait condition.</summary>
    Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default);

    /// <summary>Get timed out wait conditions.</summary>
    Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default);
}

#endregion

#region Flow Interface

/// <summary>
/// Flow executor interface.
/// </summary>
public interface IFlow<TState> where TState : class, IFlowState
{
    /// <summary>Run flow with initial state.</summary>
    Task<DslFlowResult<TState>> RunAsync(TState state, CancellationToken ct = default);

    /// <summary>Resume flow from stored state.</summary>
    Task<DslFlowResult<TState>> ResumeAsync(string flowId, CancellationToken ct = default);

    /// <summary>Get flow status.</summary>
    Task<FlowSnapshot<TState>?> GetAsync(string flowId, CancellationToken ct = default);

    /// <summary>Cancel flow.</summary>
    Task<bool> CancelAsync(string flowId, CancellationToken ct = default);
}

#endregion

#region Flow Result

/// <summary>
/// DSL Flow execution result.
/// </summary>
public readonly record struct DslFlowResult<TState>(
    bool IsSuccess,
    TState? State,
    DslFlowStatus Status,
    int CompletedSteps,
    string? Error = null) where TState : class
{
    public string? FlowId { get; init; }

    public static DslFlowResult<TState> Success(TState state, DslFlowStatus status, int steps = 0)
        => new(true, state, status, steps);

    public static DslFlowResult<TState> Failure(DslFlowStatus status, string? error, int steps = 0)
        => new(false, default, status, steps, error);
}

#endregion

#region Wait Condition

/// <summary>
/// Wait condition for WhenAll/WhenAny.
/// </summary>
public record WaitCondition
{
    public required string CorrelationId { get; init; }
    public required WaitType Type { get; init; }
    public required int ExpectedCount { get; init; }
    public int CompletedCount { get; set; }
    public required TimeSpan Timeout { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string FlowId { get; init; }
    public required string FlowType { get; init; }
    public required int Step { get; init; }
    public bool CancelOthers { get; init; }
    public List<string> ChildFlowIds { get; init; } = [];
    public List<FlowCompletedEventData> Results { get; init; } = [];
}

/// <summary>
/// Wait type for WhenAll/WhenAny.
/// </summary>
public enum WaitType
{
    All,
    Any
}

/// <summary>
/// Flow completed event data for WhenAll/WhenAny coordination.
/// </summary>
public record FlowCompletedEventData
{
    public required string FlowId { get; init; }
    public string? ParentCorrelationId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public object? Result { get; init; }
}

#endregion

#region Flow Status

/// <summary>
/// DSL Flow execution status.
/// </summary>
public enum DslFlowStatus : byte
{
    Running = 0,
    Suspended = 1,
    Compensating = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

#endregion

#region DSL Flow Options

/// <summary>
/// DSL Flow configuration options.
/// </summary>
public class DslFlowOptions
{
    /// <summary>Key prefix for storage.</summary>
    public string KeyPrefix { get; set; } = "flow";

    /// <summary>Default timeout for steps.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Default retry count.</summary>
    public int DefaultRetries { get; set; } = 0;

    /// <summary>Enable distributed tracing.</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Enable metrics collection.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Node ID for distributed scenarios.</summary>
    public string? NodeId { get; set; }

    /// <summary>Heartbeat interval.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Claim timeout for abandoned flows.</summary>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

#endregion

#region Attributes

/// <summary>
/// Marks a class for flow state source generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FlowStateAttribute : Attribute
{
}

/// <summary>
/// Marks a property to be excluded from flow state change tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FlowStateIgnoreAttribute : Attribute
{
}

#endregion
