namespace Catga.Flow.Dsl;

/// <summary>
/// Publish builder interface.
/// </summary>
public interface IPublishBuilder<TState> where TState : class, IFlowState
{
    IPublishBuilder<TState> Tag(params string[] tags);
}
