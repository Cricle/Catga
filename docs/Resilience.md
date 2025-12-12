# Catga Resilience Guide

This guide explains Catga's Polly-based resilience setup. Resilience is opt-in via UseResilience. It is AOT- and trimming-friendly, with zero reflection and no build warnings across net6/net8/net9.

- Opt-in resilience via UseResilience: retry, timeout, circuit breaker, bulkhead (v8 concurrency limiter).
- No default fallback provider is registered by AddCatga. If you use transports/persistence via DI, you must call UseResilience to register the provider.
- Multi-TFM: Polly v7 for net6; Polly v8 for net8+.

## Quick start

### Enable resilience
```csharp
services.AddCatga()
    .UseResilience(o =>
    {
        // Transport (example values)
        o.TransportRetryCount = 3;
        o.TransportRetryDelay = TimeSpan.FromMilliseconds(200);

        // Persistence bulkhead (optional – see notes below)
        // var c = Math.Max(Environment.ProcessorCount * 2, 16);
        // o.PersistenceBulkheadConcurrency = c;
        // o.PersistenceBulkheadQueueLimit = c;
    });
```

Notes:
- On net8+, if you do not explicitly set persistence bulkhead values, Catga applies conservative defaults: `PermitLimit = QueueLimit = max(CPU*2, 16)`.
- On net6, bulkhead for persistence is not applied (Polly v7 path). Other policies are supported via PolicyWrap.

## DI extensions (persistence)

All persistence stores are wrapped with `IResiliencePipelineProvider`. When `UseResilience` is not called, no provider is registered via DI. If you construct transports/persistence manually (e.g., in tests), you may pass an instance such as `DiagnosticResiliencePipelineProvider` or `DefaultResiliencePipelineProvider` explicitly.

- InMemory
```csharp
services.AddInMemoryPersistence(); // EventStore + Outbox + Inbox + DLQ
```

- NATS JetStream
```csharp
// Prerequisites: IMessageSerializer, INatsConnection
services.AddNatsPersistence(); // EventStore + Outbox + Inbox + DLQ + Idempotency
```

- Redis
```csharp
// Prerequisites: IMessageSerializer, IConnectionMultiplexer
services.AddRedisPersistence(); // Outbox + Inbox + Idempotency
```

## Observability

- Metrics (System.Diagnostics.Metrics)
  - catga.resilience.retries
  - catga.resilience.timeouts
  - catga.resilience.circuit.opened
  - catga.resilience.circuit.half_opened
  - catga.resilience.circuit.closed
  - catga.resilience.bulkhead.rejected
  - Most counters include a `component` tag: Mediator, Transport.Publish/Send, Persistence

- Tracing (System.Diagnostics.Activity via CatgaActivitySource)
  - resilience.retry (v8 includes `attempt` tag)
  - resilience.timeout
  - resilience.circuit.open / resilience.circuit.halfopen / resilience.circuit.closed
  - resilience.bulkhead.rejected

## Policies per area (v8)

- Mediator: Concurrency limiter (bulkhead), CircuitBreaker, Timeout
- Transport: Concurrency limiter (bulkhead), CircuitBreaker, Timeout, Retry
- Persistence: CircuitBreaker, Timeout, Retry; optional Concurrency limiter when configured

## Performance guidance

- Enable UseResilience when you need policy enforcement.
- For manual composition (non-DI), a diagnostic no-op provider can be passed to minimize overhead in benchmarks.
- Persistence bulkhead defaults are conservative; tune based on throughput and datastore behavior.

## Compatibility

- net6: Polly v7 PolicyWrap path.
- net8+: Polly v8 ResiliencePipeline path (with RateLimiter bulkhead and enhanced callbacks/tags).

## Troubleshooting

- If you see no bulkhead rejections, your limits may be too high for the test load; try lowering concurrency and queue limits.
- Ensure IMessageSerializer and the transport/persistence client dependencies are registered for NATS/Redis scenarios.


