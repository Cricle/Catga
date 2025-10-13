using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Messages;
using Catga.Results;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Distributed;

/// <summary>
/// Simplified Distributed Mediator - delegates distribution to infrastructure (NATS/Redis)
/// <para>
/// Design Philosophy:
/// - Application layer focuses on CQRS message dispatch
/// - Infrastructure (NATS JetStream/Redis Cluster) handles distribution, load balancing, and high availability
/// - Service discovery delegated to K8s/Consul/Aspire
/// </para>
/// </summary>
public sealed class DistributedMediator : IDistributedMediator
{
    private readonly ICatgaMediator _localMediator;
    private readonly IMessageTransport _transport;
    private readonly ILogger<DistributedMediator> _logger;

    public DistributedMediator(
        ICatgaMediator localMediator,
        IMessageTransport transport,
        ILogger<DistributedMediator> logger)
    {
        _localMediator = localMediator;
        _transport = transport;
        _logger = logger;
    }

    // Delegate all Command/Query to local mediator (same process)
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);

    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        => await _localMediator.SendAsync(request, cancellationToken);

    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => await _localMediator.SendBatchAsync<TRequest, TResponse>(requests, cancellationToken);

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => _localMediator.SendStreamAsync<TRequest, TResponse>(requests, cancellationToken);

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent
        => await _localMediator.PublishBatchAsync(events, cancellationToken);

    /// <summary>
    /// Publish event: local first, then broadcast via message transport
    /// Distribution handled by NATS JetStream (consumer groups) or Redis Streams/Pub-Sub
    /// </summary>
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        // Local publish first
        await _localMediator.PublishAsync(@event, cancellationToken);

        // Broadcast to other nodes via message transport (NATS/Redis)
        // Infrastructure handles load balancing and delivery
        var subject = $"catga.events.{TypeNameCache<TEvent>.Name}";
        try
        {
            await _transport.SendAsync((object)@event!, subject, cancellationToken: cancellationToken);
            _logger.LogDebug("Broadcasted event {EventType} to subject {Subject}", TypeNameCache<TEvent>.Name, subject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast event {EventType}", TypeNameCache<TEvent>.Name);
        }
    }
}
