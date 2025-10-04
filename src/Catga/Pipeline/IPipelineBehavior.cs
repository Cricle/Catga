using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline;

/// <summary>
/// Pipeline behavior for requests with response
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<CatgaResult<TResponse>>> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline behavior for requests without response
/// </summary>
public interface IPipelineBehavior<in TRequest> where TRequest : IRequest
{
    Task<CatgaResult> HandleAsync(
        TRequest request,
        Func<Task<CatgaResult>> next,
        CancellationToken cancellationToken = default);
}

