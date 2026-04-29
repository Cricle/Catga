using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Abstractions;

namespace Catga;

/// <summary>Mediator for CQRS (AOT-compatible)</summary>
public interface ICatgaMediator
{
    public ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;

    public ValueTask<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest;

    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;

    public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;

    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// Schedule a command to be sent after a delay.
    /// Persisted via Outbox — survives restarts.
    /// </summary>
    public Task<long> SendLaterAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        TRequest request,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
        => SendAtAsync(request, DateTimeOffset.UtcNow.Add(delay), cancellationToken);

    /// <summary>
    /// Schedule a command to be sent at a specific time.
    /// </summary>
    public Task<long> SendAtAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        TRequest request,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
        => Task.FromResult(0L); // default: not supported without Outbox

    /// <summary>
    /// Schedule an event to be published after a delay.
    /// </summary>
    public Task<long> PublishLaterAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent @event,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
        => PublishAtAsync(@event, DateTimeOffset.UtcNow.Add(delay), cancellationToken);

    /// <summary>
    /// Schedule an event to be published at a specific time.
    /// </summary>
    public Task<long> PublishAtAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent @event,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
        => Task.FromResult(0L); // default: not supported without Outbox

    /// <summary>
    /// Cancel a previously scheduled message.
    /// </summary>
    public Task<bool> CancelScheduledAsync(long scheduleId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
