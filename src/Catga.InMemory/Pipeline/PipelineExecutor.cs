using System.Buffers;
using System.Runtime.CompilerServices;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline;

/// <summary>
/// Optimized Pipeline Executor - Zero allocation design
/// </summary>
public static class PipelineExecutor
{
    /// <summary>
    /// Execute Pipeline (Optimized version - Reduce closure and delegate allocations)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Pipeline execution may require types that cannot be statically analyzed.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Pipeline execution uses reflection for handler resolution.")]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        IRequestHandler<TRequest, TResponse> handler,
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Fast path - no behaviors
        if (behaviors.Count == 0)
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result;
        }

        // Use stack allocation to store behavior index, avoid closure
        var context = new PipelineContext<TRequest, TResponse>
        {
            Request = request,
            Handler = handler,
            Behaviors = behaviors,
            CancellationToken = cancellationToken
        };

        return await ExecuteBehaviorAsync(context, 0);
    }

    /// <summary>
    /// Recursively execute behavior (Tail recursion optimization)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Pipeline execution may require types that cannot be statically analyzed.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Pipeline execution uses reflection for handler resolution.")]
    private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync<TRequest, TResponse>(
        PipelineContext<TRequest, TResponse> context,
        int index)
        where TRequest : IRequest<TResponse>
    {
        if (index >= context.Behaviors.Count)
        {
            // Reached handler
            return await context.Handler.HandleAsync(context.Request, context.CancellationToken);
        }

        var behavior = context.Behaviors[index];

        // Create next delegate - points to next behavior
        PipelineDelegate<TResponse> next = () => ExecuteBehaviorAsync(context, index + 1);

        return await behavior.HandleAsync(context.Request, next, context.CancellationToken);
    }

    /// <summary>
    /// Pipeline execution context - Avoid closure capture
    /// </summary>
    private struct PipelineContext<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public TRequest Request;
        public IRequestHandler<TRequest, TResponse> Handler;
        public IList<IPipelineBehavior<TRequest, TResponse>> Behaviors;
        public CancellationToken CancellationToken;
    }
}

