using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Query builder for query steps with result.
/// </summary>
internal class QueryBuilder<TState, TResult>(FlowStep step) : IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    public IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            step.ResultPropertyName = member.Member.Name;
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return new StepBuilder<TState>(step);
    }

    public IQueryBuilder<TState, TResult> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}
