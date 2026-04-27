using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Flow.StateMachine;

internal sealed class StateMachineEventHandler<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
    TStateEnum,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>
    : IEventHandler<TEvent>
    where TState : class, IStateMachineState<TStateEnum>
    where TStateEnum : struct, Enum
    where TEvent : class, IEvent
{
    private readonly IStateMachineExecutor<TState, TStateEnum> _executor;
    private readonly Func<TEvent, string> _instanceIdSelector;

    public StateMachineEventHandler(
        IStateMachineExecutor<TState, TStateEnum> executor,
        Func<TEvent, string> instanceIdSelector)
    {
        _executor = executor;
        _instanceIdSelector = instanceIdSelector;
    }

    public async ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        var instanceId = _instanceIdSelector(@event);
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new InvalidOperationException($"State machine instance id cannot be empty for event {typeof(TEvent).FullName}.");

        await _executor.HandleAsync(instanceId, @event, cancellationToken);
    }
}
