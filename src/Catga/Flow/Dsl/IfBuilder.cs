using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Flow.Dsl;

/// <summary>
/// If builder for conditional branching.
/// </summary>
internal class IfBuilder<TState> : IIfBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _rootBuilder;
    private FlowStep _ifStep;
    private List<FlowStep> _currentBranch;
    private readonly Stack<(FlowStep Step, List<FlowStep> Branch)> _nestedStack = new();

    public IfBuilder(FlowBuilder<TState> rootBuilder, FlowStep ifStep, List<FlowStep> currentBranch)
    {
        _rootBuilder = rootBuilder;
        _ifStep = ifStep;
        _currentBranch = currentBranch;
    }

    public IIfBuilder<TState> Send<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        var step = new FlowStep
        {
            Type = StepType.Send,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state),
            ExecuteRequest = async (mediator, request, ct) =>
            {
                var result = await mediator.SendAsync((TRequest)request, ct);
                return (result.IsSuccess, result.Error, null);
            }
        };
        _currentBranch.Add(step);
        return this;
    }

    public IIfBuilder<TState, TResult> Send<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(Func<TState, TRequest> factory) where TRequest : IRequest<TResult>
    {
        var step = new FlowStep
        {
            Type = StepType.Send,
            HasResult = true,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state),
            ExecuteRequest = async (mediator, request, ct) =>
            {
                var typedRequest = (TRequest)request;
                var result = await mediator.SendAsync<TRequest, TResult>(typedRequest, ct);
                return (result.IsSuccess, result.Error, result.Value);
            }
        };
        _currentBranch.Add(step);
        return new IfBuilderWithResult<TState, TResult>(this, step);
    }

    public IIfBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        var step = new FlowStep
        {
            Type = StepType.Publish,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state)!
        };
        _currentBranch.Add(step);
        return this;
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition)
    {
        var nestedStep = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = condition,
            ThenBranch = []
        };
        _currentBranch.Add(nestedStep);
        _nestedStack.Push((_ifStep, _currentBranch));
        _ifStep = nestedStep;
        _currentBranch = nestedStep.ThenBranch;
        return this;
    }

    public IIfBuilder<TState> ElseIf(Func<TState, bool> condition)
    {
        _ifStep.ElseIfBranches ??= [];
        var branch = new List<FlowStep>();
        _ifStep.ElseIfBranches.Add((condition, branch));
        _currentBranch = branch;
        return this;
    }

    public IIfBuilder<TState> Else()
    {
        _ifStep.ElseBranch = [];
        _currentBranch = _ifStep.ElseBranch;
        return this;
    }

    public IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector)
    {
        var step = new FlowStep
        {
            Type = StepType.ForEach,
            CollectionSelector = collectionSelector,
            ItemSteps = []
        };
        _currentBranch.Add(step);
        return new ForEachBuilder<TState, TItem>(_rootBuilder, step);
    }

    public IIfBuilder<TState> EndIf()
    {
        if (_nestedStack.Count > 0)
        {
            var (parentStep, parentBranch) = _nestedStack.Pop();
            _ifStep = parentStep;
            _currentBranch = parentBranch;
        }
        return this;
    }

    public IIfBuilder<TState> Delay(TimeSpan delay)
    {
        var step = new FlowStep
        {
            Type = StepType.Delay,
            DelayDuration = delay
        };
        _currentBranch.Add(step);
        return this;
    }

    public IIfBuilder<TState> ScheduleAt(Func<TState, DateTime> timeSelector)
    {
        var step = new FlowStep
        {
            Type = StepType.ScheduleAt,
            ScheduleTimeSelector = timeSelector,
            GetScheduleTime = state => timeSelector((TState)state)
        };
        _currentBranch.Add(step);
        return this;
    }
}

/// <summary>
/// If builder with result for Send steps that return values.
/// </summary>
internal class IfBuilderWithResult<TState, TResult>(IfBuilder<TState> parent, FlowStep step) : IIfBuilder<TState, TResult>
    where TState : class, IFlowState
{
    public IIfBuilder<TState> Into(Action<TState, TResult> setter)
    {
        step.ResultSetter = setter;
        // Also set the typed wrapper for execution
        step.SetResult = (state, result) => setter((TState)state, (TResult)result!);
        return parent;
    }
}
