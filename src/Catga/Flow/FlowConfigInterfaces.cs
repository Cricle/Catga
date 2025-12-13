using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Flow builder interface.
/// </summary>
public interface IFlowBuilder<TState> where TState : class, IFlowState
{
    // Name
    IFlowBuilder<TState> Name(string name);

    // Global settings
    ITaggedSetting Timeout(TimeSpan timeout);
    ITaggedSetting Retry(int maxRetries);
    ITaggedSetting Persist();

    // Event hooks
    IFlowBuilder<TState> OnStepCompleted<TEvent>(Func<TState, int, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnStepFailed<TEvent>(Func<TState, int, string?, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnFlowCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnFlowFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent;

    // Steps
    IStepBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IStepBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);
    IQueryBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory);
    IPublishBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    // Parallel
    IWhenAllBuilder<TState> WhenAll(params Func<TState, IRequest>[] requests);
    IWhenAnyBuilder<TState> WhenAny(params Func<TState, IRequest>[] requests);
    IWhenAnyBuilder<TState, TResult> WhenAny<TResult>(params Func<TState, IRequest<TResult>>[] requests);

    // Branching
    IIfBuilder<TState> If(Func<TState, bool> condition);
    ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull;

    // ForEach
    IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector);
}

/// <summary>If branch builder.</summary>
public interface IIfBuilder<TState> where TState : class, IFlowState
{
    // Steps within If branch
    IIfBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IIfBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);
    IIfBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    // Nested branching
    IIfBuilder<TState> If(Func<TState, bool> condition);
    IIfBuilder<TState> EndIf();

    // Collection processing
    IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector);

    // Branch transitions
    IIfBuilder<TState> ElseIf(Func<TState, bool> condition);
    IIfBuilder<TState> Else();
}

/// <summary>If builder with result.</summary>
public interface IIfBuilder<TState, TResult> where TState : class, IFlowState
{
    IIfBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IIfBuilder<TState> Into(Action<TState, TResult> setter);
}

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
    ICaseBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    ICaseBuilder<TState> Into(Action<TState, TResult> setter);
}

/// <summary>
/// Tagged setting for ForTags().
/// </summary>
public interface ITaggedSetting
{
    void ForTags(params string[] tags);
}

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

    // Branching (allows chaining from step to branch)
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

/// <summary>
/// Query builder interface.
/// </summary>
public interface IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IQueryBuilder<TState, TResult> Tag(params string[] tags);
}

/// <summary>
/// Publish builder interface.
/// </summary>
public interface IPublishBuilder<TState> where TState : class, IFlowState
{
    IPublishBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAll builder interface.
/// </summary>
public interface IWhenAllBuilder<TState> where TState : class, IFlowState
{
    IWhenAllBuilder<TState> Timeout(TimeSpan timeout);
    IWhenAllBuilder<TState> IfAnyFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IWhenAllBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAny builder interface.
/// </summary>
public interface IWhenAnyBuilder<TState> where TState : class, IFlowState
{
    IWhenAnyBuilder<TState> Timeout(TimeSpan timeout);
    IWhenAnyBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAny builder with result.
/// </summary>
public interface IWhenAnyBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IWhenAnyBuilder<TState, TResult> Timeout(TimeSpan timeout);
    IWhenAnyBuilder<TState, TResult> Tag(params string[] tags);
}
