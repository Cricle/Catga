using System.Diagnostics.CodeAnalysis;
using Catga.Flow.Dsl;

namespace Catga.Flow;

/// <summary>
/// Serialization-friendly snapshot format for persistence.
/// Used by Redis, NATS, and other distributed stores.
/// </summary>
public record StoredSnapshot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
    string FlowId,
    TState State,
    int[] PositionPath,
    DslFlowStatus Status,
    string? Error,
    string? WaitConditionId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version) where TState : class, IFlowState
{
    /// <summary>
    /// Create from FlowSnapshot.
    /// </summary>
    public StoredSnapshot(FlowSnapshot<TState> snapshot)
        : this(
            snapshot.FlowId,
            snapshot.State,
            snapshot.Position.Path,
            snapshot.Status,
            snapshot.Error,
            snapshot.WaitCondition?.CorrelationId,
            snapshot.CreatedAt,
            snapshot.UpdatedAt,
            snapshot.Version)
    { }

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
