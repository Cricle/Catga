using System.Buffers;
using System.Runtime.CompilerServices;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline;

/// <summary>Optimized pipeline executor (zero allocation, AOT-compatible)</summary>
public static class PipelineExecutor {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request, IRequestHandler<TRequest, TResponse> handler,
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse> {
        if (behaviors.Count == 0)
            return await handler.HandleAsync(request, cancellationToken);

        var context = new PipelineContext<TRequest, TResponse> {
            Request = request,
            Handler = handler,
            Behaviors = behaviors,
            CancellationToken = cancellationToken
        };
        return await ExecuteBehaviorAsync(context, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync<TRequest, TResponse>(
        PipelineContext<TRequest, TResponse> context, int index) where TRequest : IRequest<TResponse> {
        if (index >= context.Behaviors.Count)
            return await context.Handler.HandleAsync(context.Request, context.CancellationToken);

        var behavior = context.Behaviors[index];
        PipelineDelegate<TResponse> next = () => ExecuteBehaviorAsync(context, index + 1);
        return await behavior.HandleAsync(context.Request, next, context.CancellationToken);
    }

    private struct PipelineContext<TRequest, TResponse> where TRequest : IRequest<TResponse> {
        public TRequest Request;
        public IRequestHandler<TRequest, TResponse> Handler;
        public IList<IPipelineBehavior<TRequest, TResponse>> Behaviors;
        public CancellationToken CancellationToken;
    }
}

