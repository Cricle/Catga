using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline;

/// <summary>
/// Pipeline behavior for requests with response
/// Optimized: Use ValueTask to reduce heap allocations
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline behavior for requests without response
/// Optimized: Use ValueTask to reduce heap allocations
/// </summary>
public interface IPipelineBehavior<in TRequest> where TRequest : IRequest
{
    public ValueTask<CatgaResult> HandleAsync(
        TRequest request,
        PipelineDelegate next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline delegate - Optimized delegate type
/// </summary>
public delegate ValueTask<CatgaResult<TResponse>> PipelineDelegate<TResponse>();

/// <summary>
/// Pipeline delegate without response
/// </summary>
public delegate ValueTask<CatgaResult> PipelineDelegate();

