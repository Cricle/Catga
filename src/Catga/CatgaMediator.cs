using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Observability;
using Catga.Pipeline;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Catga;

/// <summary>High-performance Catga Mediator (AOT-compatible, lock-free)</summary>
public sealed class CatgaMediator : ICatgaMediator, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly HandlerCache _handlerCache;


    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger,
        CatgaOptions? options = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _handlerCache = new HandlerCache();

        options ??= new CatgaOptions();

        // Removed custom circuit breaker & concurrency limiter (Polly-based resilience is applied via behaviors)
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var reqType = TypeNameCache<TRequest>.Name;
        var message = request as IMessage;

        // Optimize: Create activity only if there are active listeners
        using var activity = CatgaActivitySource.Source.HasListeners()
            ? CatgaActivitySource.Source.StartActivity(
                $"Command: {reqType}",  // Use cached reqType instead of TypeNameCache lookup
                ActivityKind.Internal)
            : null;

        // Set Catga-specific tags for Jaeger (only if activity was created)
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.CatgaType, "command");
            activity.SetTag(CatgaActivitySource.Tags.RequestType, reqType);
            activity.SetTag(CatgaActivitySource.Tags.MessageType, reqType);

            if (message != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
                if (message.CorrelationId.HasValue)
                {
                    var correlationId = message.CorrelationId.Value;
                    activity.SetTag(CatgaActivitySource.Tags.CorrelationId, correlationId);
                    // ✅ Avoid boxing: format long directly to stack-allocated buffer
                    Span<char> buffer = stackalloc char[20];
                    correlationId.TryFormat(buffer, out int written);
                    activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, new string(buffer[..written]));
                }
            }
        }

        CatgaLog.CommandExecuting(_logger, reqType, message?.MessageId, message?.CorrelationId);

        try
        {
            // Optimize: Try to resolve Singleton handler first (skip CreateScope for performance)
            var singletonHandler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();

            if (singletonHandler != null)
            {
                // Fast path: Singleton handler found, but still need scope for behaviors
                using var singletonScope = _serviceProvider.CreateScope();
                return await ExecuteRequestWithMetricsAsync(singletonHandler, request,
                    singletonScope.ServiceProvider, activity, message, reqType, startTimestamp, cancellationToken);
            }

            // Standard path: Scoped/Transient handler
            using var scope = _serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(scopedProvider);
            if (handler == null)
            {
#if NET8_0_OR_GREATER
                CatgaDiagnostics.CommandsExecuted.Add(1,
                    new KeyValuePair<string, object?>("request_type", reqType),
                    new KeyValuePair<string, object?>("success", "false"));
#else
                var _tags_nohandler = new TagList { { "request_type", reqType }, { "success", "false" } };
                CatgaDiagnostics.CommandsExecuted.Add(1, _tags_nohandler);
#endif
                return CatgaResult<TResponse>.Failure($"No handler for {reqType}", new HandlerNotFoundException(reqType));
            }

            return await ExecuteRequestWithMetricsAsync(handler, request,
                scopedProvider, activity, message, reqType, startTimestamp, cancellationToken);
        }
        catch (Exception ex)
        {
#if NET8_0_OR_GREATER
            CatgaDiagnostics.CommandsExecuted.Add(1,
                new KeyValuePair<string, object?>("request_type", reqType),
                new KeyValuePair<string, object?>("success", "false"));
#else
            var _tags_catch = new TagList { { "request_type", reqType }, { "success", "false" } };
            CatgaDiagnostics.CommandsExecuted.Add(1, _tags_catch);
#endif
            RecordException(activity, ex);
            CatgaLog.CommandFailed(_logger, ex, reqType, message?.MessageId, ex.Message);
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PipelineFailed, isRetryable: false));
        }
    }

    /// <summary>
    /// Get elapsed time in milliseconds from a Stopwatch timestamp
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

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
        {
#if NET8_0_OR_GREATER
            CatgaDiagnostics.PipelineBehaviorCount.Record(behaviorsList.Count,
                new KeyValuePair<string, object?>("request_type", reqType));
#else
            var _tags_behavior_count = new TagList { { "request_type", reqType } };
            CatgaDiagnostics.PipelineBehaviorCount.Record(behaviorsList.Count, _tags_behavior_count);
#endif
        }

        // Measure pipeline execution duration separately
        var pipelineStart = Stopwatch.GetTimestamp();
        var result = FastPath.CanUseFastPath(behaviorsList.Count)
            ? await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken)
            : await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);
        var pipelineElapsedTicks = Stopwatch.GetTimestamp() - pipelineStart;
        var pipelineDurationMs = pipelineElapsedTicks * 1000.0 / Stopwatch.Frequency;
        {
#if NET8_0_OR_GREATER
            CatgaDiagnostics.PipelineDuration.Record(pipelineDurationMs,
                new KeyValuePair<string, object?>("request_type", reqType));
#else
            var _tags_pipeline_dur = new TagList { { "request_type", reqType } };
            CatgaDiagnostics.PipelineDuration.Record(pipelineDurationMs, _tags_pipeline_dur);
#endif
        }

        // Record metrics and logs
        var duration = GetElapsedMilliseconds(startTimestamp);
        var successValue = result.IsSuccess ? "true" : "false";  // Avoid ToString() allocation
#if NET8_0_OR_GREATER
        CatgaDiagnostics.CommandsExecuted.Add(1,
            new KeyValuePair<string, object?>("request_type", reqType),
            new KeyValuePair<string, object?>("success", successValue));
        CatgaDiagnostics.CommandDuration.Record(duration,
            new KeyValuePair<string, object?>("request_type", reqType));
#else
        var _tags_executed = new TagList { { "request_type", reqType }, { "success", successValue } };
        var _tags_duration = new TagList { { "request_type", reqType } };
        CatgaDiagnostics.CommandsExecuted.Add(1, _tags_executed);
        CatgaDiagnostics.CommandDuration.Record(duration, _tags_duration);
#endif
        CatgaLog.CommandExecuted(_logger, reqType, message?.MessageId, duration, result.IsSuccess);

        // Set activity tags
        activity?.SetTag(CatgaActivitySource.Tags.Success, result.IsSuccess);
        activity?.SetTag(CatgaActivitySource.Tags.PipelineBehaviorCount, behaviorsList.Count);
        activity?.SetTag(CatgaActivitySource.Tags.Duration, duration);

        if (result.IsSuccess)
            activity?.SetStatus(ActivityStatusCode.Ok);
        else
        {
            activity?.SetTag(CatgaActivitySource.Tags.Error, result.Error ?? "Unknown error");
            activity?.SetStatus(ActivityStatusCode.Error, result.Error);
            CatgaLog.CommandFailed(_logger, result.Exception, reqType, message?.MessageId, result.Error ?? "Unknown");
        }

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
        var eventType = TypeNameCache<TEvent>.Name;
        var message = @event as IMessage;

        // Optimize: Create activity only if there are active listeners
        using var activity = CatgaActivitySource.Source.HasListeners()
            ? CatgaActivitySource.Source.StartActivity(
                $"Event: {eventType}",
                ActivityKind.Producer)
            : null;

        // Set Catga-specific tags for Jaeger (only if activity was created)
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
            activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
            activity.SetTag(CatgaActivitySource.Tags.EventName, eventType);
            activity.SetTag(CatgaActivitySource.Tags.MessageType, eventType);

            if (message != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
                if (message.CorrelationId.HasValue)
                {
                    var correlationId = message.CorrelationId.Value;
                    activity.SetTag(CatgaActivitySource.Tags.CorrelationId, correlationId);
                    // ✅ Avoid boxing: format long directly to stack-allocated buffer
                    Span<char> buffer = stackalloc char[20];
                    correlationId.TryFormat(buffer, out int written);
                    activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, new string(buffer[..written]));
                }
            }
        }

        // Record event publication timeline event
        activity?.AddActivityEvent(CatgaActivitySource.Events.EventPublished, ("event.type", eventType));

        CatgaLog.EventPublishing(_logger, eventType, message?.MessageId);

        using var scope = _serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        // Fast-path: use generated router if available
        var generatedRouter = scopedProvider.GetService<IGeneratedEventRouter>();
        if (generatedRouter != null && generatedRouter.TryRoute(scopedProvider, @event, cancellationToken, out var dispatchedTask))
        {
            if (dispatchedTask != null)
                await dispatchedTask.ConfigureAwait(false);
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 0);
            return;
        }

        var handlerList = _handlerCache.GetEventHandlers<IEventHandler<TEvent>>(scopedProvider);
        if (handlerList.Count == 0)
        {
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 0);
            return;
        }

        // ✅ Use TagList (stack allocated) + avoid int.ToString() allocation
        Span<char> countBuffer = stackalloc char[10];
        handlerList.Count.TryFormat(countBuffer, out int charsWritten);
        var handlerCount = new string(countBuffer[..charsWritten]);
        {
#if NET8_0_OR_GREATER
            CatgaDiagnostics.EventsPublished.Add(1,
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("handler_count", handlerCount));
#else
            var _tags_event = new TagList { { "event_type", eventType }, { "handler_count", handlerCount } };
            CatgaDiagnostics.EventsPublished.Add(1, _tags_event);
#endif
        }

        if (handlerList.Count == 1)
        {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 1);
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

        CatgaLog.EventPublished(_logger, eventType, message?.MessageId, handlerList.Count);
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

            if (@event is IMessage message)
            {
                activity.SetTag("catga.event.id", message.MessageId);
                if (message.CorrelationId.HasValue)
                    activity.SetTag("catga.correlation_id", message.CorrelationId.Value);
            }

            // Capture event payload for Jaeger UI (debug-only, graceful degradation on AOT)
            ActivityPayloadCapture.CaptureEvent(activity, @event);
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
            _logger.LogError(ex, "Event handler failed: {HandlerType}", handlerType);

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
        activity?.AddTag("exception.type", ExceptionTypeCache.GetFullTypeName(ex));
        activity?.AddTag("exception.message", ex.Message);
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}
