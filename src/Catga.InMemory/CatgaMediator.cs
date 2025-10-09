using Catga.Common;
using Catga.Concurrency;
using Catga.Configuration;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Performance;
using Catga.Pipeline;
using Catga.RateLimiting;
using Catga.Resilience;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga;

/// <summary>
/// High-performance Catga Mediator (100% AOT, lock-free, non-blocking)
/// Optimized with handler caching, object pooling, and fast paths
/// </summary>
public class CatgaMediator : ICatgaMediator, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly CatgaOptions _options;

    // Performance optimizations
    private readonly HandlerCache _handlerCache;
    private readonly ResiliencePipeline _resiliencePipeline;

    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger,
        CatgaOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;

        // Performance optimization: Handler cache
        _handlerCache = new HandlerCache(serviceProvider);

        // Resilience pipeline (consolidates rate limiting, concurrency control, circuit breaking)
        var rateLimiter = options.EnableRateLimiting
            ? new TokenBucketRateLimiter(options.RateLimitBurstCapacity, options.RateLimitRequestsPerSecond)
            : null;

        var concurrencyLimiter = options.MaxConcurrentRequests > 0
            ? new ConcurrencyLimiter(options.MaxConcurrentRequests)
            : null;

        var circuitBreaker = options.EnableCircuitBreaker
            ? new CircuitBreaker(
                options.CircuitBreakerFailureThreshold,
                TimeSpan.FromSeconds(options.CircuitBreakerResetTimeoutSeconds))
            : null;

        _resiliencePipeline = new ResiliencePipeline(rateLimiter, concurrencyLimiter, circuitBreaker);
    }

    /// <summary>
    /// Optimized: Use ValueTask to reduce heap allocations
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // Use resilience pipeline for consolidated rate limiting, concurrency control, and circuit breaking
        return await _resiliencePipeline.ExecuteAsync(
            () => ProcessRequestAsync<TRequest, TResponse>(request, cancellationToken),
            cancellationToken);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private async ValueTask<CatgaResult<TResponse>> ProcessRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Optimized: Use cached handler lookup
        var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(_serviceProvider);

        if (handler == null)
        {
            return CatgaResult<TResponse>.Failure(
                $"No handler for {typeof(TRequest).Name}",
                new HandlerNotFoundException(typeof(TRequest).Name));
        }

        // Get behaviors (check count for fast path optimization)
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();

        // Fast path: No behaviors, execute handler directly (zero allocation)
        if (FastPath.CanUseFastPath(behaviorsList.Count))
        {
            return await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken);
        }

        // Standard path: Execute with pipeline
        return await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);
    }

    public async Task<CatgaResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest>>();

        if (handler == null)
        {
            return CatgaResult.Failure(
                $"No handler for {typeof(TRequest).Name}",
                new HandlerNotFoundException(typeof(TRequest).Name));
        }

        return await handler.HandleAsync(request, cancellationToken);
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // Optimized: Use cached handler lookup
        var handlerList = _handlerCache.GetEventHandlers<IEventHandler<TEvent>>(_serviceProvider);

        // Fast path: No handlers (zero allocation)
        if (handlerList.Count == 0)
        {
            await FastPath.PublishEventNoOpAsync();
            return;
        }

        // Fast path: Single handler (reduced allocation)
        if (handlerList.Count == 1)
        {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            return;
        }

        // Standard path: Multiple handlers, execute concurrently
        // Use ArrayPoolHelper for automatic resource management
        using var rentedTasks = Common.ArrayPoolHelper.RentOrAllocate<Task>(handlerList.Count);
        var tasks = rentedTasks.Array;

        // Start all event handlers
        for (int i = 0; i < handlerList.Count; i++)
        {
            var handler = handlerList[i];
            tasks[i] = HandleEventSafelyAsync(handler, @event, cancellationToken);
        }

        // Wait for all handlers (use exact count from rentedTasks)
        await Task.WhenAll(rentedTasks.AsSpan().ToArray()).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute event handler with exception isolation
    /// </summary>
    private async Task HandleEventSafelyAsync<TEvent>(
        IEventHandler<TEvent> handler,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        try
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event handler failed: {HandlerType}", handler.GetType().Name);
        }
    }

    /// <summary>
    /// Batch send requests - High performance batch processing (zero extra allocations)
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // Use BatchOperationExtensions for standardized batch processing
        return await requests.ExecuteBatchWithResultsAsync(
            request => SendAsync<TRequest, TResponse>(request, cancellationToken));
    }

    /// <summary>
    /// Stream send requests - Real-time processing of large data (backpressure support)
    /// </summary>
    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        if (requests == null)
            yield break;

        await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var result = await SendAsync<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
            yield return result;
        }
    }

    /// <summary>
    /// Batch publish events - High performance batch processing
    /// </summary>
    public async Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // Use BatchOperationExtensions for standardized batch processing
        await events.ExecuteBatchAsync(@event => PublishAsync(@event, cancellationToken));
    }

    public void Dispose()
    {
        _resiliencePipeline?.Dispose();
    }
}
