using System.Linq.Expressions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Query builder interface.
/// </summary>
public interface IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IQueryBuilder<TState, TResult> Tag(params string[] tags);
}
