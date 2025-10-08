using Catga.Concurrency;
using Catga.Configuration;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Pipeline;
using Catga.RateLimiting;
using Catga.Resilience;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga;

/// <summary>
/// High-performance Catga Mediator (100% AOT, lock-free, non-blocking)
/// </summary>
public class CatgaMediator : ICatgaMediator, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly CatgaOptions _options;

    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly TokenBucketRateLimiter? _rateLimiter;

    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger,
        CatgaOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;

        // Optional resilience components (only if enabled)
        if (options.MaxConcurrentRequests > 0)
            _concurrencyLimiter = new ConcurrencyLimiter(options.MaxConcurrentRequests);

        if (options.EnableCircuitBreaker)
            _circuitBreaker = new CircuitBreaker(
                options.CircuitBreakerFailureThreshold,
                TimeSpan.FromSeconds(options.CircuitBreakerResetTimeoutSeconds));

        if (options.EnableRateLimiting)
            _rateLimiter = new TokenBucketRateLimiter(
                options.RateLimitBurstCapacity,
                options.RateLimitRequestsPerSecond);
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
        // Fast path: Check rate limit first (fail fast)
        if (_rateLimiter != null && !_rateLimiter.TryAcquire())
            return CatgaResult<TResponse>.Failure("Rate limit exceeded");

        // Avoid unnecessary nesting, return directly
        if (_concurrencyLimiter != null)
        {
            try
            {
                return await _concurrencyLimiter.ExecuteAsync(
                    () => ProcessRequestWithCircuitBreaker<TRequest, TResponse>(request, cancellationToken).AsTask(),
                    TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (ConcurrencyLimitException ex)
            {
                return CatgaResult<TResponse>.Failure(ex.Message);
            }
        }

        return await ProcessRequestWithCircuitBreaker<TRequest, TResponse>(request, cancellationToken);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private async ValueTask<CatgaResult<TResponse>> ProcessRequestWithCircuitBreaker<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Merge circuit breaker and request processing to reduce method call overhead
        if (_circuitBreaker != null)
        {
            try
            {
                return await _circuitBreaker.ExecuteAsync(() =>
                    ProcessRequestAsync<TRequest, TResponse>(request, cancellationToken).AsTask());
            }
            catch (CircuitBreakerOpenException)
            {
                return CatgaResult<TResponse>.Failure("Service temporarily unavailable");
            }
        }

        return await ProcessRequestAsync<TRequest, TResponse>(request, cancellationToken);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private async ValueTask<CatgaResult<TResponse>> ProcessRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Get handler (explicit, no reflection)
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();

        if (handler == null)
        {
            return CatgaResult<TResponse>.Failure(
                $"No handler for {typeof(TRequest).Name}",
                new HandlerNotFoundException(typeof(TRequest).Name));
        }

        // Use optimized PipelineExecutor to reduce closure and delegate allocations
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();

        // Execute with optimized pipeline executor
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
        // Avoid LINQ Select, build task array directly
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();
        var handlerList = handlers as IList<IEventHandler<TEvent>> ?? handlers.ToArray();

        if (handlerList.Count == 0)
            return;

        // Execute handlers concurrently without Task.Run
        // HandleAsync is already async, no need to queue to thread pool
        var tasks = new Task[handlerList.Count];
        for (int i = 0; i < handlerList.Count; i++)
        {
            var handler = handlerList[i];
            // Wrap in helper method to isolate exception handling per handler
            tasks[i] = HandleEventSafelyAsync(handler, @event, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
        if (requests == null || requests.Count == 0)
            return Array.Empty<CatgaResult<TResponse>>();

        // Fast path: Single request directly calls SendAsync
        if (requests.Count == 1)
        {
            var result = await SendAsync<TRequest, TResponse>(requests[0], cancellationToken).ConfigureAwait(false);
            return new[] { result };
        }

        // Batch processing: Use array to avoid List allocation overhead
        var results = new CatgaResult<TResponse>[requests.Count];
        var tasks = new ValueTask<CatgaResult<TResponse>>[requests.Count];

        // Start all requests in parallel
        for (int i = 0; i < requests.Count; i++)
        {
            tasks[i] = SendAsync<TRequest, TResponse>(requests[i], cancellationToken);
        }

        // Wait for all requests to complete
        for (int i = 0; i < tasks.Length; i++)
        {
            results[i] = await tasks[i].ConfigureAwait(false);
        }

        return results;
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
        if (events == null || events.Count == 0)
            return;

        // Fast path: Single event directly calls PublishAsync
        if (events.Count == 1)
        {
            await PublishAsync(events[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        // Batch processing: Publish in parallel
        var tasks = new Task[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            tasks[i] = PublishAsync(events[i], cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
    }
}
