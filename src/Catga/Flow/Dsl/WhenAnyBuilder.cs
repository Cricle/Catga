namespace Catga.Flow.Dsl;

/// <summary>
/// WhenAny builder for any-of-many execution steps without result.
/// </summary>
internal class WhenAnyBuilder<TState>(FlowStep step) : IWhenAnyBuilder<TState> where TState : class, IFlowState
{
    public IWhenAnyBuilder<TState> Timeout(TimeSpan timeout)
    {
        step.Timeout = timeout;
        return this;
    }

    public IWhenAnyBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}

/// <summary>
/// WhenAny builder for any-of-many execution steps with result.
/// </summary>
internal class WhenAnyBuilder<TState, TResult>(FlowStep step) : IWhenAnyBuilder<TState, TResult> where TState : class, IFlowState
{
    public IStepBuilder<TState> Into(Action<TState, TResult> setter)
    {
        step.ResultSetter = setter;
        return new StepBuilder<TState>(step);
    }

    public IWhenAnyBuilder<TState, TResult> Timeout(TimeSpan timeout)
    {
        step.Timeout = timeout;
        return this;
    }

    public IWhenAnyBuilder<TState, TResult> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}
