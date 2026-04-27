using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow.Dsl;

/// <summary>
/// Interface for executing flows defined by FlowConfig DSL.
/// </summary>
public interface IFlowExecutor
{
    /// <summary>
    /// Execute a flow with the given initial state.
    /// </summary>
    Task<DslFlowResult<TState>> ExecuteAsync<TFlow, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(TState initialState, CancellationToken cancellationToken = default)
        where TFlow : FlowConfig<TState>, new()
        where TState : class, IFlowState, new();

    /// <summary>
    /// Resume a suspended or failed flow.
    /// </summary>
    Task<DslFlowResult<TState>> ResumeAsync<TFlow, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(string flowId, CancellationToken cancellationToken = default)
        where TFlow : FlowConfig<TState>, new()
        where TState : class, IFlowState, new();

    /// <summary>
    /// Get the current snapshot of a flow.
    /// </summary>
    Task<FlowSnapshot<TState>?> GetSnapshotAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(string flowId, CancellationToken cancellationToken = default)
        where TState : class, IFlowState, new();

    /// <summary>
    /// Cancel a running flow.
    /// </summary>
    Task<bool> CancelAsync(string flowId, CancellationToken cancellationToken = default);
}
