using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// WhenAll builder interface.
/// </summary>
public interface IWhenAllBuilder<TState> where TState : class, IFlowState
{
    IWhenAllBuilder<TState> Timeout(TimeSpan timeout);
    IWhenAllBuilder<TState> IfAnyFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IWhenAllBuilder<TState> Tag(params string[] tags);
}
