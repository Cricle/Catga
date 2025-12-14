using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// WhenAll builder for parallel execution steps.
/// </summary>
internal class WhenAllBuilder<TState>(FlowStep step) : IWhenAllBuilder<TState> where TState : class, IFlowState
{
    public IWhenAllBuilder<TState> Timeout(TimeSpan timeout)
    {
        step.Timeout = timeout;
        return this;
    }

    public IWhenAllBuilder<TState> IfAnyFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        step.HasCompensation = true;
        step.CompensationFactory = factory;
        return this;
    }

    public IWhenAllBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}
