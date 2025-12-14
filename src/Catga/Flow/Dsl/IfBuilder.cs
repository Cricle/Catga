using System.Linq.Expressions;
using Catga.Abstractions;

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

    public IIfBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
        _currentBranch.Add(step);
        return this;
    }

    public IIfBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Send, HasResult = true, RequestFactory = factory };
        _currentBranch.Add(step);
        return new IfBuilderWithResult<TState, TResult>(this, step);
    }

    public IIfBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        var step = new FlowStep { Type = StepType.Publish, RequestFactory = factory };
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
}

/// <summary>
/// If builder with result for Send steps that return values.
/// </summary>
internal class IfBuilderWithResult<TState, TResult> : IIfBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly IfBuilder<TState> _parent;
    private readonly FlowStep _step;

    public IfBuilderWithResult(IfBuilder<TState> parent, FlowStep step)
    {
        _parent = parent;
        _step = step;
    }

    public IIfBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            _step.ResultPropertyName = member.Member.Name;
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            _step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return _parent;
    }

    public IIfBuilder<TState> Into(Action<TState, TResult> setter)
    {
        _step.ResultSetter = setter;
        return _parent;
    }
}
