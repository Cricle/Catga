using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Flow.Dsl;

namespace Catga.Flow.StateMachine;

/// <summary>
/// Executes a state machine by loading state, processing events, and persisting state.
/// </summary>
public sealed class StateMachineExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TStateEnum, TConfig>
    : IStateMachineExecutor<TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>, new()
    where TStateEnum : struct, Enum
    where TConfig : StateMachineConfig<TState, TStateEnum>, new()
{
    private readonly TConfig _config;
    private readonly IDslFlowStore _store;

    public StateMachineExecutor(IDslFlowStore store, TConfig? config = null)
    {
        _store = store;
        _config = config ?? new TConfig();
        _config.Build();
    }

    /// <summary>
    /// Process an event for a specific state machine instance.
    /// Loads state, applies transition, persists updated state.
    /// </summary>
    public async ValueTask<StateMachineResult<TStateEnum>> HandleAsync(
        string instanceId,
        IEvent @event,
        CancellationToken ct = default)
    {
        var snapshot = await _store.GetAsync<TState>(instanceId, ct);
        TState state;
        int version;

        if (snapshot == null)
        {
            if (_config.TryGetInitialEvent(@event, out var initialEvent))
            {
                state = initialEvent.Factory?.Invoke(@event, instanceId) ?? new TState { FlowId = instanceId };
                EnsureFlowId(state, instanceId);
                state.CurrentState = initialEvent.State;
            }
            else
            {
                state = _config.CreateInitialInstance(@event, instanceId) ?? new TState { FlowId = instanceId };
                EnsureFlowId(state, instanceId);
                if (_config.InitialState.HasValue)
                    state.CurrentState = _config.InitialState.Value;
            }
            version = 1;
        }
        else
        {
            state = snapshot.State;
            version = snapshot.Version + 1;
        }

        var previousState = state.CurrentState;

        if (!_config.CanHandle(state, @event.GetType()))
            return new StateMachineResult<TStateEnum>(instanceId, previousState, previousState, false);

        var newState = await _config.ProcessEventAsync(state, @event, ct);

        var newSnapshot = new FlowSnapshot<TState>
        {
            FlowId = instanceId,
            State = state,
            Status = DslFlowStatus.Running,
            Version = GetVersionToPersist(snapshot, version),
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = snapshot?.CreatedAt ?? DateTime.UtcNow
        };

        if (snapshot == null)
        {
            if (!await _store.CreateAsync(newSnapshot, ct))
                throw new InvalidOperationException($"Failed to create state machine snapshot for instance '{instanceId}'.");
        }
        else if (!await TryUpdateSnapshotAsync(snapshot, newSnapshot, ct))
        {
            throw new InvalidOperationException(
                $"Failed to persist state machine snapshot for instance '{instanceId}' from version {snapshot.Version}.");
        }

        return new StateMachineResult<TStateEnum>(instanceId, previousState, newState, true);
    }

    /// <summary>Initialize a new state machine instance.</summary>
    public async ValueTask<TState> InitializeAsync(
        string instanceId,
        TStateEnum initialState,
        Action<TState>? configure = null,
        CancellationToken ct = default)
    {
        var state = new TState { FlowId = instanceId, CurrentState = initialState };
        configure?.Invoke(state);

        await _store.CreateAsync(new FlowSnapshot<TState>
        {
            FlowId = instanceId,
            State = state,
            Status = DslFlowStatus.Running,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return state;
    }

    /// <summary>Get current state of an instance.</summary>
    public async ValueTask<TState?> GetStateAsync(string instanceId, CancellationToken ct = default)
    {
        var snapshot = await _store.GetAsync<TState>(instanceId, ct);
        return snapshot?.State;
    }

    private static void EnsureFlowId(TState state, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(state.FlowId))
        {
            state.FlowId = instanceId;
            return;
        }

        if (!string.Equals(state.FlowId, instanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Initial state factory returned FlowId '{state.FlowId}' for instance '{instanceId}'.");
        }
    }

    private int GetVersionToPersist(FlowSnapshot<TState>? snapshot, int defaultVersion)
    {
        if (snapshot == null)
            return defaultVersion;

        var versioning = _store as IDslFlowStoreVersioning;
        return versioning?.VersioningMode == DslFlowStoreVersioningMode.StoreAdvancesVersion
            ? snapshot.Version
            : defaultVersion;
    }

    private async Task<bool> TryUpdateSnapshotAsync(
        FlowSnapshot<TState> original,
        FlowSnapshot<TState> preferredSnapshot,
        CancellationToken ct)
    {
        var versioning = _store as IDslFlowStoreVersioning;
        if (versioning != null)
            return await _store.UpdateAsync(preferredSnapshot, ct);

        if (await _store.UpdateAsync(preferredSnapshot, ct))
            return true;

        var currentVersionSnapshot = preferredSnapshot with { Version = original.Version };
        return await _store.UpdateAsync(currentVersionSnapshot, ct);
    }
}

/// <summary>Result of processing an event through the state machine.</summary>
public readonly record struct StateMachineResult<TStateEnum>(
    string InstanceId,
    TStateEnum PreviousState,
    TStateEnum CurrentState,
    bool Handled)
    where TStateEnum : struct, Enum
{
    public bool Transitioned => !EqualityComparer<TStateEnum>.Default.Equals(PreviousState, CurrentState);
}
