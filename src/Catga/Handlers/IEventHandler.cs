using Catga.Messages;

namespace Catga.Handlers;

/// <summary>
/// Handler for domain events
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

