namespace Catga.StateMachine;

/// <summary>
/// State machine for workflow orchestration - simpler than Saga
/// </summary>
public interface IStateMachine<TState, TData> 
    where TState : struct, Enum
    where TData : class
{
    /// <summary>
    /// Current state
    /// </summary>
    TState CurrentState { get; }

    /// <summary>
    /// State data
    /// </summary>
    TData Data { get; }

    /// <summary>
    /// Transition to new state
    /// </summary>
    ValueTask TransitionAsync(
        TState newState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete the state machine
    /// </summary>
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Compensate (rollback) the state machine
    /// </summary>
    ValueTask CompensateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// State machine instance
/// </summary>
public sealed record StateMachineInstance<TState, TData> 
    where TState : struct, Enum
    where TData : class
{
    public required string Id { get; init; }
    public required TState CurrentState { get; init; }
    public required TData Data { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsCompleted { get; init; }
    public bool IsCompensated { get; init; }
}

