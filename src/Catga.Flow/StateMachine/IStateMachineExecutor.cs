using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Flow.StateMachine;

public interface IStateMachineExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>
    where TStateEnum : struct, Enum
{
    ValueTask<StateMachineResult<TStateEnum>> HandleAsync(
        string instanceId,
        IEvent @event,
        CancellationToken ct = default);

    ValueTask<TState> InitializeAsync(
        string instanceId,
        TStateEnum initialState,
        Action<TState>? configure = null,
        CancellationToken ct = default);

    ValueTask<TState?> GetStateAsync(string instanceId, CancellationToken ct = default);
}
