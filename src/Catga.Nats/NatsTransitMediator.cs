using System.Text.Json;
using CatCat.Transit.Concurrency;
using CatCat.Transit.Configuration;
using CatCat.Transit.Messages;
using CatCat.Transit.RateLimiting;
using CatCat.Transit.Resilience;
using CatCat.Transit.Results;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Nats;

/// <summary>
/// Simple, high-performance NATS Transit Mediator (100% AOT, lock-free, non-blocking)
/// </summary>
public class NatsTransitMediator : ITransitMediator, IDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsTransitMediator> _logger;
    private readonly TransitOptions _options;
    private readonly TimeSpan _timeout;

    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly TokenBucketRateLimiter? _rateLimiter;

    public NatsTransitMediator(
        INatsConnection connection,
        ILogger<NatsTransitMediator> logger,
        TransitOptions options)
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
                return TransitResult<TResponse>.Failure(ex.Message);
            }
        }

        return await ExecuteNatsRequestAsync<TRequest, TResponse>(request, cancellationToken);
    }

    private async Task<TransitResult<TResponse>> ExecuteNatsRequestAsync<TRequest, TResponse>(
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
                return TransitResult<TResponse>.Failure("NATS temporarily unavailable");
            }
        }

        return await SendToNatsAsync<TRequest, TResponse>(request, cancellationToken);
    }

    private async Task<TransitResult<TResponse>> SendToNatsAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var subject = $"transit.request.{typeof(TRequest).Name}";

        try
        {
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var reply = await _connection.RequestAsync<byte[], byte[]>(
                subject,
                requestBytes,
                cancellationToken: cts.Token);

            if (reply.Data == null)
            {
                return TransitResult<TResponse>.Failure("No response from NATS");
            }

            var result = JsonSerializer.Deserialize<TransitResult<TResponse>>(reply.Data);
            return result ?? TransitResult<TResponse>.Failure("Invalid response format");
        }
        catch (OperationCanceledException)
        {
            return TransitResult<TResponse>.Failure(
                "Request timeout",
                new TransitTimeoutException($"Timeout after {_timeout.TotalSeconds}s"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS error: {RequestType}", typeof(TRequest).Name);
            return TransitResult<TResponse>.Failure(
                "NATS communication error",
                new TransitException("NATS error", ex, isRetryable: true));
        }
    }

    public async Task<TransitResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var subject = $"transit.request.{typeof(TRequest).Name}";

        try
        {
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var reply = await _connection.RequestAsync<byte[], byte[]>(
                subject,
                requestBytes,
                cancellationToken: cts.Token);

            if (reply.Data == null)
            {
                return TransitResult.Failure("No response from NATS");
            }

            var result = JsonSerializer.Deserialize<TransitResult>(reply.Data);
            return result ?? TransitResult.Failure("Invalid response format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS error: {RequestType}", typeof(TRequest).Name);
            return TransitResult.Failure("NATS error");
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
            var eventBytes = JsonSerializer.SerializeToUtf8Bytes(@event);
            await _connection.PublishAsync(subject, eventBytes, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS publish failed: {EventType}", typeof(TEvent).Name);
            throw;
        }
    }

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
    }
}
