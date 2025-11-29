using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Observability;
using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga;

/// <summary>High-performance Catga Mediator (AOT-compatible, lock-free)</summary>
public sealed class CatgaMediator : ICatgaMediator, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;


    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    // Back-compat overload used by tests
    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger,
        CatgaOptions options)
        : this(serviceProvider, logger)
    {
        // Currently unused, but kept for API compatibility
        _ = options;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var reqType = TypeNameCache<TRequest>.Name;

        // Minimal hooks: no-op unless tracing/metrics explicitly enabled
        using var activity = ObservabilityHooks.StartCommand(reqType, request);

        if (request is null)
        {
            var ex = new CatgaException("Request is null");
            ObservabilityHooks.RecordCommandError(reqType, ex, activity);
            CatgaLog.CommandFailed(_logger, ex, reqType, null, "Request is null");
            return CatgaResult<TResponse>.Failure(ex.Message, ex);
        }

        CatgaLog.CommandExecuting(_logger, reqType, request.MessageId, request.CorrelationId);

        try
        {
            // Optimize: Try to resolve Singleton handler first (skip CreateScope for performance)
            var singletonHandler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();

            if (singletonHandler != null)
            {
                // Fast path: Singleton handler found, but still need scope for behaviors
                using var singletonScope = _serviceProvider.CreateScope();
                return await ExecuteRequestWithMetricsAsync(singletonHandler, request,
                    singletonScope.ServiceProvider, activity as Activity, request, reqType, startTimestamp, cancellationToken);
            }

            // Standard path: Scoped/Transient handler
            using var scope = _serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var handler = scopedProvider.GetService<IRequestHandler<TRequest, TResponse>>();
            if (handler == null)
            {
                ObservabilityHooks.RecordCommandError(reqType, new HandlerNotFoundException(reqType), activity);
                return CatgaResult<TResponse>.Failure($"No handler for {reqType}", new HandlerNotFoundException(reqType));
            }

            return await ExecuteRequestWithMetricsAsync(handler, request,
                scopedProvider, activity as Activity, request, reqType, startTimestamp, cancellationToken);
        }
        catch (Exception ex)
        {
            ObservabilityHooks.RecordCommandError(reqType, ex, activity);
            RecordException(activity as Activity, ex);
            CatgaLog.CommandFailed(_logger, ex, reqType, request.MessageId, ex.Message);
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PipelineFailed, isRetryable: false));
        }
    }

    /// <summary>
    /// Get elapsed time in milliseconds from a Stopwatch timestamp
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetElapsedMilliseconds(long startTimestamp) => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Execute request with pipeline and record metrics (DRY helper)
    /// </summary>
    private async ValueTask<CatgaResult<TResponse>> ExecuteRequestWithMetricsAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        TRequest request,
        IServiceProvider scopedProvider,
        Activity? activity,
        IMessage? message,
        string reqType,
        long startTimestamp,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var behaviors = scopedProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>>
            ?? behaviors.ToArray();

        // Record pipeline behavior count
        ObservabilityHooks.RecordPipelineBehaviorCount(reqType, behaviorsList.Count);

        // Measure pipeline execution duration separately
        var pipelineStart = Stopwatch.GetTimestamp();
        var result = behaviorsList.Count == 0
            ? await ExecuteRequestDirectAsync(handler, request, cancellationToken)
            : await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);
        var pipelineElapsedTicks = Stopwatch.GetTimestamp() - pipelineStart;
        var pipelineDurationMs = pipelineElapsedTicks * 1000.0 / Stopwatch.Frequency;
        ObservabilityHooks.RecordPipelineDuration(reqType, pipelineDurationMs);

        // Record metrics and logs
        var duration = GetElapsedMilliseconds(startTimestamp);
        ObservabilityHooks.RecordCommandResult(reqType, result.IsSuccess, duration, activity);
        CatgaLog.CommandExecuted(_logger, reqType, message?.MessageId, duration, result.IsSuccess);
        if (!result.IsSuccess)
            CatgaLog.CommandFailed(_logger, result.Exception, reqType, message?.MessageId, result.Error ?? "Unknown");

        return result;
    }

    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService<IRequestHandler<TRequest>>();
        if (handler == null)
            return CatgaResult.Failure($"No handler for {TypeNameCache<TRequest>.Name}", new HandlerNotFoundException(TypeNameCache<TRequest>.Name));
        return await handler.HandleAsync(request, cancellationToken);
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (@event is null)
            return;

        var eventType = TypeNameCache<TEvent>.Name;

        using var activity = ObservabilityHooks.StartEventPublish(eventType, @event);

        CatgaLog.EventPublishing(_logger, eventType, @event.MessageId);

        using var scope = _serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        // Fast-path: use generated router if available
        var generatedRouter = scopedProvider.GetService<IGeneratedEventRouter>();
        if (generatedRouter != null && generatedRouter.TryRoute(scopedProvider, @event, cancellationToken, out var dispatchedTask))
        {
            if (dispatchedTask != null)
                await dispatchedTask.ConfigureAwait(false);
            CatgaLog.EventPublished(_logger, eventType, @event.MessageId, 0);
            return;
        }

        var handlerList = scopedProvider.GetServices<IEventHandler<TEvent>>().ToList();
        if (handlerList.Count == 0)
        {
            CatgaLog.EventPublished(_logger, eventType, @event.MessageId, 0);
            return;
        }

        ObservabilityHooks.RecordEventPublished(eventType, handlerList.Count);

        if (handlerList.Count == 1)
        {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            CatgaLog.EventPublished(_logger, eventType, @event.MessageId, 1);
            return;
        }

        // Batch processing
        if (handlerList.Count <= BatchOperationHelper.DefaultChunkSize)
        {
            // Small batch: execute all at once (fast path)
            var tasks = new Task[handlerList.Count];
            for (var i = 0; i < handlerList.Count; i++)
                tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            // Large batch: use chunked processing to prevent thread pool starvation
            await BatchOperationHelper.ExecuteBatchAsync(
                handlerList,
                handler => HandleEventSafelyAsync(handler, @event, cancellationToken),
                chunkSize: BatchOperationHelper.DefaultChunkSize).ConfigureAwait(false);
        }

        CatgaLog.EventPublished(_logger, eventType, @event?.MessageId, handlerList.Count);
    }

    private async Task HandleEventSafelyAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IEventHandler<TEvent> handler, TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        var handlerType = handler.GetType().Name;
        var eventType = typeof(TEvent).Name;

        // Optimize: Create activity only if there are active listeners
        using var activity = CatgaActivitySource.Source.HasListeners()
            ? CatgaActivitySource.Source.StartActivity($"Handle: {eventType}", ActivityKind.Consumer)
            : null;

        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
            activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
            activity.SetTag(CatgaActivitySource.Tags.HandlerType, handlerType);

            // Record event reception
            activity.AddActivityEvent(
                CatgaActivitySource.Events.EventReceived,
                ("event.type", eventType),
                ("handler", handlerType));

            activity.SetTag("catga.event.id", @event.MessageId);
            if (@event.CorrelationId.HasValue)
                activity.SetTag("catga.correlation_id", @event.CorrelationId.Value);

            // Zero-GC enrichment via interface (implemented by source-generated classes or manual)
            if (@event is IActivityTagProvider enricher && activity != null)
                enricher.Enrich(activity);
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);

            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.Success, true);
                activity.SetTag(CatgaActivitySource.Tags.Duration, GetElapsedMilliseconds(startTimestamp));
            }
        }
        catch (Exception ex)
        {
            CatgaLog.EventHandlerFailed(_logger, ex, eventType, @event.MessageId, handlerType);

            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.Success, false);
                activity.SetTag(CatgaActivitySource.Tags.Error, ex.Message);
                activity.SetTag(CatgaActivitySource.Tags.ErrorType, ex.GetType().Name);
                activity.SetTag("exception.message", ex.Message);
                activity.SetTag("exception.type", ex.GetType().FullName);
                activity.SetTag("exception.stacktrace", ex.StackTrace);
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(requests);
        return await requests.ExecuteBatchWithResultsAsync(request => SendAsync<TRequest, TResponse>(request, cancellationToken));
    }

    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(requests);

        await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return await SendAsync<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(events);
        await events.ExecuteBatchAsync(@event => PublishAsync(@event, cancellationToken));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        var typeName = ex.GetType().FullName ?? ex.GetType().Name;
        activity?.AddTag("exception.type", typeName);
        activity?.AddTag("exception.message", ex.Message);
    }

    public void Dispose()
    {
        // No-op: retained for back-compatibility with tests that call Dispose()
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<CatgaResult<TResponse>> ExecuteRequestDirectAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        try
        {
            return await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (CatgaException ex)
        {
            return CatgaResult<TResponse>.Failure($"Handler failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            return CatgaResult<TResponse>.Failure($"Handler failed: {ex.Message}");
        }
    }
}
