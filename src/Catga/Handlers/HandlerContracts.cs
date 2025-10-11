using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;

namespace Catga.Handlers;

#region Request Handlers

/// <summary>
/// Handler for requests with response
/// </summary>
public interface IRequestHandler<in TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TResponse> where TRequest : IRequest<TResponse>
{
    public Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler for requests without response
/// </summary>
public interface IRequestHandler<in TRequest> where TRequest : IRequest
{
    public Task<CatgaResult> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

#endregion

#region Event Handlers

/// <summary>
/// Handler for domain events
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

#endregion

