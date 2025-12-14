using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Flow builder interface - core methods only.
/// Step methods (Send, Query, Publish, etc.) are provided via extension methods.
/// </summary>
public interface IFlowBuilder<TState> where TState : class, IFlowState
{
    IFlowBuilder<TState> Name(string name);

    ITaggedSetting Timeout(TimeSpan timeout);
    ITaggedSetting Retry(int maxRetries);
    ITaggedSetting Persist();

    IFlowBuilder<TState> OnStepCompleted<TEvent>(Func<TState, int, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnStepFailed<TEvent>(Func<TState, int, string?, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnFlowCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnFlowFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent;
}

/// <summary>
/// Tagged setting for ForTags().
/// </summary>
public interface ITaggedSetting
{
    void ForTags(params string[] tags);
}
