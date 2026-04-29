using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
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
    private readonly IRequestClientFactory? _requestClientFactory;

    public FlowExecutorService(
        ICatgaMediator mediator,
        IDslFlowStore store,
        IFlowScheduler? scheduler = null,
        IRequestClientFactory? requestClientFactory = null)
    {
        _mediator = mediator;
        _store = store;
        _scheduler = scheduler;
        _requestClientFactory = requestClientFactory;
    }

    public async Task<DslFlowResult<TState>> ExecuteAsync<TFlow, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        TState initialState,
        CancellationToken cancellationToken = default)
        where TFlow : FlowConfig<TState>, new()
        where TState : class, IFlowState, new()
    {
        var config = new TFlow();
        var executor = new DslFlowExecutor<TState, TFlow>(_mediator, _store, config, _scheduler, _requestClientFactory);
        return await executor.RunAsync(initialState, cancellationToken);
    }

    public async Task<DslFlowResult<TState>> ResumeAsync<TFlow, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId,
        CancellationToken cancellationToken = default)
        where TFlow : FlowConfig<TState>, new()
        where TState : class, IFlowState, new()
    {
        var config = new TFlow();
        var executor = new DslFlowExecutor<TState, TFlow>(_mediator, _store, config, _scheduler, _requestClientFactory);
        return await executor.ResumeAsync(flowId, cancellationToken);
    }

    public async Task<FlowSnapshot<TState>?> GetSnapshotAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
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
            UpdatedAt = DateTime.UtcNow,
            Version = snapshot.Version + 1
        };

        var versioning = _store as IDslFlowStoreVersioning;
        if (versioning != null)
        {
            var snapshotToPersist = versioning.VersioningMode == DslFlowStoreVersioningMode.StoreAdvancesVersion
                ? cancelled with { Version = snapshot.Version }
                : cancelled;
            return await _store.UpdateAsync(snapshotToPersist, cancellationToken);
        }

        if (await _store.UpdateAsync(cancelled, cancellationToken))
            return true;

        var currentVersionCancelled = cancelled with { Version = snapshot.Version };
        return await _store.UpdateAsync(currentVersionCancelled, cancellationToken);
    }
}
