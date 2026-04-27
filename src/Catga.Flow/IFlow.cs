namespace Catga.Flow.Dsl;

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
