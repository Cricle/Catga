using System.Runtime.CompilerServices;
using Catga.Concurrency;
using Catga.Exceptions;
using Catga.RateLimiting;
using Catga.Results;

namespace Catga.Resilience;

/// <summary>
/// Resilience pipeline that applies rate limiting, concurrency control, and circuit breaking
/// Consolidates resilience logic to reduce code duplication
/// </summary>
internal sealed class ResiliencePipeline : IDisposable
{
    private readonly TokenBucketRateLimiter? _rateLimiter;
    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly CircuitBreaker? _circuitBreaker;

    public ResiliencePipeline(
        TokenBucketRateLimiter? rateLimiter = null,
        ConcurrencyLimiter? concurrencyLimiter = null,
        CircuitBreaker? circuitBreaker = null)
    {
        _rateLimiter = rateLimiter;
        _concurrencyLimiter = concurrencyLimiter;
        _circuitBreaker = circuitBreaker;
    }

    /// <summary>
    /// Execute action with all configured resilience policies
    /// Order: Rate Limit -> Concurrency Limit -> Circuit Breaker -> Action
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> ExecuteAsync<TResponse>(
        Func<ValueTask<CatgaResult<TResponse>>> action,
        CancellationToken cancellationToken = default)
    {
        // 1. Rate Limiting (fast fail)
        if (_rateLimiter != null && !_rateLimiter.TryAcquire())
        {
            return CatgaResult<TResponse>.Failure("Rate limit exceeded");
        }

        // 2. Concurrency Limiting
        if (_concurrencyLimiter != null)
        {
            try
            {
                return await _concurrencyLimiter.ExecuteAsync(
                    async () => await ExecuteWithCircuitBreakerAsync(action, cancellationToken),
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            }
            catch (ConcurrencyLimitException ex)
            {
                return CatgaResult<TResponse>.Failure(ex.Message);
            }
        }

        // 3. Circuit Breaker (if no concurrency limiter)
        return await ExecuteWithCircuitBreakerAsync(action, cancellationToken);
    }

    /// <summary>
    /// Execute action with circuit breaker protection
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<CatgaResult<TResponse>> ExecuteWithCircuitBreakerAsync<TResponse>(
        Func<ValueTask<CatgaResult<TResponse>>> action,
        CancellationToken cancellationToken)
    {
        if (_circuitBreaker != null)
        {
            try
            {
                return await _circuitBreaker.ExecuteAsync(() => action().AsTask());
            }
            catch (CircuitBreakerOpenException)
            {
                return CatgaResult<TResponse>.Failure("Service temporarily unavailable");
            }
        }

        return await action();
    }

    /// <summary>
    /// Check if pipeline has any resilience policies configured
    /// </summary>
    public bool HasPolicies => _rateLimiter != null || _concurrencyLimiter != null || _circuitBreaker != null;

    /// <summary>
    /// Dispose resilience components
    /// </summary>
    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
    }
}

