using Catga.Concurrency;
using Catga.Configuration;
using Catga.Exceptions;
using Catga.Messages;
using Catga.Nats.Serialization;
using Catga.RateLimiting;
using Catga.Resilience;
using Catga.Results;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Nats;

/// <summary>
/// Simple, high-performance NATS Catga Mediator (100% AOT, lock-free, non-blocking)
/// </summary>
public class NatsCatgaMediator : ICatgaMediator, IDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsCatgaMediator> _logger;
    private readonly CatgaOptions _options;
    private readonly TimeSpan _timeout;

    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly TokenBucketRateLimiter? _rateLimiter;

    public NatsCatgaMediator(
        INatsConnection connection,
        ILogger<NatsCatgaMediator> logger,
        CatgaOptions options)
    {
        _connection = connection;
        _logger = logger;
        _options = options;
        _timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        // Optional resilience components
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

    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // Rate limiting (non-blocking)
        if (_rateLimiter != null && !_rateLimiter.TryAcquire())
        {
            return CatgaResult<TResponse>.Failure("Rate limit exceeded");
        }

        // Concurrency limiting (non-blocking)
        if (_concurrencyLimiter != null)
        {
            try
            {
                return await _concurrencyLimiter.ExecuteAsync(
                    () => ExecuteNatsRequestAsync<TRequest, TResponse>(request, cancellationToken),
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            }
            catch (ConcurrencyLimitException ex)
            {
                return CatgaResult<TResponse>.Failure(ex.Message);
            }
        }

        return await ExecuteNatsRequestAsync<TRequest, TResponse>(request, cancellationToken);
    }

    private async Task<CatgaResult<TResponse>> ExecuteNatsRequestAsync<TRequest, TResponse>(
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
                    SendToNatsAsync<TRequest, TResponse>(request, cancellationToken));
            }
            catch (CircuitBreakerOpenException)
            {
                return CatgaResult<TResponse>.Failure("NATS temporarily unavailable");
            }
        }

        return await SendToNatsAsync<TRequest, TResponse>(request, cancellationToken);
    }

    private async Task<CatgaResult<TResponse>> SendToNatsAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var subject = $"transit.request.{typeof(TRequest).Name}";

        try
        {
            var requestBytes = NatsJsonSerializer.SerializeToUtf8Bytes(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var reply = await _connection.RequestAsync<byte[], byte[]>(
                subject,
                requestBytes,
                cancellationToken: cts.Token);

            if (reply.Data == null)
            {
                return CatgaResult<TResponse>.Failure("No response from NATS");
            }

            var result = NatsJsonSerializer.Deserialize<CatgaResult<TResponse>>(reply.Data);
            return result ?? CatgaResult<TResponse>.Failure("Invalid response format");
        }
        catch (OperationCanceledException)
        {
            return CatgaResult<TResponse>.Failure(
                "Request timeout",
                new CatgaTimeoutException($"Timeout after {_timeout.TotalSeconds}s"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS error: {RequestType}", typeof(TRequest).Name);
            return CatgaResult<TResponse>.Failure(
                "NATS communication error",
                new CatgaException("NATS error", ex, isRetryable: true));
        }
    }

    public async Task<CatgaResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var subject = $"transit.request.{typeof(TRequest).Name}";

        try
        {
            var requestBytes = NatsJsonSerializer.SerializeToUtf8Bytes(request);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var reply = await _connection.RequestAsync<byte[], byte[]>(
                subject,
                requestBytes,
                cancellationToken: cts.Token);

            if (reply.Data == null)
            {
                return CatgaResult.Failure("No response from NATS");
            }

            var result = NatsJsonSerializer.Deserialize<CatgaResult>(reply.Data);
            return result ?? CatgaResult.Failure("Invalid response format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS error: {RequestType}", typeof(TRequest).Name);
            return CatgaResult.Failure("NATS error");
        }
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        var subject = $"transit.event.{typeof(TEvent).Name}";

        try
        {
            var eventBytes = NatsJsonSerializer.SerializeToUtf8Bytes(@event);
            await _connection.PublishAsync(subject, eventBytes, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS publish failed: {EventType}", typeof(TEvent).Name);
            throw;
        }
    }

    /// <summary>
    /// 🔥 批量发送请求 - NATS优化批处理
    /// </summary>
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        if (requests == null || requests.Count == 0)
            return Array.Empty<CatgaResult<TResponse>>();

        // 快速路径：单个请求
        if (requests.Count == 1)
        {
            var result = await SendAsync<TRequest, TResponse>(requests[0], cancellationToken).ConfigureAwait(false);
            return new[] { result };
        }

        // 批量处理：并行发送
        var results = new CatgaResult<TResponse>[requests.Count];
        var tasks = new ValueTask<CatgaResult<TResponse>>[requests.Count];

        for (int i = 0; i < requests.Count; i++)
        {
            tasks[i] = SendAsync<TRequest, TResponse>(requests[i], cancellationToken);
        }

        for (int i = 0; i < tasks.Length; i++)
        {
            results[i] = await tasks[i].ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// 🔥 流式发送请求 - NATS实时流处理
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
    /// 🔥 批量发布事件 - NATS优化批处理
    /// </summary>
    public async Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        if (events == null || events.Count == 0)
            return;

        // 快速路径：单个事件
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
