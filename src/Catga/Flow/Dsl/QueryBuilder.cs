namespace Catga.Flow.Dsl;

/// <summary>
/// Query builder for query steps with result.
/// </summary>
internal class QueryBuilder<TState, TResult>(FlowStep step) : IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    public IStepBuilder<TState> Into(Action<TState, TResult> setter)
    {
        step.ResultSetter = setter;
        return new StepBuilder<TState>(step);
    }

    public IQueryBuilder<TState, TResult> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}
