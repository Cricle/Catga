namespace Catga.Flow.Dsl;

/// <summary>
/// Query builder interface.
/// </summary>
public interface IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Action<TState, TResult> setter);
    IQueryBuilder<TState, TResult> Tag(params string[] tags);
}
