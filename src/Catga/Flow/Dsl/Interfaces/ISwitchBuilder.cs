using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>Switch branch builder.</summary>
public interface ISwitchBuilder<TState, TValue> where TState : class, IFlowState where TValue : notnull
{
    ISwitchBuilder<TState, TValue> Case(TValue value, Action<ICaseBuilder<TState>> configure);
    ISwitchBuilder<TState, TValue> Default(Action<ICaseBuilder<TState>> configure);
    IFlowBuilder<TState> EndSwitch();
}

/// <summary>Case builder for Switch.</summary>
public interface ICaseBuilder<TState> where TState : class, IFlowState
{
    ICaseBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    ICaseBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);
    ICaseBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;
}

/// <summary>Case builder with result.</summary>
public interface ICaseBuilder<TState, TResult> where TState : class, IFlowState
{
    ICaseBuilder<TState> Into(Action<TState, TResult> setter);
}
