using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Catga.Flow.Dsl;

namespace Catga.Flow;

/// <summary>
/// Serialization-friendly snapshot format for persistence.
/// Used by Redis, NATS, and other distributed stores.
/// </summary>
public record StoredSnapshot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState> where TState : class, IFlowState
{
    public string FlowId { get; init; }
    public string TypeName { get; init; }
    public TState State { get; init; }
    public int[] PositionPath { get; init; }
    public DslFlowStatus Status { get; init; }
    public string? Error { get; init; }
    public string? WaitConditionId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int Version { get; init; }

    [JsonConstructor]
    public StoredSnapshot(
        string flowId,
        string typeName,
        TState state,
        int[] positionPath,
        DslFlowStatus status,
        string? error,
        string? waitConditionId,
        DateTime createdAt,
        DateTime updatedAt,
        int version)
    {
        FlowId = flowId;
        TypeName = typeName;
        State = state;
        PositionPath = positionPath;
        Status = status;
        Error = error;
        WaitConditionId = waitConditionId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Version = version;
    }

    /// <summary>
    /// Create from FlowSnapshot.
    /// </summary>
    public StoredSnapshot(FlowSnapshot<TState> snapshot)
    {
        FlowId = snapshot.FlowId;
        TypeName = typeof(TState).FullName ?? typeof(TState).Name;
        State = snapshot.State;
        PositionPath = snapshot.Position.Path;
        Status = snapshot.Status;
        Error = snapshot.Error;
        WaitConditionId = snapshot.WaitCondition?.CorrelationId;
        CreatedAt = snapshot.CreatedAt;
        UpdatedAt = snapshot.UpdatedAt;
        Version = snapshot.Version;
    }

    /// <summary>
    /// Convert back to FlowSnapshot.
    /// </summary>
    public FlowSnapshot<TState> ToSnapshot() => new()
    {
        FlowId = FlowId,
        State = State,
        Position = new FlowPosition(PositionPath),
        Status = Status,
        Error = Error,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Version = Version
    };
}

/// <summary>
/// Metadata-only snapshot format for query operations.
/// Used when we don't need to deserialize the full state.
/// </summary>
public record StoredSnapshotMetadata(
    string FlowId,
    string TypeName,
    DslFlowStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);
