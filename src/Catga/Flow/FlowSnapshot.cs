namespace Catga.Flow.Dsl;

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
