using Catga.Core;

namespace Catga.Flow.Dsl;

/// <summary>
/// Default implementation of IFlowExecutor that creates DslFlowExecutor instances.
/// </summary>
public sealed class FlowExecutorService : IFlowExecutor
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;
    private readonly IFlowScheduler? _scheduler;

    public FlowExecutorService(ICatgaMediator mediator, IDslFlowStore store, IFlowScheduler? scheduler = null)
    {
        _mediator = mediator;
        _store = store;
        _scheduler = scheduler;
    }

    public async Task<DslFlowResult<TState>> ExecuteAsync<TFlow, TState>(
        TState initialState,
        CancellationToken cancellationToken = default)
        where TFlow : FlowConfig<TState>, new()
        where TState : class, IFlowState, new()
    {
        var config = new TFlow();
        var executor = new DslFlowExecutor<TState, TFlow>(_mediator, _store, config, _scheduler);
        return await executor.RunAsync(initialState, cancellationToken);
    }

    public async Task<DslFlowResult<TState>> ResumeAsync<TFlow, TState>(
        string flowId,
        CancellationToken cancellationToken = default)
        where TFlow : FlowConfig<TState>, new()
        where TState : class, IFlowState, new()
    {
        var config = new TFlow();
        var executor = new DslFlowExecutor<TState, TFlow>(_mediator, _store, config, _scheduler);
        return await executor.ResumeAsync(flowId, cancellationToken);
    }

    public async Task<FlowSnapshot<TState>?> GetSnapshotAsync<TState>(
        string flowId,
        CancellationToken cancellationToken = default)
        where TState : class, IFlowState, new()
    {
        return await _store.GetAsync<TState>(flowId, cancellationToken);
    }

    public async Task<bool> CancelAsync(string flowId, CancellationToken cancellationToken = default)
    {
        // We need to know the TState type to cancel, but we don't have it here
        // For now, we'll use a generic approach through the store
        var snapshot = await _store.GetAsync<IFlowState>(flowId, cancellationToken);
        if (snapshot == null || snapshot.Status != DslFlowStatus.Running)
            return false;

        var cancelled = snapshot with
        {
            Status = DslFlowStatus.Cancelled,
            UpdatedAt = DateTime.UtcNow
        };

        return await _store.UpdateAsync(cancelled, cancellationToken);
    }
}
