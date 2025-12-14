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

    public static DslFlowResult<TState> Success(TState state, DslFlowStatus status, int steps = 0)
        => new(true, state, status, steps);

    public static DslFlowResult<TState> Failure(DslFlowStatus status, string? error, int steps = 0)
        => new(false, default, status, steps, error);

    public static DslFlowResult<TState> Failure(TState? state, DslFlowStatus status, string? error, int steps = 0)
        => new(false, state, status, steps, error);
}
