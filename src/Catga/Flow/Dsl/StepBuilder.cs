using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Step builder without result.
/// </summary>
internal class StepBuilder<TState> : StepBuilderBase<TState, StepBuilder<TState>>, IStepBuilder<TState>
    where TState : class, IFlowState
{
    public StepBuilder(FlowStep step) : base(null, step) { }

    public StepBuilder(FlowBuilder<TState>? builder, FlowStep step) : base(builder, step) { }

    protected override StepBuilder<TState> Self => this;

    public IStepBuilder<TState> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        Step.HasCompensation = true;
        Step.CompensationFactory = factory;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition)
    {
        Step.HasFailCondition = true;
        Step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition, string errorMessage)
    {
        Step.HasFailCondition = true;
        Step.FailConditionFactory = condition;
        Step.FailConditionMessage = errorMessage;
        return this;
    }

    // Interface implementations that delegate to base class
    IStepBuilder<TState> IStepBuilder<TState>.Tag(params string[] tags) => Tag(tags);
    IStepBuilder<TState> IStepBuilder<TState>.OnlyWhen(Func<TState, bool> condition) => OnlyWhen(condition);
    IStepBuilder<TState> IStepBuilder<TState>.Optional() => Optional();
    IStepBuilder<TState> IStepBuilder<TState>.OnCompleted<TEvent>(Func<TState, TEvent> factory) => OnCompleted(factory);
    IStepBuilder<TState> IStepBuilder<TState>.OnFailed<TEvent>(Func<TState, string?, TEvent> factory) => OnFailed(factory);
}

/// <summary>
/// Step builder with result.
/// </summary>
internal class StepBuilder<TState, TResult> : StepBuilderBase<TState, StepBuilder<TState, TResult>>, IStepBuilder<TState, TResult>
    where TState : class, IFlowState
{
    public StepBuilder(FlowBuilder<TState> builder, FlowStep step) : base(builder, step) { }

    protected override StepBuilder<TState, TResult> Self => this;

    public IStepBuilder<TState> Into(Action<TState, TResult> setter)
    {
        Step.ResultSetter = setter;
        return new StepBuilder<TState>(Builder!, Step);
    }

    public IStepBuilder<TState, TResult> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        Step.HasCompensation = true;
        Step.CompensationFactory = factory;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.IfFail<TRequest>(Func<TState, TRequest> factory)
    {
        Step.HasCompensation = true;
        Step.CompensationFactory = factory;
        return new StepBuilder<TState>(Step);
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition)
    {
        Step.HasFailCondition = true;
        Step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition, string errorMessage)
    {
        Step.HasFailCondition = true;
        Step.FailConditionFactory = condition;
        Step.FailConditionMessage = errorMessage;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition)
    {
        Step.HasFailCondition = true;
        Step.FailConditionFactory = condition;
        return new StepBuilder<TState>(Step);
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition, string errorMessage)
    {
        Step.HasFailCondition = true;
        Step.FailConditionFactory = condition;
        Step.FailConditionMessage = errorMessage;
        return new StepBuilder<TState>(Step);
    }

    // Interface implementations that delegate to base class
    IStepBuilder<TState> IStepBuilder<TState>.Tag(params string[] tags) => Tag(tags);
    IStepBuilder<TState> IStepBuilder<TState>.OnlyWhen(Func<TState, bool> condition) => OnlyWhen(condition);
    IStepBuilder<TState> IStepBuilder<TState>.Optional() => Optional();
    IStepBuilder<TState> IStepBuilder<TState>.OnCompleted<TEvent>(Func<TState, TEvent> factory) => OnCompleted(factory);
    IStepBuilder<TState> IStepBuilder<TState>.OnFailed<TEvent>(Func<TState, string?, TEvent> factory) => OnFailed(factory);
}
