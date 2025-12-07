using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Observability;
using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Buffers;

namespace Catga;

/// <summary>High-performance Catga Mediator (AOT-compatible, lock-free)</summary>
public sealed class CatgaMediator : ICatgaMediator, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly bool _enableLogging;
    private readonly bool _enableTracing;

    // Static handler cache - shared across all CatgaMediator instances
    private static readonly ConcurrentDictionary<Type, object?> _handlerCache = new();
    private static readonly ConcurrentDictionary<Type, object?> _behaviorCache = new();

    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger)
        : this(serviceProvider, logger, serviceProvider.GetService<CatgaOptions>() ?? new CatgaOptions())
    {
    }

    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger,
        CatgaOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _enableLogging = options.EnableLogging;
        _enableTracing = options.EnableTracing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        var startTimestamp = _enableTracing ? Stopwatch.GetTimestamp() : 0;
        var reqType = _enableLogging || _enableTracing ? TypeNameCache<TRequest>.Name : null;

        // Only start activity if tracing is enabled
        using var activity = _enableTracing ? ObservabilityHooks.StartCommand(reqType!, request) : null;

        if (request is null)
        {
            var ex = new CatgaException("Request is null");
            if (_enableTracing) ObservabilityHooks.RecordCommandError(reqType!, ex, activity);
            if (_enableLogging) CatgaLog.CommandFailed(_logger, ex, reqType ?? "Unknown", null, "Request is null");
            return CatgaResult<TResponse>.Failure(ex.Message, ex);
        }

        if (_enableLogging) CatgaLog.CommandExecuting(_logger, reqType!, request.MessageId, request.CorrelationId);

        try
        {
            // Fast-path: get handler from static cache
            var handler = GetCachedHandler<TRequest, TResponse>();
            if (handler == null)
            {
                if (_enableTracing) ObservabilityHooks.RecordCommandError(reqType ?? TypeNameCache<TRequest>.Name, new HandlerNotFoundException(TypeNameCache<TRequest>.Name), activity);
                return CatgaResult<TResponse>.Failure($"No handler for {TypeNameCache<TRequest>.Name}", new HandlerNotFoundException(TypeNameCache<TRequest>.Name));
            }

            // Fast-path: get behaviors from static cache
            var behaviorsList = GetCachedBehaviors<TRequest, TResponse>();

            if (behaviorsList.Count == 0)
                return await ExecuteRequestDirectAsync(handler, request, cancellationToken).ConfigureAwait(false);

            // Execute with pipeline behaviors
            return await ExecuteRequestWithMetricsAsync(handler, request,
                _serviceProvider, activity as Activity, request, reqType, startTimestamp, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_enableTracing)
            {
                ObservabilityHooks.RecordCommandError(reqType ?? TypeNameCache<TRequest>.Name, ex, activity);
                RecordException(activity as Activity, ex);
            }
            if (_enableLogging) CatgaLog.CommandFailed(_logger, ex, reqType ?? TypeNameCache<TRequest>.Name, request.MessageId, ex.Message);
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
        string? reqType,
        long startTimestamp,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var behaviors = scopedProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>>
            ?? behaviors.ToArray();

        // Record pipeline behavior count (only if tracing enabled)
        if (_enableTracing) ObservabilityHooks.RecordPipelineBehaviorCount(reqType ?? TypeNameCache<TRequest>.Name, behaviorsList.Count);

        // Measure pipeline execution duration separately (only if tracing enabled)
        var pipelineStart = _enableTracing ? Stopwatch.GetTimestamp() : 0;
        var result = behaviorsList.Count == 0
            ? await ExecuteRequestDirectAsync(handler, request, cancellationToken)
            : await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);

        if (_enableTracing)
        {
            var pipelineElapsedTicks = Stopwatch.GetTimestamp() - pipelineStart;
            var pipelineDurationMs = pipelineElapsedTicks * 1000.0 / Stopwatch.Frequency;
            ObservabilityHooks.RecordPipelineDuration(reqType ?? TypeNameCache<TRequest>.Name, pipelineDurationMs);

            // Record metrics
            var duration = GetElapsedMilliseconds(startTimestamp);
            ObservabilityHooks.RecordCommandResult(reqType ?? TypeNameCache<TRequest>.Name, result.IsSuccess, duration, activity);
        }

        if (_enableLogging)
        {
            var duration = _enableTracing ? GetElapsedMilliseconds(startTimestamp) : 0;
            CatgaLog.CommandExecuted(_logger, reqType ?? TypeNameCache<TRequest>.Name, message?.MessageId, duration, result.IsSuccess);
            if (!result.IsSuccess)
                CatgaLog.CommandFailed(_logger, result.Exception, reqType ?? TypeNameCache<TRequest>.Name, message?.MessageId, result.Error ?? "Unknown");
        }

        return result;
    }

    public async ValueTask<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        // All handlers are now Singleton - no scope creation needed
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest>>();
        if (handler == null)
            return CatgaResult.Failure($"No handler for {TypeNameCache<TRequest>.Name}", new HandlerNotFoundException(TypeNameCache<TRequest>.Name));
        return await handler.HandleAsync(request, cancellationToken);
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (@event is null)
            return;

        var eventType = _enableLogging || _enableTracing ? TypeNameCache<TEvent>.Name : null;

        using var activity = _enableTracing ? ObservabilityHooks.StartEventPublish(eventType!, @event) : null;

        if (_enableLogging) CatgaLog.EventPublishing(_logger, eventType!, @event.MessageId);

        // All handlers are now Singleton - no scope creation needed

        // Fast-path: use generated router if available
        var generatedRouter = _serviceProvider.GetService<IGeneratedEventRouter>();
        if (generatedRouter != null && generatedRouter.TryRoute(_serviceProvider, @event, cancellationToken, out var dispatchedTask))
        {
            if (dispatchedTask != null)
                await dispatchedTask.ConfigureAwait(false);
            if (_enableLogging) CatgaLog.EventPublished(_logger, eventType!, @event.MessageId, 0);
            return;
        }

        // Enumerate handlers without allocating a List
        var handlersEnumerable = _serviceProvider.GetServices<IEventHandler<TEvent>>();
        var pool = ArrayPool<IEventHandler<TEvent>>.Shared;
        var arr = pool.Rent(8);
        var count = 0;
        try
        {
            foreach (var h in handlersEnumerable)
            {
                if (count == arr.Length)
                {
                    var bigger = pool.Rent(arr.Length * 2);
                    Array.Copy(arr, 0, bigger, 0, arr.Length);
                    pool.Return(arr, clearArray: true);
                    arr = bigger;
                }
                arr[count++] = h;
            }

            if (count == 0)
            {
                if (_enableLogging) CatgaLog.EventPublished(_logger, eventType!, @event.MessageId, 0);
                return;
            }

            if (_enableTracing) ObservabilityHooks.RecordEventPublished(eventType!, count);

            if (count == 1)
            {
                await HandleEventSafelyAsync(arr[0], @event, cancellationToken).ConfigureAwait(false);
                if (_enableLogging) CatgaLog.EventPublished(_logger, eventType!, @event.MessageId, 1);
                return;
            }

            // Batch processing with pooled Task arrays
            var chunkSize = BatchOperationHelper.DefaultChunkSize;
            if (count <= chunkSize)
            {
                var taskPool = ArrayPool<Task>.Shared;
                var tasks = taskPool.Rent(count);
                try
                {
                    for (int i = 0; i < count; i++)
                        tasks[i] = HandleEventSafelyAsync(arr[i], @event, cancellationToken);
                    await Task.WhenAll(tasks.AsSpan(0, count).ToArray()).ConfigureAwait(false);
                }
                finally
                {
                    taskPool.Return(tasks, clearArray: true);
                }
            }
            else
            {
                var taskPool = ArrayPool<Task>.Shared;
                for (int offset = 0; offset < count; offset += chunkSize)
                {
                    var take = Math.Min(chunkSize, count - offset);
                    var tasks = taskPool.Rent(take);
                    try
                    {
                        for (int i = 0; i < take; i++)
                            tasks[i] = HandleEventSafelyAsync(arr[offset + i], @event, cancellationToken);
                        await Task.WhenAll(tasks.AsSpan(0, take).ToArray()).ConfigureAwait(false);
                    }
                    finally
                    {
                        taskPool.Return(tasks, clearArray: true);
                    }
                }
            }

            if (_enableLogging) CatgaLog.EventPublished(_logger, eventType!, @event?.MessageId, count);
        }
        finally
        {
            pool.Return(arr, clearArray: true);
        }
    }

    private async Task HandleEventSafelyAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IEventHandler<TEvent> handler, TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        Activity? activity = null;
        string? handlerType = null;
        string? eventType = null;
        long startTimestamp = 0;

        if (_enableTracing && CatgaActivitySource.Source.HasListeners())
        {
            handlerType = handler.GetType().Name;
            eventType = typeof(TEvent).Name;
            activity = CatgaActivitySource.Source.StartActivity($"Handle: {eventType}", ActivityKind.Consumer);

            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
                activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
                activity.SetTag(CatgaActivitySource.Tags.HandlerType, handlerType);

                activity.AddActivityEvent(
                    CatgaActivitySource.Events.EventReceived,
                    ("event.type", eventType),
                    ("handler", handlerType));

                activity.SetTag("catga.event.id", @event.MessageId);
                if (@event.CorrelationId.HasValue)
                    activity.SetTag("catga.correlation_id", @event.CorrelationId.Value);

                if (@event is IActivityTagProvider enricher)
                    enricher.Enrich(activity);

                startTimestamp = Stopwatch.GetTimestamp();
            }
        }

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
            if (_enableLogging)
            {
                handlerType ??= handler.GetType().Name;
                eventType ??= typeof(TEvent).Name;
                CatgaLog.EventHandlerFailed(_logger, ex, eventType, @event.MessageId, handlerType);
            }

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
        finally
        {
            activity?.Dispose();
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

    /// <summary>
    /// Get handler from static cache (first access resolves and caches)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IRequestHandler<TRequest, TResponse>? GetCachedHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var key = typeof(TRequest);
        if (_handlerCache.TryGetValue(key, out var cached))
            return cached as IRequestHandler<TRequest, TResponse>;

        // First access: resolve and cache
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        _handlerCache.TryAdd(key, handler);
        return handler;
    }

    /// <summary>
    /// Get behaviors from static cache (first access resolves and caches)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IList<IPipelineBehavior<TRequest, TResponse>> GetCachedBehaviors<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var key = typeof(TRequest);
        if (_behaviorCache.TryGetValue(key, out var cached))
            return (cached as IList<IPipelineBehavior<TRequest, TResponse>>) ?? Array.Empty<IPipelineBehavior<TRequest, TResponse>>();

        // First access: resolve and cache
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToArray();
        _behaviorCache.TryAdd(key, behaviorsList);
        return behaviorsList;
    }
}
