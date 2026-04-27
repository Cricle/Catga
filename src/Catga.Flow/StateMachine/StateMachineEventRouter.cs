using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Flow.StateMachine;

/// <summary>
/// Routes published events to the correct state machine instance.
/// Equivalent to MassTransit's Saga InitiatedBy/Orchestrates.
/// </summary>
public interface IStateMachineEventRouter<TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>, new()
    where TStateEnum : struct, Enum
{
    Task RouteAsync(IEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Routes events to state machine instances via per-type correlation ID resolvers.
/// </summary>
public sealed class StateMachineEventRouter<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
    TStateEnum,
    TConfig>
    : IStateMachineEventRouter<TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>, new()
    where TStateEnum : struct, Enum
    where TConfig : StateMachineConfig<TState, TStateEnum>, new()
{
    private readonly StateMachineExecutor<TState, TStateEnum, TConfig> _executor;
    private readonly Dictionary<Type, Func<IEvent, string>> _resolvers = new();

    public StateMachineEventRouter(StateMachineExecutor<TState, TStateEnum, TConfig> executor)
        => _executor = executor;

    /// <summary>Register a typed resolver for a specific event type.</summary>
    public StateMachineEventRouter<TState, TStateEnum, TConfig> For<TEvent>(Func<TEvent, string> resolver)
        where TEvent : IEvent
    {
        _resolvers[typeof(TEvent)] = e => resolver((TEvent)e);
        return this;
    }

    /// <summary>Register a fallback resolver for all event types.</summary>
    public StateMachineEventRouter<TState, TStateEnum, TConfig> ForAll(Func<IEvent, string> resolver)
    {
        _resolvers[typeof(IEvent)] = resolver;
        return this;
    }

    public async Task RouteAsync(IEvent @event, CancellationToken ct = default)
    {
        var id = _resolvers.TryGetValue(@event.GetType(), out var r) ? r(@event)
                 : _resolvers.TryGetValue(typeof(IEvent), out var fb) ? fb(@event)
                 : null;
        if (id != null) await _executor.HandleAsync(id, @event, ct);
    }
}
