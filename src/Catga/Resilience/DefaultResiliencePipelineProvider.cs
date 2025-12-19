using System.Threading.RateLimiting;
using Catga.Observability;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;

namespace Catga.Resilience;

public sealed class DefaultResiliencePipelineProvider : IResiliencePipelineProvider
{
    private readonly ResiliencePipeline _mediator;
    private readonly ResiliencePipeline _transportPublish;
    private readonly ResiliencePipeline _transportSend;
    private readonly ResiliencePipeline _persistence;
    private readonly ResiliencePipeline _persistenceNoRetry;

    public DefaultResiliencePipelineProvider(CatgaResilienceOptions? options = null)
    {
        var o = options ?? new CatgaResilienceOptions();
        _mediator = Build(o.MediatorBulkheadConcurrency, o.MediatorBulkheadQueueLimit, o.MediatorTimeout, 20);
        _transportPublish = Build(o.TransportBulkheadConcurrency, o.TransportBulkheadQueueLimit, o.TransportTimeout, 50, o.TransportRetryDelay, o.TransportRetryCount);
        _transportSend = Build(o.TransportBulkheadConcurrency, o.TransportBulkheadQueueLimit, o.TransportTimeout, 50, o.TransportRetryDelay, o.TransportRetryCount);
        _persistence = Build(o.PersistenceBulkheadConcurrency, o.PersistenceBulkheadQueueLimit, o.PersistenceTimeout, 50, o.PersistenceRetryDelay, o.PersistenceRetryCount);
        // No retry pipeline for non-idempotent operations (locks, optimistic concurrency)
        _persistenceNoRetry = Build(o.PersistenceBulkheadConcurrency, o.PersistenceBulkheadQueueLimit, o.PersistenceTimeout, 50);
    }

    private static ResiliencePipeline Build(int concurrency, int queue, TimeSpan timeout, int minThroughput, TimeSpan? retryDelay = null, int retryCount = 0)
    {
        var b = new ResiliencePipelineBuilder();
        if (concurrency > 0)
            b.AddRateLimiter(new RateLimiterStrategyOptions
            {
                DefaultRateLimiterOptions = new ConcurrencyLimiterOptions { PermitLimit = concurrency, QueueLimit = queue },
                OnRejected = _ => { CatgaDiagnostics.ResilienceRetries.Add(1); return default; }
            });
        b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = minThroughput,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ => { CatgaDiagnostics.ResilienceCircuitOpened.Add(1); return default; },
            OnHalfOpened = _ => default,
            OnClosed = _ => default
        });
        b.AddTimeout(new TimeoutStrategyOptions { Timeout = timeout });
        if (retryDelay.HasValue && retryCount > 0)
            b.AddRetry(new RetryStrategyOptions
            {
                Delay = retryDelay.Value,
                UseJitter = true,
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = retryCount,
                ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
                OnRetry = _ => { CatgaDiagnostics.ResilienceRetries.Add(1); return default; }
            });
        return b.Build();
    }

    public async ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _mediator.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _mediator.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _transportPublish.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _transportPublish.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _transportSend.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _transportSend.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _persistence.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _persistence.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecutePersistenceNoRetryAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _persistenceNoRetry.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecutePersistenceNoRetryAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _persistenceNoRetry.ExecuteAsync(async c => { await action(c); return 0; }, ct);
}
