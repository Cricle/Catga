namespace Catga.Flow.Dsl;

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

    /// <summary>Schedule ID when flow is suspended for delayed resume.</summary>
    public string? ScheduleId { get; init; }

    public static DslFlowResult<TState> Success(TState state, DslFlowStatus status, int steps = 0)
        => new(true, state, status, steps);

    public static DslFlowResult<TState> Failure(DslFlowStatus status, string? error, int steps = 0)
        => new(false, default, status, steps, error);

    public static DslFlowResult<TState> Failure(TState? state, DslFlowStatus status, string? error, int steps = 0)
        => new(false, state, status, steps, error);

    public static DslFlowResult<TState> Suspended(TState state, int steps, string scheduleId)
        => new(true, state, DslFlowStatus.Suspended, steps) { ScheduleId = scheduleId };
}
