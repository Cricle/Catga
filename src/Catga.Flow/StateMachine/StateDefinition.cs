using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Flow.StateMachine;

/// <summary>
/// Defines transitions and actions for a single state.
/// </summary>
public sealed class StateDefinition<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>
    where TStateEnum : struct, Enum
{
    private readonly StateMachineConfig<TState, TStateEnum> _owner;
    internal readonly TStateEnum State;
    internal readonly List<EventTransition<TState, TStateEnum>> Transitions = [];
    internal Func<TState, CancellationToken, ValueTask>? OnEnterAction;
    internal Func<TState, CancellationToken, ValueTask>? OnExitAction;

    internal StateDefinition(StateMachineConfig<TState, TStateEnum> owner, TStateEnum state)
    {
        _owner = owner;
        State = state;
    }

    /// <summary>Define a transition when an event of type TEvent is received.</summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> On<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>()
        where TEvent : class, IEvent
    {
        var transition = new EventTransition<TState, TStateEnum>(typeof(TEvent));
        Transitions.Add(transition);
        return new EventTransitionBuilder<TState, TStateEnum, TEvent>(_owner, this, transition);
    }

    /// <summary>Action to execute when entering this state.</summary>
    public StateDefinition<TState, TStateEnum> OnEnter(Func<TState, CancellationToken, ValueTask> action)
    {
        OnEnterAction = action;
        return this;
    }

    /// <summary>Action to execute when exiting this state.</summary>
    public StateDefinition<TState, TStateEnum> OnExit(Func<TState, CancellationToken, ValueTask> action)
    {
        OnExitAction = action;
        return this;
    }
}

/// <summary>
/// Represents a single event-triggered transition.
/// </summary>
public sealed class EventTransition<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>
    where TStateEnum : struct, Enum
{
    internal readonly Type EventType;
    internal TStateEnum? TargetState;
    internal Func<TState, object, CancellationToken, ValueTask>? Action;
    internal Func<TState, object, bool>? Guard;

    internal EventTransition(Type eventType) => EventType = eventType;
}

/// <summary>
/// Fluent builder for a single event transition.
/// </summary>
public sealed class EventTransitionBuilder<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
    TStateEnum,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>
    where TState : class, IStateMachineState<TStateEnum>
    where TStateEnum : struct, Enum
    where TEvent : class, IEvent
{
    private readonly StateMachineConfig<TState, TStateEnum> _owner;
    private readonly StateDefinition<TState, TStateEnum> _stateDef;
    private readonly EventTransition<TState, TStateEnum> _transition;

    internal EventTransitionBuilder(
        StateMachineConfig<TState, TStateEnum> owner,
        StateDefinition<TState, TStateEnum> stateDef,
        EventTransition<TState, TStateEnum> transition)
    {
        _owner = owner;
        _stateDef = stateDef;
        _transition = transition;
    }

    /// <summary>
    /// Registers message correlation for this event so published events can drive the state machine.
    /// </summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> CorrelateById(Func<TEvent, string> instanceIdSelector)
    {
        _owner.RegisterEventCorrelation(instanceIdSelector);
        return this;
    }

    /// <summary>
    /// Declares that this event can create a missing instance in the current state.
    /// Also auto-registers the event correlation used by the message bridge.
    /// </summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> StartsNew(Func<TEvent, string> instanceIdSelector)
    {
        _owner.RegisterInitialEvent(_stateDef.State, instanceIdSelector);
        return this;
    }

    /// <summary>
    /// Declares that this event can create a missing instance in the current state,
    /// hydrating the new instance from the event payload.
    /// Also auto-registers the event correlation used by the message bridge.
    /// </summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> StartsNew(
        Func<TEvent, string> instanceIdSelector,
        Func<TEvent, string, TState> instanceFactory)
    {
        _owner.RegisterInitialEvent(_stateDef.State, instanceIdSelector, instanceFactory);
        return this;
    }

    /// <summary>
    /// Declares that this event can create a missing instance in the current state,
    /// hydrating the new instance from the event payload.
    /// Also auto-registers the event correlation used by the message bridge.
    /// </summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> StartsNew(
        Func<TEvent, string> instanceIdSelector,
        Func<TEvent, TState> instanceFactory)
    {
        _owner.RegisterInitialEvent(_stateDef.State, instanceIdSelector, (evt, _) => instanceFactory(evt));
        return this;
    }

    /// <summary>Transition to the specified state after handling the event.</summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> TransitionTo(TStateEnum targetState)
    {
        _transition.TargetState = targetState;
        return this;
    }

    /// <summary>Execute an action when this event is received.</summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> Execute(
        Func<TState, TEvent, CancellationToken, ValueTask> action)
    {
        _transition.Action = (state, evt, ct) => action(state, (TEvent)evt, ct);
        return this;
    }

    /// <summary>Execute a synchronous action when this event is received.</summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> Execute(Action<TState, TEvent> action)
    {
        _transition.Action = (state, evt, _) =>
        {
            action(state, (TEvent)evt);
            return ValueTask.CompletedTask;
        };
        return this;
    }

    /// <summary>Only trigger this transition if the guard condition is true.</summary>
    public EventTransitionBuilder<TState, TStateEnum, TEvent> When(Func<TState, TEvent, bool> guard)
    {
        _transition.Guard = (state, evt) => guard(state, (TEvent)evt);
        return this;
    }

    /// <summary>Return to the state definition for chaining more transitions.</summary>
    public StateDefinition<TState, TStateEnum> And() => _stateDef;
}
