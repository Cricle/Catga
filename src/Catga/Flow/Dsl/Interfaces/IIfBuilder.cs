using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>If branch builder.</summary>
public interface IIfBuilder<TState> where TState : class, IFlowState
{
    IIfBuilder<TState> Send<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    
    /// <summary>Send a request with result using concrete request type (recommended for AOT compatibility).</summary>
    IIfBuilder<TState, TResult> Send<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(Func<TState, TRequest> factory) where TRequest : IRequest<TResult>;
    
    IIfBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    IIfBuilder<TState> If(Func<TState, bool> condition);
    IIfBuilder<TState> EndIf();

    IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector);

    IIfBuilder<TState> ElseIf(Func<TState, bool> condition);
    IIfBuilder<TState> Else();

    IIfBuilder<TState> Delay(TimeSpan delay);
    IIfBuilder<TState> ScheduleAt(Func<TState, DateTime> timeSelector);
}

/// <summary>If builder with result.</summary>
public interface IIfBuilder<TState, TResult> where TState : class, IFlowState
{
    IIfBuilder<TState> Into(Action<TState, TResult> setter);
}
