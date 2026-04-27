using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Flow.StateMachine;

/// <summary>
/// Base class for state machine configurations.
/// Provides State&lt;TEnum&gt; + On&lt;TEvent&gt; + TransitionTo syntax.
/// 
/// Usage:
/// <code>
/// public class OrderStateMachine : StateMachineConfig&lt;OrderState, OrderStatus&gt;
/// {
///     protected override void Configure()
///     {
///         State(OrderStatus.Pending)
///             .On&lt;OrderPaid&gt;()
///                 .Execute((s, e, _) => { s.PaidAt = e.PaidAt; return ValueTask.CompletedTask; })
///                 .TransitionTo(OrderStatus.Paid)
///             .And()
///             .On&lt;OrderCancelled&gt;()
///                 .TransitionTo(OrderStatus.Cancelled);
///
///         State(OrderStatus.Paid)
///             .On&lt;OrderShipped&gt;()
///                 .TransitionTo(OrderStatus.Shipped);
///     }
/// }
/// </code>
/// </summary>
public abstract class StateMachineConfig<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TStateEnum>
    where TState : class, IStateMachineState<TStateEnum>
    where TStateEnum : struct, Enum
{
    private readonly Dictionary<TStateEnum, StateDefinition<TState, TStateEnum>> _states = new();
    private readonly List<EventCorrelationRegistration> _eventCorrelationRegistrations = [];
    private readonly List<InitialInstanceFactoryRegistration> _initialInstanceFactories = [];
    private readonly List<InitialEventRegistration> _initialEvents = [];
    private TStateEnum? _initialState;
    private bool _built;

    /// <summary>Define behavior for a specific state.</summary>
    protected StateDefinition<TState, TStateEnum> State(TStateEnum state)
    {
        if (!_states.TryGetValue(state, out var def))
        {
            def = new StateDefinition<TState, TStateEnum>(this, state);
            _states[state] = def;
        }
        return def;
    }

    /// <summary>
    /// Declares the initial state to use when a state machine instance is created implicitly.
    /// </summary>
    protected void Initially(TStateEnum state) => _initialState = state;

    /// <summary>
    /// Registers how the first correlated event should hydrate a new state machine instance.
    /// Combine with <see cref="Initially(TStateEnum)"/> when the first event should be
    /// processed from a specific starting state.
    /// </summary>
    protected void CreateInstanceFrom<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        Func<TEvent, string, TState> instanceFactory)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(instanceFactory);
        _initialInstanceFactories.Add(new InitialInstanceFactoryRegistration(
            typeof(TEvent),
            (@event, instanceId) => instanceFactory((TEvent)@event, instanceId)));
    }

    /// <summary>
    /// Registers how a published event maps to the state machine instance id.
    /// Events with a correlation selector can be auto-wired as message-driven saga inputs.
    /// </summary>
    protected void CorrelateById<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(Func<TEvent, string> instanceIdSelector)
        where TEvent : class, IEvent
    {
        RegisterEventCorrelation(instanceIdSelector);
    }

    /// <summary>Configure the state machine transitions.</summary>
    protected abstract void Configure();

    /// <summary>Build the state machine (idempotent).</summary>
    public void Build()
    {
        if (_built) return;
        Configure();
        _built = true;
    }

    /// <summary>Get all state definitions.</summary>
    public IReadOnlyDictionary<TStateEnum, StateDefinition<TState, TStateEnum>> States
    {
        get
        {
            Build();
            return _states;
        }
    }

    internal IReadOnlyList<Action<IServiceCollection>> EventCorrelationRegistrations
    {
        get
        {
            Build();
            return _eventCorrelationRegistrations
                .Select(registration => registration.Registration)
                .ToArray();
        }
    }

    internal TStateEnum? InitialState
    {
        get
        {
            Build();
            return _initialState;
        }
    }

    internal TState? CreateInitialInstance(IEvent @event, string instanceId)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrEmpty(instanceId);

        Build();

        var eventType = @event.GetType();
        var registration = _initialInstanceFactories
            .FirstOrDefault(candidate => candidate.EventType.IsAssignableFrom(eventType));

        return registration?.Factory(@event, instanceId);
    }

    internal bool TryGetInitialEvent(IEvent @event, [NotNullWhen(true)] out InitialEventRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(@event);

        Build();

        var eventType = @event.GetType();
        registration = _initialEvents
            .FirstOrDefault(candidate => candidate.EventType.IsAssignableFrom(eventType));

        return registration != null;
    }

    internal void RegisterEventCorrelation<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        Func<TEvent, string> instanceIdSelector)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(instanceIdSelector);

        var eventType = typeof(TEvent);
        _eventCorrelationRegistrations.RemoveAll(candidate => candidate.EventType == eventType);
        _eventCorrelationRegistrations.Add(new EventCorrelationRegistration(
            eventType,
            services => services.AddSingleton<IEventHandler<TEvent>>(sp =>
                new StateMachineEventHandler<TState, TStateEnum, TEvent>(
                    sp.GetRequiredService<IStateMachineExecutor<TState, TStateEnum>>(),
                    instanceIdSelector))));
    }

    internal void RegisterInitialEvent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TStateEnum state,
        Func<TEvent, string> instanceIdSelector,
        Func<TEvent, string, TState>? instanceFactory = null)
        where TEvent : class, IEvent
    {
        RegisterEventCorrelation(instanceIdSelector);

        var eventType = typeof(TEvent);
        _initialEvents.RemoveAll(candidate => candidate.EventType == eventType);
        _initialEvents.Add(new InitialEventRegistration(
            eventType,
            state,
            instanceFactory == null
                ? null
                : (@event, instanceId) => instanceFactory((TEvent)@event, instanceId)));
    }

    /// <summary>
    /// Process an event against the current state.
    /// Returns the new state (may be same if no transition defined).
    /// </summary>
    public async ValueTask<TStateEnum> ProcessEventAsync(
        TState state,
        IEvent @event,
        CancellationToken ct = default)
    {
        Build();

        if (!_states.TryGetValue(state.CurrentState, out var stateDef))
            return state.CurrentState; // Unknown state, no-op

        var eventType = @event.GetType();
        var transition = stateDef.Transitions
            .FirstOrDefault(t => t.EventType.IsAssignableFrom(eventType)
                                 && (t.Guard == null || t.Guard(state, @event)));

        if (transition == null)
            return state.CurrentState; // No matching transition

        // Execute exit action
        if (stateDef.OnExitAction != null)
            await stateDef.OnExitAction(state, ct);

        // Execute transition action
        if (transition.Action != null)
            await transition.Action(state, @event, ct);

        // Apply state transition
        if (transition.TargetState.HasValue)
        {
            var previousState = state.CurrentState;
            state.CurrentState = transition.TargetState.Value;

            // Execute enter action for new state
            if (_states.TryGetValue(state.CurrentState, out var newStateDef)
                && newStateDef.OnEnterAction != null)
            {
                await newStateDef.OnEnterAction(state, ct);
            }
        }

        return state.CurrentState;
    }

    /// <summary>Check if a transition exists for the given event in the current state.</summary>
    public bool CanHandle(TState state, Type eventType)
    {
        Build();
        return _states.TryGetValue(state.CurrentState, out var def)
               && def.Transitions.Any(t => t.EventType.IsAssignableFrom(eventType));
    }

    internal sealed record InitialEventRegistration(
        Type EventType,
        TStateEnum State,
        Func<IEvent, string, TState>? Factory);

    private sealed record EventCorrelationRegistration(
        Type EventType,
        Action<IServiceCollection> Registration);

    private sealed record InitialInstanceFactoryRegistration(
        Type EventType,
        Func<IEvent, string, TState> Factory);
}
