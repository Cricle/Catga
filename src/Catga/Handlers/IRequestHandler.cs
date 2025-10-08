using Catga.Messages;
using Catga.Results;

namespace Catga.Handlers;

/// <summary>
/// Handler for requests with response
/// </summary>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
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

