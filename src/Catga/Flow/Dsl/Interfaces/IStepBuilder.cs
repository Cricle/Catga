using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Step builder interface.
/// </summary>
public interface IStepBuilder<TState> where TState : class, IFlowState
{
    IStepBuilder<TState> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IStepBuilder<TState> FailIf(Func<TState, bool> condition);
    IStepBuilder<TState> FailIf(Func<TState, bool> condition, string errorMessage);
    IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition);
    IStepBuilder<TState> Optional();
    IStepBuilder<TState> Tag(params string[] tags);
    IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;
    IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent;

    IIfBuilder<TState> If(Func<TState, bool> condition);
    ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull;
}

/// <summary>
/// Step builder with result.
/// </summary>
public interface IStepBuilder<TState, TResult> : IStepBuilder<TState> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    new IStepBuilder<TState, TResult> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition);
    IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition, string errorMessage);
}
