using Catga.Concurrency;
using Catga.Configuration;
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
/// Simple, high-performance Transit Mediator (100% AOT, lock-free, non-blocking)
/// </summary>
public class TransitMediator : ITransitMediator, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransitMediator> _logger;
    private readonly TransitOptions _options;

    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly TokenBucketRateLimiter? _rateLimiter;

    public TransitMediator(
        IServiceProvider serviceProvider,
        ILogger<TransitMediator> logger,
        TransitOptions options)
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

    public async Task<TransitResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // Rate limiting (non-blocking)
        if (_rateLimiter != null && !_rateLimiter.TryAcquire())
        {
            return TransitResult<TResponse>.Failure("Rate limit exceeded");
        }

        // Concurrency limiting (non-blocking with timeout)
        if (_concurrencyLimiter != null)
        {
            try
            {
                return await _concurrencyLimiter.ExecuteAsync(
                    () => ExecuteRequestAsync<TRequest, TResponse>(request, cancellationToken),
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            }
            catch (ConcurrencyLimitException ex)
            {
                return TransitResult<TResponse>.Failure(ex.Message);
            }
        }

        return await ExecuteRequestAsync<TRequest, TResponse>(request, cancellationToken);
    }

    private async Task<TransitResult<TResponse>> ExecuteRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Circuit breaker (optional)
        if (_circuitBreaker != null)
        {
            try
            {
                return await _circuitBreaker.ExecuteAsync(() =>
                    ProcessRequestAsync<TRequest, TResponse>(request, cancellationToken));
            }
            catch (CircuitBreakerOpenException)
            {
                return TransitResult<TResponse>.Failure("Service temporarily unavailable");
            }
        }

        return await ProcessRequestAsync<TRequest, TResponse>(request, cancellationToken);
    }

    private async Task<TransitResult<TResponse>> ProcessRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Get handler (explicit, no reflection)
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();

        if (handler == null)
        {
            return TransitResult<TResponse>.Failure(
                $"No handler for {typeof(TRequest).Name}",
                new HandlerNotFoundException(typeof(TRequest).Name));
        }

        // Build pipeline - optimized with minimal allocations
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();

        // Early exit if no behaviors
        if (!behaviors.TryGetNonEnumeratedCount(out var count) || count == 0)
            return await handler.HandleAsync(request, cancellationToken);

        // Build pipeline in reverse (reduce allocations by pre-sizing array)
        var behaviorArray = new IPipelineBehavior<TRequest, TResponse>[count];
        var i = 0;
        foreach (var b in behaviors)
            behaviorArray[i++] = b;

        Func<Task<TransitResult<TResponse>>> pipeline = () => handler.HandleAsync(request, cancellationToken);

        for (int j = behaviorArray.Length - 1; j >= 0; j--)
        {
            var behavior = behaviorArray[j];
            var currentPipeline = pipeline;
            pipeline = () => behavior.HandleAsync(request, currentPipeline, cancellationToken);
        }

        return await pipeline();
    }

    public async Task<TransitResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest>>();

        if (handler == null)
        {
            return TransitResult.Failure(
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
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();

        // Zero-allocation parallel processing: direct iteration
        if (!handlers.TryGetNonEnumeratedCount(out var count) || count == 0)
            return;

        var tasks = new Task[count];
        var i = 0;

        foreach (var handler in handlers)
        {
            var h = handler; // Capture for closure
            tasks[i++] = Task.Run(async () =>
            {
                try
                {
                    await h.HandleAsync(@event, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Event handler failed: {HandlerType}", h.GetType().Name);
                }
            }, cancellationToken);
        }

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
    }
}
