using Catga.Abstractions;
using Catga.Core;
using Catga.Resilience;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Applies configured resilience policies around pipeline execution (AOT-safe, no reflection).
/// </summary>
public sealed class PollyBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IResiliencePipelineProvider _provider;

    public PollyBehavior(IResiliencePipelineProvider provider) => _provider = provider;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
        => await _provider.ExecuteMediatorAsync(_ => next(), cancellationToken);
}
