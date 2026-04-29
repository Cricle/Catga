# Catga 配置指南

这篇文档只回答一件事：Catga 在真实项目里应该怎么配，哪些是必配项，哪些是按场景追加。

如果你要看 broker 生产选型，不要只看这一篇，还要一起看：

- [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)
- [Redis 生产接入](../deployment/redis-production.md)
- [RabbitMQ 生产接入](../deployment/rabbitmq-production.md)
- [NATS 生产接入](../deployment/nats-production.md)

## 先记住 4 条规则

1. `AddCatga()` 之后必须注册序列化器，默认推荐 `UseMemoryPack()`
2. `transport` 和 `persistence` 是两层能力，不要混成一件事
3. 需要 `Inbox / Outbox / DLQ / scheduling` 时，要把对应 store 和 behavior 一起接上
4. 进入生产时，通常还要补 `AddHostedServices()` 和 `AddCatgaHealthChecks()`

## 最小可用配置

### 开发 / 测试

这是最短、也最稳的本地接法：

```csharp
using Catga.DependencyInjection;

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

builder.Services.AddInMemoryTransport();

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

这套配置的特点：

- transport 用 `InMemory`
- persistence 也用 `InMemory`
- 适合本地开发、集成测试、单机验证

### 生产默认答案

如果你没有既定 broker 平台约束，优先从 `Redis + Redis` 开始：

```csharp
using Catga.DependencyInjection;

var redis = builder.Configuration.GetConnectionString("Redis")!;

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices(options =>
    {
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
        options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    });

builder.Services.AddRedisTransport(redis);

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

## 先分清两层配置

### Transport

负责消息收发：

- `AddInMemoryTransport()`
- `AddRedisTransport(...)`
- `AddNatsTransport(...)`
- `AddRabbitMqTransport(...)`

### Persistence

负责业务状态和可靠性存储：

- `UseInMemory()`
- `UseRedis(...)`
- `UseNats(...)`

这层通常会覆盖：

- Event Store
- Snapshot Store
- Outbox / Inbox
- Idempotency
- Flow / Saga 状态
- Dead Letter Queue
- Projection Checkpoint / Subscription Store

结论很简单：

- `AddRedisTransport(...)` 不等于 `UseRedis(...)`
- `AddNatsTransport(...)` 不等于 `UseNats(...)`
- RabbitMQ 当前只有 transport，没有 RabbitMQ persistence

## 序列化配置

### 推荐方案：MemoryPack

Catga 当前默认推荐直接用 `UseMemoryPack()`：

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack();
```

原因：

- 当前仓库的 analyzer 也是按这个路径推荐
- AOT 兼容性最好
- 运行时分配和性能表现更稳

### 自定义序列化器

如果你不用 MemoryPack，就需要自己注册 `IMessageSerializer`：

```csharp
builder.Services.AddSingleton<IMessageSerializer, CustomJsonMessageSerializer>();

builder.Services.AddCatga();
```

注意：

- 当前仓库没有一个“开箱即用”的 `AddJsonMessageSerializer()` 扩展可直接替代 `UseMemoryPack()`
- 只要 transport / persistence 用到了消息序列化，`IMessageSerializer` 就是必需依赖

详细说明看：

- [序列化指南](../guides/serialization.md)

## Transport 配置

### InMemory

```csharp
builder.Services.AddInMemoryTransport();
```

适合：

- 本地开发
- 自动化测试
- 不需要跨进程 broker 的场景

### Redis

```csharp
using StackExchange.Redis;

builder.Services.AddRedisTransport(options =>
{
    options.ConfigurationOptions = ConfigurationOptions.Parse("redis:6379");
    options.ChannelPrefix = "prod.orders.";
    options.ConsumerGroup = "orders-api";
    options.ConsumerName = Environment.MachineName;
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.PoolSize = Math.Max(Environment.ProcessorCount, 4);
});
```

真实可配项重点看这些：

- `ChannelPrefix`
- `Naming`
- `ConsumerGroup`
- `ConsumerName`
- `Batch`
- `MaxQueueLength`
- `RequestTimeout`
- `RegistConnection`
- `ConfigurationOptions`
- `PoolSize`
- `SelectionStrategy`

### NATS

```csharp
using NATS.Client.Core;

builder.Services.AddSingleton<INatsConnection>(_ =>
{
    var opts = NatsOpts.Default with { Url = "nats://nats:4222" };
    return new NatsConnection(opts);
});

builder.Services.AddNatsTransport(new NatsTransportOptions
{
    SubjectPrefix = "prod.orders.",
    RequestTimeout = TimeSpan.FromSeconds(30)
});
```

也可以直接：

```csharp
builder.Services.AddNatsTransport("nats://nats:4222");
```

但进入生产后，通常更建议自己显式注册 `INatsConnection`。

### RabbitMQ

```csharp
builder.Services.AddRabbitMqTransport(options =>
{
    options.Uri = "amqp://user:pass@rabbitmq:5672/%2f";
    options.Exchange = "catga.prod";
    options.ExchangeType = "topic";
    options.Prefix = "prod.orders.";
    options.PrefetchCount = 64;
    options.DurableExchange = true;
    options.DurableQueues = true;
    options.AutoDeleteQueues = false;
    options.RequestTimeout = TimeSpan.FromSeconds(30);
});
```

RabbitMQ transport 的真实关键项包括：

- `Uri`
- `Exchange`
- `ExchangeType`
- `UseDelayedExchange`
- `DeclareExchange`
- `DurableExchange`
- `Prefix`
- `MessageTtlMs`
- `MaxPriority`
- `PrefetchCount`
- `DurableQueues`
- `AutoDeleteQueues`
- `RequestTimeout`
- `EndpointNaming`

## Persistence 配置

### InMemory persistence

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory();
```

适合：

- 本地开发
- 单机场景
- 测试环境

### Redis persistence

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(builder.Configuration.GetConnectionString("Redis"));
```

`UseRedis(...)` 会注册当前最完整的一组生产持久化能力，包括：

- `IOutboxStore`
- `IInboxStore`
- `IIdempotencyStore`
- `IEventStore`
- `ISnapshotStore`
- `IEnhancedSnapshotStore`
- `IDeadLetterQueue`
- `IFlowStore`
- `IDslFlowStore`
- `IProjectionCheckpointStore`
- `ISubscriptionStore`
- `IDistributedLockProvider`

### NATS persistence

```csharp
builder.Services.AddNatsConnection("nats://nats:4222");

builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseNats(options =>
    {
        options.StreamPrefix = "CATGA";
        options.EventStreamName = "CATGA_EVENTS";
        options.OutboxStreamName = "CATGA_OUTBOX";
        options.InboxStreamName = "CATGA_INBOX";
        options.FlowBucketName = "flows";
        options.DslFlowBucketName = "dslflows";
    });
```

`UseNats(...)` 适合你已经明确使用 JetStream / KV 来承接 Catga 状态能力的场景。

## 可靠性行为

下面这些是行为开关，不是存储实现本身：

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue();
```

可以这样理解：

- `UseInbox()`：开启 InboxBehavior
- `UseOutbox()`：开启 OutboxBehavior
- `UseDeadLetterQueue()`：开启 DeadLetterBehavior

要点：

- 这些 behavior 只有在所需依赖已注册时才真正工作
- 例如 `UseOutbox()` 不是存储注册，它要求 `IOutboxStore` 已存在
- 调度发送 `SendLaterAsync / SendAtAsync` 依赖 Outbox

## Hosting 和健康检查

进入生产后，通常不要只停留在 `AddCatga() + transport`。

推荐补齐：

```csharp
var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .AddHostedServices();

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

`AddHostedServices()` 主要补这些运行期能力：

- RecoveryHostedService
- TransportHostedService
- OutboxProcessorService

`AddCatgaHealthChecks()` 会补这些检查：

- `catga_transport`
- `catga_persistence`
- `catga_recovery`

详细说明看：

- [Hosting 配置](../guides/hosting-configuration.md)
- [OpenTelemetry 集成](./opentelemetry-integration.md)

## 常见生产组合

### 1. 默认生产答案：Redis + Redis

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRedisTransport(redis);
```

### 2. 企业标准 broker：RabbitMQ + Redis

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRabbitMqTransport("amqp://user:pass@rabbitmq:5672/%2f", options =>
{
    options.Exchange = "catga.prod";
    options.Prefix = "prod.orders.";
    options.PrefetchCount = 64;
});
```

### 3. 一体化 broker 世界：NATS + NATS

```csharp
builder.Services.AddNatsConnection("nats://nats:4222");

builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseNats()
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddNatsTransport("nats://nats:4222");
```

## 命名约定

可以统一在 `CatgaOptions` 上配置 endpoint naming：

```csharp
builder.Services.AddCatga(options =>
{
    options.EndpointNamingConvention = t => $"shop.orders.{t.Name}".ToLowerInvariant();
});
```

优先级说明：

- NATS / Redis transport：`TransportOptions.Naming` 优先于 `CatgaOptions.EndpointNamingConvention`
- RabbitMQ transport：`RabbitMqTransportOptions.EndpointNaming`
- 如果都不配，回退到默认类型名 / 生成器映射

## 环境建议

### Development

- `UseInMemory()`
- `AddInMemoryTransport()`
- 开启 `AddHostedServices()`

### Staging

- 优先用和生产相同的 broker 组合
- 至少保证命名前缀和生产隔离

### Production

- 明确 `serializer`
- 明确 `transport`
- 明确 `persistence`
- 明确 `Inbox / Outbox / DLQ` 是否开启
- 明确 `HostedServices / HealthChecks / Tracing`

## 常见错误

### 1. 调了 `AddCatga()`，没注册序列化器

最稳的修法就是：

```csharp
builder.Services.AddCatga().UseMemoryPack();
```

### 2. 只注册了 transport，没注册 persistence

比如：

```csharp
builder.Services.AddRedisTransport(redis);
```

这只能发消息，不会自动给你 Outbox / EventStore / Flow 状态存储。

### 3. 只开了 `UseOutbox()`，却没有 `IOutboxStore`

behavior 不是 store，本身不会替你补基础设施。

### 4. 把 benchmark 结果直接当成 broker 选型结论

性能对比回答的是运行时成本，不直接回答生产默认 broker 路径。真正选型要一起看部署文档。

## 下一步看什么

- 生产 broker 怎么选：看 [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)
- 序列化怎么定：看 [序列化指南](../guides/serialization.md)
- 托管和健康检查怎么接：看 [Hosting 配置](../guides/hosting-configuration.md)
- 运行时 benchmark 怎么读：看 [Benchmark Results](../BENCHMARK-RESULTS.md)
