using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that publishes a Fault&lt;TRequest&gt; event when a handler fails.
/// Equivalent to MassTransit's fault publishing.
/// Register via services.AddCatga().WithFaultPublishing().
/// </summary>
public sealed class FaultPublishingBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Use IServiceProvider to lazily resolve ICatgaMediator and break circular dependency
    private readonly IServiceProvider _sp;

    public FaultPublishingBehavior(IServiceProvider sp) => _sp = sp;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var result = await next();

        if (!result.IsSuccess)
        {
            var fault = new Fault<TRequest>(request, result.Exception, result.ErrorCode, result.Error);
            try
            {
                var mediator = _sp.GetService<ICatgaMediator>();
                if (mediator != null)
                    await mediator.PublishAsync(fault, cancellationToken);
            }
            catch { /* swallow — fault publishing must not affect the original result */ }
        }

        return result;
    }
}
