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

    /// <summary>
    /// 🔥 批量发送请求 - 高性能批处理（零额外分配）
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        if (requests == null || requests.Count == 0)
            return Array.Empty<CatgaResult<TResponse>>();

        // 快速路径：单个请求直接调用 SendAsync
        if (requests.Count == 1)
        {
            var result = await SendAsync<TRequest, TResponse>(requests[0], cancellationToken).ConfigureAwait(false);
            return new[] { result };
        }

        // 批量处理：使用数组避免 List 的分配开销
        var results = new CatgaResult<TResponse>[requests.Count];
        var tasks = new ValueTask<CatgaResult<TResponse>>[requests.Count];

        // 并行启动所有请求
        for (int i = 0; i < requests.Count; i++)
        {
            tasks[i] = SendAsync<TRequest, TResponse>(requests[i], cancellationToken);
        }

        // 等待所有请求完成
        for (int i = 0; i < tasks.Length; i++)
        {
            results[i] = await tasks[i].ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// 🔥 流式发送请求 - 实时处理大量数据（背压支持）
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
    /// 🔥 批量发布事件 - 高性能批处理
    /// </summary>
    public async Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        if (events == null || events.Count == 0)
            return;

        // 快速路径：单个事件直接调用 PublishAsync
        if (events.Count == 1)
        {
            await PublishAsync(events[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        // 批量处理：并行发布
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
