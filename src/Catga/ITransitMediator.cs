using Catga.Messages;
using Catga.Results;

namespace Catga;

/// <summary>
/// Mediator for sending requests and publishing events (AOT-compatible)
/// </summary>
public interface ITransitMediator
{
    /// <summary>
    /// Send a request and wait for response (AOT-compatible with explicit type parameters)
    /// </summary>
    Task<TransitResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// Send a request without expecting a response (AOT-compatible)
    /// </summary>
    Task<TransitResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Publish an event to all subscribers (AOT-compatible)
    /// </summary>
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default) where TEvent : IEvent;
}
