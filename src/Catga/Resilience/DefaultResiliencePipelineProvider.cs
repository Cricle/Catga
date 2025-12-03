using System.Diagnostics;
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

    public DefaultResiliencePipelineProvider(CatgaResilienceOptions? options = null)
    {
        var o = options ?? new CatgaResilienceOptions();
        _mediator = Build(ResilienceKeys.Mediator, o.MediatorBulkheadConcurrency, o.MediatorBulkheadQueueLimit, o.MediatorTimeout, 20);
        _transportPublish = Build(ResilienceKeys.TransportPublish, o.TransportBulkheadConcurrency, o.TransportBulkheadQueueLimit, o.TransportTimeout, 50, o.TransportRetryDelay, o.TransportRetryCount);
        _transportSend = Build(ResilienceKeys.TransportSend, o.TransportBulkheadConcurrency, o.TransportBulkheadQueueLimit, o.TransportTimeout, 50, o.TransportRetryDelay, o.TransportRetryCount);
        _persistence = Build(ResilienceKeys.Persistence, o.PersistenceBulkheadConcurrency, o.PersistenceBulkheadQueueLimit, o.PersistenceTimeout, 50, o.PersistenceRetryDelay, o.PersistenceRetryCount);
    }

    private static ResiliencePipeline Build(string comp, int concurrency, int queue, TimeSpan timeout, int minThroughput, TimeSpan? retryDelay = null, int retryCount = 0)
    {
        var b = new ResiliencePipelineBuilder();
        if (concurrency > 0)
            b.AddRateLimiter(new RateLimiterStrategyOptions
            {
                DefaultRateLimiterOptions = new ConcurrencyLimiterOptions { PermitLimit = concurrency, QueueLimit = queue },
                OnRejected = _ => { Metric(CatgaDiagnostics.ResilienceBulkheadRejected, comp); Event(CatgaActivitySource.Events.ResilienceBulkheadRejected, comp); return default; }
            });
        b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5, MinimumThroughput = minThroughput, BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ => { Metric(CatgaDiagnostics.ResilienceCircuitOpened, comp); Event(CatgaActivitySource.Events.ResilienceCircuitOpen, comp); return default; },
            OnHalfOpened = _ => { Metric(CatgaDiagnostics.ResilienceCircuitHalfOpened, comp); Event(CatgaActivitySource.Events.ResilienceCircuitHalfOpen, comp); return default; },
            OnClosed = _ => { Metric(CatgaDiagnostics.ResilienceCircuitClosed, comp); Event(CatgaActivitySource.Events.ResilienceCircuitClosed, comp); return default; }
        });
        b.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = timeout,
            OnTimeout = _ => { Metric(CatgaDiagnostics.ResilienceTimeouts, comp); Event(CatgaActivitySource.Events.ResilienceTimeout, comp); return default; }
        });
        if (retryDelay.HasValue && retryCount > 0)
            b.AddRetry(new RetryStrategyOptions
            {
                Delay = retryDelay.Value, UseJitter = true, BackoffType = DelayBackoffType.Exponential, MaxRetryAttempts = retryCount,
                ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
                OnRetry = args => { Metric(CatgaDiagnostics.ResilienceRetries, comp); Event(CatgaActivitySource.Events.ResilienceRetry, comp, args.AttemptNumber); return default; }
            });
        return b.Build();
    }

    private static void Metric(System.Diagnostics.Metrics.Counter<long> counter, string comp) => counter.Add(1, new KeyValuePair<string, object?>("component", comp));
    private static void Event(string name, string comp, int? attempt = null) => Activity.Current?.AddEvent(new ActivityEvent(name, tags: attempt.HasValue ? new ActivityTagsCollection { ["component"] = comp, ["attempt"] = attempt.Value } : new ActivityTagsCollection { ["component"] = comp }));

    public async ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _mediator.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _mediator.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _transportPublish.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _transportPublish.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _transportSend.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _transportSend.ExecuteAsync(async c => { await action(c); return 0; }, ct);
    public async ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => await _persistence.ExecuteAsync(async c => await action(c), ct);
    public async ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => await _persistence.ExecuteAsync(async c => { await action(c); return 0; }, ct);
}
