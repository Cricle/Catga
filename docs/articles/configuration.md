# Configuration Guide

Complete guide to configuring Catga for different scenarios.

## Basic Configuration

### Minimal Setup (Development)

```csharp
builder.Services
    .AddCatga()
    .AddInMemoryTransport()
    .AddInMemoryEventStore();
```

### Production Setup

```csharp
builder.Services
    .AddCatga(options =>
    {
        options.IdempotencyShardCount = 64;
        options.IdempotencyRetentionHours = 24;
        options.EnableDeadLetterQueue = true;
    })
    .AddRedisTransport(options =>
    {
        options.ConnectionString = "redis-cluster:6379";
        options.DefaultQoS = QualityOfService.AtLeastOnce;
        options.ConnectTimeout = 10000;
        options.Mode = RedisMode.Cluster;
    })
    .AddNatsPersistence(options =>
    {
        options.EventStreamName = "PROD_EVENTS";
        options.EventStoreOptions = new NatsJSStoreOptions
        {
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(90),
            Replicas = 3,
            Compression = StreamConfigCompression.S2
        };
    });
```

## Transport Configuration

### InMemory Transport

```csharp
services.AddInMemoryTransport();
```

**Use cases**: Development, testing, single-instance applications

### Redis Transport

```csharp
services.AddRedisTransport(options =>
{
    // Connection
    options.ConnectionString = "redis:6379,ssl=true";
    options.ConnectTimeout = 5000;
    options.SyncTimeout = 3000;
    options.AsyncTimeout = 3000;

    // QoS
    options.DefaultQoS = QualityOfService.AtLeastOnce;
    options.ConsumerGroup = "my-service";

    // High Availability
    options.Mode = RedisMode.Sentinel;
    options.SentinelServiceName = "mymaster";
    options.UseSsl = true;

    // Performance
    options.KeepAlive = 60;
    options.ConnectRetry = 3;
});
```

**Configuration options**:
- **QoS 0**: Pub/Sub (fast, no guarantees)
- **QoS 1**: Streams (reliable, acknowledgment)

### NATS Transport

```csharp
services.AddNatsTransport(connectionString: "nats://nats:4222");
```

**Use cases**: High throughput, cloud-native, multi-region

## Persistence Configuration

### NATS JetStream Persistence

```csharp
services.AddNatsPersistence(options =>
{
    // Stream names
    options.EventStreamName = "EVENTS";
    options.OutboxStreamName = "OUTBOX";
    options.InboxStreamName = "INBOX";

    // Event Store options
    options.EventStoreOptions = new NatsJSStoreOptions
    {
        Retention = StreamConfigRetention.Limits,
        MaxAge = TimeSpan.FromDays(365),
        MaxMessages = 10_000_000,
        Replicas = 3,
        Storage = StreamConfigStorage.File,
        Compression = StreamConfigCompression.S2
    };

    // Outbox options
    options.OutboxStoreOptions = new NatsJSStoreOptions
    {
        Retention = StreamConfigRetention.WorkQueue,
        MaxAge = TimeSpan.FromHours(24),
        Replicas = 3
    };
});
```

### Redis Persistence

```csharp
services.AddRedisOutboxStore();
services.AddRedisInboxStore();
```

## Serialization Configuration

### JSON Serializer (Default)

```csharp
services.AddJsonMessageSerializer();
```

### MemoryPack Serializer (Fastest)

```csharp
services.AddMemoryPackMessageSerializer();
```

## Advanced Options

### Idempotency Configuration

```csharp
services.AddCatga(options =>
{
    // Sharding for performance
    options.IdempotencyShardCount = 64;

    // Retention
    options.IdempotencyRetentionHours = 24;
});
```

### Dead Letter Queue

```csharp
services.AddCatga(options =>
{
    options.EnableDeadLetterQueue = true;
    options.DeadLetterQueueMaxSize = 10000;
});
```

### Global Endpoint Naming

```csharp
// Program.cs — explicit configuration (one place, global effect)
builder.Services.AddCatga(o =>
{
    o.EndpointNamingConvention = t => $"shop.orders.{t.Name}".ToLowerInvariant();
});

// Or via source generator attributes (zero-config, recommended)
using Catga;
[assembly: CatgaMessageDefaults(App = "shop", BoundedContext = "orders", Separator = ".", LowerCase = true)]

// Optional per-message override
[CatgaMessage(Name = "special.order.created")]
public record OrderCreatedEvent(string OrderId) : IEvent;
```

Notes:
- If `EndpointNamingConvention` is not explicitly set, `AddCatga()` uses the generated mapping.
- Transports precedence:
  - NATS/Redis: `TransportOptions.Naming` > `CatgaOptions.EndpointNamingConvention` > type name
  - InMemory: naming is used for observability tags/metrics only (routing unaffected)

### Reliability Toggles (conditional behaviors)

```csharp
builder.Services
    .AddCatga()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue();
```

Notes:
- Behaviors activate only if required dependencies are registered (e.g., Inbox/Outbox stores, IDeadLetterQueue).
- Safe to enable in any environment; missing dependencies simply skip the behavior.

### Custom Behaviors

```csharp
services.AddCatga()
    .AddBehavior(typeof(CustomValidationBehavior<,>))
    .AddBehavior(typeof(AuditBehavior<,>));
```

## Environment-Specific Configuration

### Development

```csharp
#if DEBUG
services.AddInMemoryTransport();
#else
services.AddRedisTransport(...);
#endif
```

### Staging

```csharp
services.AddRedisTransport(options =>
{
    options.ConnectionString = config["Redis:ConnectionString"];
    options.DefaultQoS = QualityOfService.AtLeastOnce;
});
```

### Production

```csharp
services
    .AddRedisTransport(options =>
    {
        options.Mode = RedisMode.Cluster;
        options.UseSsl = true;
        options.AbortOnConnectFail = true;
    })
    .AddNatsPersistence(options =>
    {
        options.EventStoreOptions.Replicas = 3;
    });
```

## Performance Tuning

### High Throughput

```csharp
services.AddNatsTransport(); // Fastest transport

services.AddMemoryPackMessageSerializer(); // Fastest serialization

services.AddCatga(options =>
{
    options.IdempotencyShardCount = 128; // More shards
});
```

### Low Latency

```csharp
services.AddRedisTransport(options =>
{
    options.DefaultQoS = QualityOfService.AtMostOnce; // No ack overhead
    options.KeepAlive = 30; // Frequent keep-alive
});
```

### Memory Optimization

```csharp
services.AddCatga(options =>
{
    options.IdempotencyRetentionHours = 1; // Short retention
    options.DeadLetterQueueMaxSize = 1000; // Smaller DLQ
});
```

## Next Steps

- [Transport Layer](transport-layer.md) - Detailed transport comparison
- [Persistence Layer](persistence-layer.md) - Persistence strategies
- [Native AOT Deployment](aot-deployment.md) - Deploy with Native AOT

