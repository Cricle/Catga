using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>If branch builder.</summary>
public interface IIfBuilder<TState> where TState : class, IFlowState
{
    IIfBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IIfBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);
    IIfBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    IIfBuilder<TState> If(Func<TState, bool> condition);
    IIfBuilder<TState> EndIf();

    IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector);

    IIfBuilder<TState> ElseIf(Func<TState, bool> condition);
    IIfBuilder<TState> Else();
}

/// <summary>If builder with result.</summary>
public interface IIfBuilder<TState, TResult> where TState : class, IFlowState
{
    IIfBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IIfBuilder<TState> Into(Action<TState, TResult> setter);
}
