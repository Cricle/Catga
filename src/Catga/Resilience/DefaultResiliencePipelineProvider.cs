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

    public DefaultResiliencePipelineProvider(CatgaResilienceOptions options)
    {
        options ??= new CatgaResilienceOptions();
        _mediator = BuildMediator(options);
        _transportPublish = BuildTransport(options, ResilienceKeys.TransportPublish);
        _transportSend = BuildTransport(options, ResilienceKeys.TransportSend);
        _persistence = BuildPersistence(options);
    }

    private static ResiliencePipeline BuildMediator(CatgaResilienceOptions o)
    {
        var builder = new ResiliencePipelineBuilder();
        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
            {
                PermitLimit = o.MediatorBulkheadConcurrency,
                QueueLimit = o.MediatorBulkheadQueueLimit
            },
            OnRejected = _ =>
            {
                CatgaDiagnostics.ResilienceBulkheadRejected.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.bulkhead.rejected",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator }));
                }
                return default;
            }
        });
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 20,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.open",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator }));
                }
                return default;
            },
            OnHalfOpened = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.halfopen",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator }));
                }
                return default;
            },
            OnClosed = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.closed",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator }));
                }
                return default;
            }
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = o.MediatorTimeout,
            OnTimeout = _ =>
            {
                CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.timeout",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator }));
                }
                return default;
            }
        });
        return builder.Build();
    }

    private static ResiliencePipeline BuildTransport(CatgaResilienceOptions o, string component)
    {
        var builder = new ResiliencePipelineBuilder();
        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
            {
                PermitLimit = o.TransportBulkheadConcurrency,
                QueueLimit = o.TransportBulkheadQueueLimit
            },
            OnRejected = _ =>
            {
                CatgaDiagnostics.ResilienceBulkheadRejected.Add(1, new KeyValuePair<string, object?>("component", component));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.bulkhead.rejected",
                        tags: new ActivityTagsCollection { ["component"] = component }));
                }
                return default;
            }
        });
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 50,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", component));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.open",
                        tags: new ActivityTagsCollection { ["component"] = component }));
                }
                return default;
            },
            OnHalfOpened = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", component));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.halfopen",
                        tags: new ActivityTagsCollection { ["component"] = component }));
                }
                return default;
            },
            OnClosed = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", component));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.closed",
                        tags: new ActivityTagsCollection { ["component"] = component }));
                }
                return default;
            }
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = o.TransportTimeout,
            OnTimeout = _ =>
            {
                CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", component));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.timeout",
                        tags: new ActivityTagsCollection { ["component"] = component }));
                }
                return default;
            }
        });
        builder.AddRetry(new RetryStrategyOptions
        {
            Delay = o.TransportRetryDelay,
            UseJitter = true,
            BackoffType = DelayBackoffType.Exponential,
            MaxRetryAttempts = o.TransportRetryCount,
            ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
            OnRetry = args =>
            {
                CatgaDiagnostics.ResilienceRetries.Add(1, new KeyValuePair<string, object?>("component", component));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.retry",
                        tags: new ActivityTagsCollection { ["component"] = component, ["attempt"] = args.AttemptNumber }));
                }
                return default;
            }
        });
        return builder.Build();
    }

    private static ResiliencePipeline BuildPersistence(CatgaResilienceOptions o)
    {
        var builder = new ResiliencePipelineBuilder();
        if (o.PersistenceBulkheadConcurrency > 0)
        {
            builder.AddRateLimiter(new RateLimiterStrategyOptions
            {
                DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
                {
                    PermitLimit = o.PersistenceBulkheadConcurrency,
                    QueueLimit = o.PersistenceBulkheadQueueLimit
                },
                OnRejected = _ =>
                {
                    CatgaDiagnostics.ResilienceBulkheadRejected.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence));
                    var a = Activity.Current;
                    if (a != null)
                    {
                        a.AddEvent(new ActivityEvent(
                            "resilience.bulkhead.rejected",
                            tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence }));
                    }
                    return default;
                }
            });
        }
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 50,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.open",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence }));
                }
                return default;
            },
            OnHalfOpened = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.halfopen",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence }));
                }
                return default;
            },
            OnClosed = _ =>
            {
                CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence));
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.circuit.closed",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence }));
                }
                return default;
            }
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = o.PersistenceTimeout,
            OnTimeout = _ =>
            {
                CatgaDiagnostics.ResilienceTimeouts.Add(1);
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.timeout",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence }));
                }
                return default;
            }
        });
        builder.AddRetry(new RetryStrategyOptions
        {
            Delay = o.PersistenceRetryDelay,
            UseJitter = true,
            BackoffType = DelayBackoffType.Exponential,
            MaxRetryAttempts = o.PersistenceRetryCount,
            ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
            OnRetry = args =>
            {
                CatgaDiagnostics.ResilienceRetries.Add(1);
                var a = Activity.Current;
                if (a != null)
                {
                    a.AddEvent(new ActivityEvent(
                        "resilience.retry",
                        tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence, ["attempt"] = args.AttemptNumber }));
                }
                return default;
            }
        });
        return builder.Build();
    }

    public async ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        return await _mediator.ExecuteAsync(async ct => await action(ct), cancellationToken);
    }

    public async ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
        await _mediator.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
    }

    public async ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        return await _transportPublish.ExecuteAsync(async ct => await action(ct), cancellationToken);
    }

    public async ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
        await _transportPublish.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
    }

    public async ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        return await _transportSend.ExecuteAsync(async ct => await action(ct), cancellationToken);
    }

    public async ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
        await _transportSend.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
    }

    public async ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        return await _persistence.ExecuteAsync(async ct => await action(ct), cancellationToken);
    }

    public async ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
        await _persistence.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
    }
}
