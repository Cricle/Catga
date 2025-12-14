using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Switch builder for switch/case branching.
/// </summary>
internal class SwitchBuilder<TState, TValue> : ISwitchBuilder<TState, TValue>
    where TState : class, IFlowState
    where TValue : notnull
{
    private readonly FlowBuilder<TState> _rootBuilder;
    private readonly FlowStep _switchStep;

    public SwitchBuilder(FlowBuilder<TState> rootBuilder, FlowStep switchStep)
    {
        _rootBuilder = rootBuilder;
        _switchStep = switchStep;
    }

    public ISwitchBuilder<TState, TValue> Case(TValue value, Action<ICaseBuilder<TState>> configure)
    {
        var steps = new List<FlowStep>();
        var caseBuilder = new CaseBuilder<TState>(steps);
        configure(caseBuilder);
        _switchStep.Cases![value] = steps;
        return this;
    }

    public ISwitchBuilder<TState, TValue> Default(Action<ICaseBuilder<TState>> configure)
    {
        var steps = new List<FlowStep>();
        var caseBuilder = new CaseBuilder<TState>(steps);
        configure(caseBuilder);
        _switchStep.DefaultBranch = steps;
        return this;
    }

    public IFlowBuilder<TState> EndSwitch() => _rootBuilder;
}

/// <summary>
/// Case builder for individual case branches in switch statements.
/// </summary>
internal class CaseBuilder<TState> : ICaseBuilder<TState> where TState : class, IFlowState
{
    private readonly List<FlowStep> _steps;

    public CaseBuilder(List<FlowStep> steps) => _steps = steps;

    public ICaseBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
        _steps.Add(step);
        return this;
    }

    public ICaseBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Send, HasResult = true, RequestFactory = factory };
        _steps.Add(step);
        return new CaseBuilderWithResult<TState, TResult>(this, step);
    }

    public ICaseBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        var step = new FlowStep { Type = StepType.Publish, RequestFactory = factory };
        _steps.Add(step);
        return this;
    }
}

/// <summary>
/// Case builder with result for Send steps that return values.
/// </summary>
internal class CaseBuilderWithResult<TState, TResult>(CaseBuilder<TState> parent, FlowStep step) : ICaseBuilder<TState, TResult>
    where TState : class, IFlowState
{
    public ICaseBuilder<TState> Into(Action<TState, TResult> setter)
    {
        step.ResultSetter = setter;
        return parent;
    }
}
