using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Pipeline;

/// <summary>Optimized pipeline executor (zero allocation, AOT-compatible)</summary>
public static class PipelineExecutor
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TRequest request, IRequestHandler<TRequest, TResponse> handler,
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        if (behaviors.Count == 0)
        {
            if (!CatgaActivitySource.Source.HasListeners())
                return await handler.HandleAsync(request, cancellationToken);

            using var span = CatgaActivitySource.Source.StartActivity($"Pipeline.Handler: {handler.GetType().Name}", ActivityKind.Internal);
            var startTicks = Stopwatch.GetTimestamp();
            try
            {
                var result = await handler.HandleAsync(request, cancellationToken);
                if (span != null)
                {
                    var durationMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                    span.SetTag(CatgaActivitySource.Tags.RequestType, TypeNameCache<TRequest>.Name);
                    span.SetTag(CatgaActivitySource.Tags.HandlerType, handler.GetType().Name);
                    span.SetTag(CatgaActivitySource.Tags.Duration, durationMs);
                    span.SetTag(CatgaActivitySource.Tags.Success, result.IsSuccess);
                }
                return result;
            }
            catch (Exception ex)
            {
                span?.SetError(ex);
                throw;
            }
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
        {
            if (!CatgaActivitySource.Source.HasListeners())
                return await context.Handler.HandleAsync(context.Request, context.CancellationToken);

            using var handlerSpan = CatgaActivitySource.Source.StartActivity($"Pipeline.Handler: {context.Handler.GetType().Name}", ActivityKind.Internal);
            var handlerStart = Stopwatch.GetTimestamp();
            try
            {
                var result = await context.Handler.HandleAsync(context.Request, context.CancellationToken);
                if (handlerSpan != null)
                {
                    var durationMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
                    handlerSpan.SetTag(CatgaActivitySource.Tags.RequestType, TypeNameCache<TRequest>.Name);
                    handlerSpan.SetTag(CatgaActivitySource.Tags.HandlerType, context.Handler.GetType().Name);
                    handlerSpan.SetTag(CatgaActivitySource.Tags.Duration, durationMs);
                    handlerSpan.SetTag(CatgaActivitySource.Tags.Success, result.IsSuccess);
                }
                return result;
            }
            catch (Exception ex)
            {
                handlerSpan?.SetError(ex);
                throw;
            }
        }

        var behavior = context.Behaviors[index];
        ValueTask<CatgaResult<TResponse>> next() => ExecuteBehaviorAsync(context, index + 1);

        var start = Stopwatch.GetTimestamp();
        Activity? span = null;
        if (CatgaActivitySource.Source.HasListeners())
        {
            span = CatgaActivitySource.Source.StartActivity($"Pipeline.Behavior: {behavior.GetType().Name}", ActivityKind.Internal);
            span?.SetTag(CatgaActivitySource.Tags.RequestType, TypeNameCache<TRequest>.Name);
            span?.SetTag("catga.behavior.type", behavior.GetType().Name);
        }

        try
        {
            var result = await behavior.HandleAsync(context.Request, next, context.CancellationToken);
            var elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            CatgaDiagnostics.PipelineBehaviorDuration.Record(elapsedMs,
                new KeyValuePair<string, object?>("request_type", TypeNameCache<TRequest>.Name),
                new KeyValuePair<string, object?>("behavior_type", behavior.GetType().Name));
            if (span != null)
            {
                span.SetTag(CatgaActivitySource.Tags.Duration, elapsedMs);
                span.SetTag(CatgaActivitySource.Tags.Success, result.IsSuccess);
            }
            return result;
        }
        catch (Exception ex)
        {
            span?.SetError(ex);
            throw;
        }
        finally
        {
            span?.Dispose();
        }
    }

    private struct PipelineContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> where TRequest : IRequest<TResponse>
    {
        public TRequest Request;
        public IRequestHandler<TRequest, TResponse> Handler;
        public IList<IPipelineBehavior<TRequest, TResponse>> Behaviors;
        public CancellationToken CancellationToken;
    }
}

