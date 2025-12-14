using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Base class for step builders with common functionality.
/// Uses CRTP (Curiously Recurring Template Pattern) for fluent API.
/// </summary>
internal abstract class StepBuilderBase<TState, TSelf>
    where TState : class, IFlowState
    where TSelf : StepBuilderBase<TState, TSelf>
{
    protected readonly FlowBuilder<TState>? Builder;
    protected readonly FlowStep Step;

    protected StepBuilderBase(FlowBuilder<TState>? builder, FlowStep step)
    {
        Builder = builder;
        Step = step;
    }

    protected abstract TSelf Self { get; }

    public TSelf Tag(params string[] tags)
    {
        Step.Tags.AddRange(tags);
        return Self;
    }

    public TSelf OnlyWhen(Func<TState, bool> condition)
    {
        Step.HasCondition = true;
        Step.ConditionFactory = condition;
        return Self;
    }

    public TSelf Optional()
    {
        Step.IsOptional = true;
        return Self;
    }

    public TSelf OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        Step.HasOnCompletedHook = true;
        Step.OnCompletedFactory = factory;
        return Self;
    }

    public TSelf OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        Step.HasOnFailedHook = true;
        Step.OnFailedFactory = factory;
        return Self;
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition)
    {
        if (Builder == null)
            throw new InvalidOperationException("Cannot use If() without a FlowBuilder context");
        return Builder.If(condition);
    }

    public ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull
    {
        if (Builder == null)
            throw new InvalidOperationException("Cannot use Switch() without a FlowBuilder context");
        return Builder.Switch(selector);
    }
}
