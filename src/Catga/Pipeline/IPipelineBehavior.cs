using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline;

/// <summary>
/// Pipeline behavior for requests with response
/// 🔥 优化: 使用 ValueTask 减少堆分配
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline behavior for requests without response
/// 🔥 优化: 使用 ValueTask 减少堆分配
/// </summary>
public interface IPipelineBehavior<in TRequest> where TRequest : IRequest
{
    ValueTask<CatgaResult> HandleAsync(
        TRequest request,
        PipelineDelegate next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline delegate - 优化的委托类型
/// </summary>
public delegate ValueTask<CatgaResult<TResponse>> PipelineDelegate<TResponse>();

/// <summary>
/// Pipeline delegate without response
/// </summary>
public delegate ValueTask<CatgaResult> PipelineDelegate();

