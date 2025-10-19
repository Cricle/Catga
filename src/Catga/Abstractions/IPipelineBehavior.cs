using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Messages;

namespace Catga.Pipeline;

/// <summary>Pipeline behavior with response (AOT-compatible)</summary>
public interface IPipelineBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] in TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> where TRequest : IRequest<TResponse>
{
    public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default);
}

/// <summary>Pipeline behavior without response (AOT-compatible)</summary>
public interface IPipelineBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] in TRequest> where TRequest : IRequest
{
    public ValueTask<CatgaResult> HandleAsync(TRequest request, PipelineDelegate next, CancellationToken cancellationToken = default);
}

/// <summary>Pipeline delegate with response</summary>
public delegate ValueTask<CatgaResult<TResponse>> PipelineDelegate<TResponse>();

/// <summary>Pipeline delegate without response</summary>
public delegate ValueTask<CatgaResult> PipelineDelegate();

