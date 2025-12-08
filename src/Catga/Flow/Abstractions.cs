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

#region Flow Position

/// <summary>
/// Flow execution position supporting nested branches.
/// </summary>
public record FlowPosition
{
    /// <summary>Path through the flow. Each element is a step index within current scope.</summary>
    public int[] Path { get; init; }

    public FlowPosition(int[] path) => Path = path ?? [0];

    /// <summary>Create initial position at step 0.</summary>
    public static FlowPosition Initial => new([0]);

    /// <summary>Current step index (last element of path).</summary>
    public int CurrentIndex => Path.Length > 0 ? Path[^1] : 0;

    /// <summary>Depth in branch hierarchy (0 = top level).</summary>
    public int Depth => Path.Length - 1;

    /// <summary>Whether currently inside a branch.</summary>
    public bool IsInBranch => Path.Length > 1;

    /// <summary>Advance to next step in current scope.</summary>
    public FlowPosition Advance() => Path.Length == 0
        ? new([1])
        : new([.. Path[..^1], Path[^1] + 1]);

    /// <summary>Enter a branch at given step index.</summary>
    public FlowPosition EnterBranch(int stepIndex) => new([.. Path, stepIndex]);

    /// <summary>Exit current branch scope.</summary>
    public FlowPosition ExitBranch() => Path.Length <= 1
        ? this
        : new(Path[..^1]);

    /// <summary>Get parent position (one level up).</summary>
    public FlowPosition Parent => ExitBranch();
}

/// <summary>Branch type for If/Switch.</summary>
public enum BranchType { Then, Else, Case, Default }

#endregion

#region Flow Snapshot

/// <summary>
/// Persistent flow snapshot with position support for branching.
/// </summary>
public record FlowSnapshot<TState> where TState : class, IFlowState
{
    public required string FlowId { get; init; }
    public required TState State { get; init; }
    public FlowPosition Position { get; init; } = FlowPosition.Initial;
    public DslFlowStatus Status { get; init; } = DslFlowStatus.Pending;
    public string? Error { get; init; }
    public WaitCondition? WaitCondition { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    public int Version { get; init; } = 1;

    /// <summary>Create a new snapshot with updated position.</summary>
    public FlowSnapshot<TState> WithPosition(FlowPosition position)
        => this with { Position = position, UpdatedAt = DateTime.UtcNow };

    /// <summary>Create a new snapshot with advanced position.</summary>
    public FlowSnapshot<TState> Advance()
        => WithPosition(Position.Advance());

    /// <summary>Create snapshot from legacy parameters (for backward compatibility).</summary>
    public static FlowSnapshot<TState> Create(
        string flowId,
        TState state,
        int currentStep,
        DslFlowStatus status,
        string? error = null,
        WaitCondition? waitCondition = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null,
        int version = 1) => new()
    {
        FlowId = flowId,
        State = state,
        Position = new FlowPosition([currentStep]),
        Status = status,
        Error = error,
        WaitCondition = waitCondition,
        CreatedAt = createdAt ?? DateTime.UtcNow,
        UpdatedAt = updatedAt ?? DateTime.UtcNow,
        Version = version
    };
}

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
    Pending = 0,
    Running = 1,
    Suspended = 2,
    Compensating = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
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
