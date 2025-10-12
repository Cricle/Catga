using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;

namespace Catga;

/// <summary>Mediator for CQRS (AOT-compatible)</summary>
public interface ICatgaMediator
{
    public ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;

    public Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest;

    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;

    public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;

    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent;
}
