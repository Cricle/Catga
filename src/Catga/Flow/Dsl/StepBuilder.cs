using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Step builder without result.
/// </summary>
internal class StepBuilder<TState> : IStepBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState>? _builder;
    private readonly FlowStep _step;

    public StepBuilder(FlowStep step) : this(null, step) { }

    public StepBuilder(FlowBuilder<TState>? builder, FlowStep step)
    {
        _builder = builder;
        _step = step;
    }

    public IStepBuilder<TState> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        _step.HasCompensation = true;
        _step.CompensationFactory = factory;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition, string errorMessage)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        _step.FailConditionMessage = errorMessage;
        return this;
    }

    public IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition)
    {
        _step.HasCondition = true;
        _step.ConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> Optional()
    {
        _step.IsOptional = true;
        return this;
    }

    public IStepBuilder<TState> Tag(params string[] tags)
    {
        _step.Tags.AddRange(tags);
        return this;
    }

    public IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnCompletedHook = true;
        _step.OnCompletedFactory = factory;
        return this;
    }

    public IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnFailedHook = true;
        _step.OnFailedFactory = factory;
        return this;
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition)
    {
        if (_builder == null)
            throw new InvalidOperationException("Cannot use If() without a FlowBuilder context");
        return _builder.If(condition);
    }

    public ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull
    {
        if (_builder == null)
            throw new InvalidOperationException("Cannot use Switch() without a FlowBuilder context");
        return _builder.Switch(selector);
    }
}

/// <summary>
/// Step builder with result.
/// </summary>
internal class StepBuilder<TState, TResult> : IStepBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _builder;
    private readonly FlowStep _step;

    public StepBuilder(FlowBuilder<TState> builder, FlowStep step)
    {
        _builder = builder;
        _step = step;
    }

    [RequiresDynamicCode("Into uses expression compilation")]
    public IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        var param = Expression.Parameter(typeof(TState), "s");
        var value = Expression.Parameter(typeof(TResult), "v");

        var visitor = new ParameterReplacer(property.Parameters[0], param);
        var targetExpression = visitor.Visit(property.Body);

        Expression assign;

        if (targetExpression is MethodCallExpression methodCall &&
            methodCall.Method.Name == "get_Item" &&
            methodCall.Object != null)
        {
            var setMethod = methodCall.Object.Type.GetMethod("set_Item")
                ?? throw new InvalidOperationException("Cannot find setter for indexer");
            assign = Expression.Call(methodCall.Object, setMethod, methodCall.Arguments[0], value);
        }
        else
        {
            assign = Expression.Assign(targetExpression, value);
        }

        _step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();

        if (property.Body is MemberExpression member)
        {
            _step.ResultPropertyName = member.Member.Name;
        }

        return new StepBuilder<TState>(_builder, _step);
    }

    [RequiresDynamicCode("Into uses expression compilation")]
    public IStepBuilder<TState> Into(Action<TState, TResult> setter)
    {
        _step.ResultSetter = setter;
        return new StepBuilder<TState>(_builder, _step);
    }

    public IStepBuilder<TState, TResult> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        _step.HasCompensation = true;
        _step.CompensationFactory = factory;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.IfFail<TRequest>(Func<TState, TRequest> factory)
    {
        _step.HasCompensation = true;
        _step.CompensationFactory = factory;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition, string errorMessage)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        _step.FailConditionMessage = errorMessage;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        return new StepBuilder<TState>(_step);
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition, string errorMessage)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        _step.FailConditionMessage = errorMessage;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition)
    {
        _step.HasCondition = true;
        _step.ConditionFactory = condition;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> Optional()
    {
        _step.IsOptional = true;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> Tag(params string[] tags)
    {
        _step.Tags.AddRange(tags);
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnCompletedHook = true;
        _step.OnCompletedFactory = factory;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnFailedHook = true;
        _step.OnFailedFactory = factory;
        return new StepBuilder<TState>(_builder, _step);
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition) => _builder.If(condition);

    public ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull
        => _builder.Switch(selector);
}
