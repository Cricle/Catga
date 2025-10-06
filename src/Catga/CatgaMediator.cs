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
/// 精简高性能 Catga 中介器（100% AOT，无锁，非阻塞）
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
    /// 🔥 优化: 使用 ValueTask 减少堆分配
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 🔥 优化: 快速路径检查 - 先检查限流（最快失败）
        if (_rateLimiter != null && !_rateLimiter.TryAcquire())
            return CatgaResult<TResponse>.Failure("Rate limit exceeded");

        // 🔥 优化: 避免不必要的嵌套，直接返回
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
        // 合并熔断器和请求处理，减少一层方法调用
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

        // 🔥 优化: 使用优化的 PipelineExecutor，减少闭包和委托分配
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();

        // 使用优化的 Pipeline 执行器
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
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();

        // 进一步优化 - 避免Task.Run包装，直接并行执行
        var tasks = handlers.Select(handler =>
            Task.Run(async () =>
            {
                try
                {
                    await handler.HandleAsync(@event, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Event handler failed: {HandlerType}", handler.GetType().Name);
                }
            }, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
    }
}
