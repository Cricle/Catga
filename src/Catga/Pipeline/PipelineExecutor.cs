using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Pipeline;

public static class PipelineExecutor
{
    /// <summary>
    /// Maximum pipeline depth to prevent stack overflow.
    /// Typical applications have 5-10 behaviors, 100 is a safe upper limit.
    /// </summary>
    private const int MaxPipelineDepth = 100;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TRequest request, IRequestHandler<TRequest, TResponse> handler,
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        if (behaviors.Count == 0)
            return await handler.HandleAsync(request, cancellationToken);

        if (behaviors.Count > MaxPipelineDepth)
        {
            return CatgaResult<TResponse>.Failure(
                $"Pipeline depth ({behaviors.Count}) exceeds maximum allowed depth ({MaxPipelineDepth})");
        }

        var context = new PipelineContext<TRequest, TResponse>
        {
            Request = request,
            Handler = handler,
            Behaviors = behaviors,
            CancellationToken = cancellationToken
        };
        return await ExecuteBehaviorAsync(context, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        PipelineContext<TRequest, TResponse> context, int index) where TRequest : IRequest<TResponse>
    {
        if (index >= context.Behaviors.Count)
            return await context.Handler.HandleAsync(context.Request, context.CancellationToken);

        var behavior = context.Behaviors[index];
        ValueTask<CatgaResult<TResponse>> next() => ExecuteBehaviorAsync(context, index + 1);

        var start = Stopwatch.GetTimestamp();
        var result = await behavior.HandleAsync(context.Request, next, context.CancellationToken);
        var elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        CatgaDiagnostics.PipelineDuration.Record(elapsedMs, new KeyValuePair<string, object?>("request_type", TypeNameCache<TRequest>.Name));
        return result;
    }

    private struct PipelineContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> where TRequest : IRequest<TResponse>
    {
        public TRequest Request;
        public IRequestHandler<TRequest, TResponse> Handler;
        public IList<IPipelineBehavior<TRequest, TResponse>> Behaviors;
        public CancellationToken CancellationToken;
    }
}
