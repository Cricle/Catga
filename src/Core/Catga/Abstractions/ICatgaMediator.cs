using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;

namespace Catga;

/// <summary>
/// Mediator for sending requests and publishing events (AOT-compatible)
/// </summary>
public interface ICatgaMediator
{
    /// <summary>
    /// Send a request and wait for response (AOT-compatible with explicit type parameters)
    /// </summary>
    public ValueTask<CatgaResult<TResponse>> SendAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// Send a request without expecting a response (AOT-compatible)
    /// </summary>
    public Task<CatgaResult> SendAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Publish an event to all subscribers (AOT-compatible)
    /// </summary>
    public Task PublishAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// Batch send requests - High performance batch processing
    /// </summary>
    public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// Stream send requests - Real-time processing of large data
    /// </summary>
    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest,
        TResponse>(
        IAsyncEnumerable<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// Batch publish events - High performance batch processing
    /// </summary>
    public Task PublishBatchAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
