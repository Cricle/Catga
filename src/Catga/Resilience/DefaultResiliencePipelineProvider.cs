#if NET8_0_OR_GREATER
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Polly.RateLimiting;
using System.Threading.RateLimiting;
#else
using Polly;
#endif
using System.Diagnostics;
using Catga.Observability;

namespace Catga.Resilience;

public sealed class DefaultResiliencePipelineProvider : IResiliencePipelineProvider
{
#if NET8_0_OR_GREATER
    private readonly ResiliencePipeline _mediator;
    private readonly ResiliencePipeline _transportPublish;
    private readonly ResiliencePipeline _transportSend;
    private readonly ResiliencePipeline _persistence;
#else
    private readonly Polly.Wrap.AsyncPolicyWrap _mediator;
    private readonly Polly.Wrap.AsyncPolicyWrap _transportPublish;
    private readonly Polly.Wrap.AsyncPolicyWrap _transportSend;
    private readonly Polly.Wrap.AsyncPolicyWrap _persistence;
#endif

    public DefaultResiliencePipelineProvider(CatgaResilienceOptions options)
    {
        options ??= new CatgaResilienceOptions();
#if NET8_0_OR_GREATER
        _mediator = BuildMediator(options);
        _transportPublish = BuildTransport(options, ResilienceKeys.TransportPublish);
        _transportSend = BuildTransport(options, ResilienceKeys.TransportSend);
        _persistence = BuildPersistence(options);
#else
        _mediator = BuildMediatorV7(options);
        _transportPublish = BuildTransportV7(options);
        _transportSend = BuildTransportV7(options);
        _persistence = BuildPersistenceV7(options);
#endif
    }

#if NET8_0_OR_GREATER
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
            OnRejected = _ => { CatgaDiagnostics.ResilienceBulkheadRejected.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.bulkhead.rejected", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); return default; }
        });
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 20,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ => { CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.open", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); return default; },
            OnHalfOpened = _ => { CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.halfopen", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); return default; },
            OnClosed = _ => { CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.closed", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); return default; }
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = o.MediatorTimeout,
            OnTimeout = _ => { CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.timeout", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); return default; }
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
            OnRejected = _ => { CatgaDiagnostics.ResilienceBulkheadRejected.Add(1, new KeyValuePair<string, object?>("component", component)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.bulkhead.rejected", tags: new ActivityTagsCollection { ["component"] = component })); return default; }
        });
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 50,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ => { CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", component)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.open", tags: new ActivityTagsCollection { ["component"] = component })); return default; },
            OnHalfOpened = _ => { CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", component)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.halfopen", tags: new ActivityTagsCollection { ["component"] = component })); return default; },
            OnClosed = _ => { CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", component)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.closed", tags: new ActivityTagsCollection { ["component"] = component })); return default; }
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = o.TransportTimeout,
            OnTimeout = _ => { CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", component)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.timeout", tags: new ActivityTagsCollection { ["component"] = component })); return default; }
        });
        builder.AddRetry(new RetryStrategyOptions
        {
            Delay = o.TransportRetryDelay,
            UseJitter = true,
            BackoffType = DelayBackoffType.Exponential,
            MaxRetryAttempts = o.TransportRetryCount,
            ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
            OnRetry = args => { CatgaDiagnostics.ResilienceRetries.Add(1, new KeyValuePair<string, object?>("component", component)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.retry", tags: new ActivityTagsCollection { ["component"] = component, ["attempt"] = args.AttemptNumber })); return default; }
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
                OnRejected = _ => { CatgaDiagnostics.ResilienceBulkheadRejected.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.bulkhead.rejected", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence })); return default; }
            });
        }
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 50,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ => { CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.open", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence })); return default; },
            OnHalfOpened = _ => { CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.halfopen", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence })); return default; },
            OnClosed = _ => { CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.closed", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence })); return default; }
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = o.PersistenceTimeout,
            OnTimeout = _ => { CatgaDiagnostics.ResilienceTimeouts.Add(1); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.timeout", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence })); return default; }
        });
        builder.AddRetry(new RetryStrategyOptions
        {
            Delay = o.PersistenceRetryDelay,
            UseJitter = true,
            BackoffType = DelayBackoffType.Exponential,
            MaxRetryAttempts = o.PersistenceRetryCount,
            ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<Exception>(),
            OnRetry = args => { CatgaDiagnostics.ResilienceRetries.Add(1); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.retry", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence, ["attempt"] = args.AttemptNumber })); return default; }
        });
        return builder.Build();
    }
#else
    private static Polly.Wrap.AsyncPolicyWrap BuildMediatorV7(CatgaResilienceOptions o)
    {
        var bulkhead = Policy.BulkheadAsync(o.MediatorBulkheadConcurrency, o.MediatorBulkheadQueueLimit);
        var circuit = Policy.Handle<Exception>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),
                onBreak: (ex, dt) => { CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.open", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); },
                onReset: () => { CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.closed", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); },
                onHalfOpen: () => { CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.halfopen", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Mediator })); });
        var timeout = Policy.TimeoutAsync(o.MediatorTimeout, Polly.Timeout.TimeoutStrategy.Optimistic,
            (ctx, ts, task) => { CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Mediator)); return Task.CompletedTask; });
        var retry = Policy.NoOpAsync(); // 默认命令不重试，查询稍后在行为里覆盖
        return Policy.WrapAsync(bulkhead, circuit, timeout, retry);
    }

    private static Polly.Wrap.AsyncPolicyWrap BuildTransportV7(CatgaResilienceOptions o)
    {
        var bulkhead = Policy.BulkheadAsync(o.TransportBulkheadConcurrency, o.TransportBulkheadQueueLimit);
        var circuit = Policy.Handle<Exception>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),
                onBreak: (ex, dt) => { CatgaDiagnostics.ResilienceCircuitOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.TransportPublish)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.open", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.TransportPublish })); },
                onReset: () => { CatgaDiagnostics.ResilienceCircuitClosed.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.TransportPublish)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.closed", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.TransportPublish })); },
                onHalfOpen: () => { CatgaDiagnostics.ResilienceCircuitHalfOpened.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.TransportPublish)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.circuit.halfopen", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.TransportPublish })); });
        var timeout = Policy.TimeoutAsync(o.TransportTimeout, Polly.Timeout.TimeoutStrategy.Optimistic,
            (ctx, ts, task) => { CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.TransportPublish)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.timeout", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.TransportPublish })); return Task.CompletedTask; });
        var retry = Policy.Handle<Exception>()
            .WaitAndRetryAsync(
                o.TransportRetryCount,
                attempt => TimeSpan.FromMilliseconds(o.TransportRetryDelay.TotalMilliseconds * Math.Pow(2, attempt)),
                (ex, delay, attempt, ctx) => { CatgaDiagnostics.ResilienceRetries.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.TransportPublish)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.retry", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.TransportPublish, ["attempt"] = attempt })); });
        return Policy.WrapAsync(bulkhead, circuit, timeout, retry);
    }

    private static Polly.Wrap.AsyncPolicyWrap BuildPersistenceV7(CatgaResilienceOptions o)
    {
        var timeout = Policy.TimeoutAsync(o.PersistenceTimeout, Polly.Timeout.TimeoutStrategy.Optimistic,
            (ctx, ts, task) => { CatgaDiagnostics.ResilienceTimeouts.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.timeout", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence })); return Task.CompletedTask; });
        var retry = Policy.Handle<Exception>()
            .WaitAndRetryAsync(
                o.PersistenceRetryCount,
                attempt => TimeSpan.FromMilliseconds(o.PersistenceRetryDelay.TotalMilliseconds * Math.Pow(2, attempt)),
                (ex, delay, attempt, ctx) => { CatgaDiagnostics.ResilienceRetries.Add(1, new KeyValuePair<string, object?>("component", ResilienceKeys.Persistence)); var a = Activity.Current; if (a != null) a.AddEvent(new ActivityEvent("resilience.retry", tags: new ActivityTagsCollection { ["component"] = ResilienceKeys.Persistence, ["attempt"] = attempt })); });
        return Policy.WrapAsync(timeout, retry);
    }
#endif

    public async ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await _mediator.ExecuteAsync(async ct => await action(ct), cancellationToken);
#else
        return await _mediator.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        await _mediator.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
#else
        await _mediator.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await _transportPublish.ExecuteAsync(async ct => await action(ct), cancellationToken);
#else
        return await _transportPublish.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        await _transportPublish.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
#else
        await _transportPublish.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await _transportSend.ExecuteAsync(async ct => await action(ct), cancellationToken);
#else
        return await _transportSend.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        await _transportSend.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
#else
        await _transportSend.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await _persistence.ExecuteAsync(async ct => await action(ct), cancellationToken);
#else
        return await _persistence.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }

    public async ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        await _persistence.ExecuteAsync(async ct => { await action(ct); return 0; }, cancellationToken);
#else
        await _persistence.ExecuteAsync(ct => action(ct).AsTask(), cancellationToken);
#endif
    }
}
