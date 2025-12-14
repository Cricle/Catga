using System.Linq.Expressions;

namespace Catga.Flow.Dsl;

/// <summary>
/// WhenAny builder interface.
/// </summary>
public interface IWhenAnyBuilder<TState> where TState : class, IFlowState
{
    IWhenAnyBuilder<TState> Timeout(TimeSpan timeout);
    IWhenAnyBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAny builder with result.
/// </summary>
public interface IWhenAnyBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IWhenAnyBuilder<TState, TResult> Timeout(TimeSpan timeout);
    IWhenAnyBuilder<TState, TResult> Tag(params string[] tags);
}
