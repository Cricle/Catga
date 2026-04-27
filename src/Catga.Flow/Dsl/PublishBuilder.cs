using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Publish builder for publish steps.
/// </summary>
internal class PublishBuilder<TState>(FlowStep step) : IPublishBuilder<TState> where TState : class, IFlowState
{
    public IPublishBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}
