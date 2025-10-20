using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Core;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Observability;
using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Mediator;

/// <summary>High-performance Catga Mediator (AOT-compatible, lock-free)</summary>
public sealed class CatgaMediator : ICatgaMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly HandlerCache _handlerCache;

    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _handlerCache = new HandlerCache();
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
                    activity.SetTag(CatgaActivitySource.Tags.CorrelationId, message.CorrelationId.Value);
                    activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, message.CorrelationId.Value.ToString());
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
                var singletonBehaviors = singletonScope.ServiceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
                // Optimize: Prefer IList to avoid ToArray(), most DI containers return List<T>
                var singletonBehaviorsList = singletonBehaviors as IList<IPipelineBehavior<TRequest, TResponse>>
                    ?? singletonBehaviors.ToArray();

                var singletonResult = FastPath.CanUseFastPath(singletonBehaviorsList.Count)
                    ? await FastPath.ExecuteRequestDirectAsync(singletonHandler, request, cancellationToken)
                    : await PipelineExecutor.ExecuteAsync(request, singletonHandler, singletonBehaviorsList, cancellationToken);

                var singletonDuration = GetElapsedMilliseconds(startTimestamp);
                CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", singletonResult.IsSuccess.ToString()));
                CatgaDiagnostics.CommandDuration.Record(singletonDuration, new KeyValuePair<string, object?>("request_type", reqType));
                CatgaLog.CommandExecuted(_logger, reqType, message?.MessageId, singletonDuration, singletonResult.IsSuccess);

                activity?.SetTag(CatgaActivitySource.Tags.Success, singletonResult.IsSuccess);
                activity?.SetTag(CatgaActivitySource.Tags.Duration, singletonDuration);

                if (singletonResult.IsSuccess)
                    activity?.SetStatus(ActivityStatusCode.Ok);
                else
                {
                    activity?.SetTag(CatgaActivitySource.Tags.Error, singletonResult.Error ?? "Unknown error");
                    activity?.SetStatus(ActivityStatusCode.Error, singletonResult.Error);
                    CatgaLog.CommandFailed(_logger, singletonResult.Exception, reqType, message?.MessageId, singletonResult.Error ?? "Unknown");
                }

                return singletonResult;
            }

            // Standard path: Scoped/Transient handler
            using var scope = _serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(scopedProvider);
            if (handler == null)
            {
                CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", "false"));
                return CatgaResult<TResponse>.Failure($"No handler for {reqType}", new HandlerNotFoundException(reqType));
            }

            var behaviors = scopedProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
            // Optimize: Prefer IList to avoid ToArray(), most DI containers return List<T>
            var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>>
                ?? behaviors.ToArray();
            var result = FastPath.CanUseFastPath(behaviorsList.Count)
                ? await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken)
                : await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);

            var duration = GetElapsedMilliseconds(startTimestamp);
            CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", result.IsSuccess.ToString()));
            CatgaDiagnostics.CommandDuration.Record(duration, new KeyValuePair<string, object?>("request_type", reqType));
            CatgaLog.CommandExecuted(_logger, reqType, message?.MessageId, duration, result.IsSuccess);

            // Set result and duration tags
            activity?.SetTag(CatgaActivitySource.Tags.Success, result.IsSuccess);
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
        catch (Exception ex)
        {
            CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", "false"));
            RecordException(activity, ex);
            CatgaLog.CommandFailed(_logger, ex, reqType, message?.MessageId, ex.Message);
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PipelineExecutionFailed, isRetryable: false));
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
                    activity.SetTag(CatgaActivitySource.Tags.CorrelationId, message.CorrelationId.Value);
                    activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, message.CorrelationId.Value.ToString());
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

        CatgaDiagnostics.EventsPublished.Add(1, new("event_type", eventType), new("handler_count", handlerList.Count.ToString()));

        if (handlerList.Count == 1)
        {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 1);
            return;
        }

        var tasks = new Task[handlerList.Count];
        for (var i = 0; i < handlerList.Count; i++)
            tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
                activity.SetTag("catga.success", true);
                activity.SetTag("catga.duration.ms", GetElapsedMilliseconds(startTimestamp));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event handler failed: {HandlerType}", handlerType);

            if (activity != null)
            {
                activity.SetTag("catga.success", false);
                activity.SetTag("catga.error", ex.Message);
                activity.SetTag("catga.error.type", ex.GetType().Name);
                activity.SetTag("exception.message", ex.Message);
                activity.SetTag("exception.type", ex.GetType().FullName);
                activity.SetTag("exception.stacktrace", ex.StackTrace);
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => await requests.ExecuteBatchWithResultsAsync(request => SendAsync<TRequest, TResponse>(request, cancellationToken));

    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        if (requests == null)
            yield break;
        await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return await SendAsync<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent
        => await events.ExecuteBatchAsync(@event => PublishAsync(@event, cancellationToken));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddTag("exception.type", ExceptionTypeCache.GetFullTypeName(ex));
        activity?.AddTag("exception.message", ex.Message);
    }
}
