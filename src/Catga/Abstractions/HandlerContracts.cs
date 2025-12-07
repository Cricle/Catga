using System.Diagnostics.CodeAnalysis;
using Catga.Core;

using Catga.Abstractions;
namespace Catga.Abstractions;

/// <summary>Request handler with response</summary>
public interface IRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] in TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> where TRequest : IRequest<TResponse>
{
    public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Request handler without response</summary>
public interface IRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] in TRequest> where TRequest : IRequest
{
    public ValueTask<CatgaResult> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Event handler</summary>
public interface IEventHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] in TEvent> where TEvent : IEvent
{
    public ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generated event router contract - implemented by source generator for zero-reflection routing.
/// </summary>
public interface IGeneratedEventRouter
{
    // Returns true if a fast-path route exists for this event type
    bool TryRoute<TEvent>(IServiceProvider sp, TEvent @event, CancellationToken ct, out Task? dispatched) where TEvent : IEvent;
}

