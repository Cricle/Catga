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

/// <summary>
/// High-performance Catga Mediator implementation.
/// <para>Features: AOT-compatible, lock-free, static handler caching.</para>
/// <para>Use <c>options.Minimal()</c> for maximum performance (disables logging/tracing).</para>
/// </summary>
public sealed class CatgaMediator : ICatgaMediator, IDisposable
{
    #region Fields

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly bool _enableLogging;
    private readonly bool _enableTracing;

    /// <summary>Static handler cache shared across all instances for zero-allocation dispatch.</summary>
    private static readonly ConcurrentDictionary<Type, object?> _handlerCache = new();

    /// <summary>Static behavior cache shared across all instances.</summary>
    private static readonly ConcurrentDictionary<Type, object?> _behaviorCache = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new CatgaMediator with default options from DI.
    /// </summary>
    public CatgaMediator(IServiceProvider serviceProvider, ILogger<CatgaMediator> logger)
        : this(serviceProvider, logger, serviceProvider.GetService<CatgaOptions>() ?? new CatgaOptions())
    {
    }

    /// <summary>
    /// Creates a new CatgaMediator with explicit options.
    /// </summary>
    public CatgaMediator(IServiceProvider serviceProvider, ILogger<CatgaMediator> logger, CatgaOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _enableLogging = options.EnableLogging;
        _enableTracing = options.EnableTracing;
    }

    #endregion

    #region Public API - Commands & Queries

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // Fast-path: skip observability overhead when disabled
        return !_enableLogging && !_enableTracing
            ? SendAsyncFast<TRequest, TResponse>(request, cancellationToken)
            : SendAsyncWithObservability<TRequest, TResponse>(request, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest>>();
        if (handler == null)
            return CatgaResult.Failure($"No handler for {TypeNameCache<TRequest>.Name}", new HandlerNotFoundException(TypeNameCache<TRequest>.Name));

        return await handler.HandleAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(requests);
        return await requests.ExecuteBatchWithResultsAsync(request => SendAsync<TRequest, TResponse>(request, cancellationToken));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IAsyncEnumerable<TRequest> requests,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(requests);

        await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return await SendAsync<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Public API - Events

    /// <inheritdoc />
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        if (@event is null) return;

        // Fast-path: skip observability overhead when disabled
        if (!_enableLogging && !_enableTracing)
        {
            await PublishAsyncFast(@event, cancellationToken).ConfigureAwait(false);
            return;
        }

        await PublishAsyncWithObservability(@event, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(events);
        await events.ExecuteBatchAsync(@event => PublishAsync(@event, cancellationToken));
    }

    #endregion

    #region Fast Path (No Observability)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<CatgaResult<TResponse>> SendAsyncFast<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var handler = GetCachedHandler<TRequest, TResponse>();
        if (handler == null)
            return ValueTask.FromResult(CatgaResult<TResponse>.Failure(
                $"No handler for {TypeNameCache<TRequest>.Name}",
                new HandlerNotFoundException(TypeNameCache<TRequest>.Name)));

        var behaviors = GetCachedBehaviors<TRequest, TResponse>();
        return behaviors.Count == 0
            ? ExecuteHandlerAsync(handler, request, cancellationToken)
            : ExecutePipelineAsync(handler, request, behaviors, cancellationToken);
    }

    private async Task PublishAsyncFast<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        // Try generated router first
        var router = _serviceProvider.GetService<IGeneratedEventRouter>();
        if (router != null && router.TryRoute(_serviceProvider, @event, cancellationToken, out var task))
        {
            if (task != null) await task.ConfigureAwait(false);
            return;
        }

        // Fallback to dynamic resolution
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>().ToArray();
        if (handlers.Length == 0) return;

        if (handlers.Length == 1)
        {
            await handlers[0].HandleAsync(@event, cancellationToken).ConfigureAwait(false);
            return;
        }

        await Task.WhenAll(handlers.Select(h => h.HandleAsync(@event, cancellationToken).AsTask())).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<CatgaResult<TResponse>> ExecuteHandlerAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<CatgaResult<TResponse>> ExecutePipelineAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        TRequest request,
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        PipelineDelegate<TResponse> next = () => handler.HandleAsync(request, cancellationToken);
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentNext = next;
            next = () => behavior.HandleAsync(request, currentNext, cancellationToken);
        }
        return await next().ConfigureAwait(false);
    }

    #endregion

    private async ValueTask<CatgaResult<TResponse>> SendAsyncWithObservability<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken) where TRequest : IRequest<TResponse>
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
                return await ExecuteHandlerAsync(handler, request, cancellationToken).ConfigureAwait(false);

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
            ? await ExecuteHandlerAsync(handler, request, cancellationToken)
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

    #region Observability Path (With Logging/Tracing)

    private async Task PublishAsyncWithObservability<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        var eventType = TypeNameCache<TEvent>.Name;
        using var activity = _enableTracing ? ObservabilityHooks.StartEventPublish(eventType, @event) : null;

        if (_enableLogging) CatgaLog.EventPublishing(_logger, eventType, @event.MessageId);

        // Try generated router first
        var router = _serviceProvider.GetService<IGeneratedEventRouter>();
        if (router != null && router.TryRoute(_serviceProvider, @event, cancellationToken, out var task))
        {
            if (task != null) await task.ConfigureAwait(false);
            if (_enableLogging) CatgaLog.EventPublished(_logger, eventType, @event.MessageId, 0);
            return;
        }

        // Fallback to dynamic resolution with pooled arrays
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
                if (_enableLogging) CatgaLog.EventPublished(_logger, eventType, @event.MessageId, 0);
                return;
            }

            if (_enableTracing) ObservabilityHooks.RecordEventPublished(eventType, count);

            // Execute handlers
            if (count == 1)
            {
                await HandleEventWithTracingAsync(arr[0], @event, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var tasks = new Task[count];
                for (int i = 0; i < count; i++)
                    tasks[i] = HandleEventWithTracingAsync(arr[i], @event, cancellationToken);
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            if (_enableLogging) CatgaLog.EventPublished(_logger, eventType, @event.MessageId, count);
        }
        finally
        {
            pool.Return(arr, clearArray: true);
        }
    }

    private async Task HandleEventWithTracingAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        IEventHandler<TEvent> handler,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        Activity? activity = null;
        long startTimestamp = 0;

        if (_enableTracing && CatgaActivitySource.Source.HasListeners())
        {
            var handlerType = handler.GetType().Name;
            var eventType = typeof(TEvent).Name;
            activity = CatgaActivitySource.Source.StartActivity($"Handle: {eventType}", ActivityKind.Consumer);

            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
                activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
                activity.SetTag(CatgaActivitySource.Tags.HandlerType, handlerType);
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
            activity?.SetTag(CatgaActivitySource.Tags.Success, true);
        }
        catch (Exception ex)
        {
            if (_enableLogging)
                CatgaLog.EventHandlerFailed(_logger, ex, typeof(TEvent).Name, @event.MessageId, handler.GetType().Name);

            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.Success, false);
                activity.SetTag(CatgaActivitySource.Tags.Error, ex.Message);
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }
        finally
        {
            if (activity != null && startTimestamp > 0)
                activity.SetTag(CatgaActivitySource.Tags.Duration, GetElapsedMilliseconds(startTimestamp));
            activity?.Dispose();
        }
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetElapsedMilliseconds(long startTimestamp)
        => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        if (activity == null) return;
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddTag("exception.type", ex.GetType().FullName ?? ex.GetType().Name);
        activity.AddTag("exception.message", ex.Message);
    }

    #endregion

    #region Caching

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IRequestHandler<TRequest, TResponse>? GetCachedHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var key = typeof(TRequest);
        if (_handlerCache.TryGetValue(key, out var cached))
            return cached as IRequestHandler<TRequest, TResponse>;

        var handler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        _handlerCache.TryAdd(key, handler);
        return handler;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IList<IPipelineBehavior<TRequest, TResponse>> GetCachedBehaviors<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var key = typeof(TRequest);
        if (_behaviorCache.TryGetValue(key, out var cached))
            return (cached as IList<IPipelineBehavior<TRequest, TResponse>>) ?? Array.Empty<IPipelineBehavior<TRequest, TResponse>>();

        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToArray();
        _behaviorCache.TryAdd(key, behaviorsList);
        return behaviorsList;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        // No-op: retained for API compatibility
    }

    #endregion
}
