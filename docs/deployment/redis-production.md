# Redis 生产接入指南

这篇文档面向“Redis 既做消息传输，也承接 Catga 状态持久化”的生产接入场景。

在 Catga 当前实现里，Redis 是最务实的一条生产路线：

- 基础设施普及率高
- 运维门槛相对低
- 传输和持久化都已经有成熟扩展
- 跟 RabbitMQ 组合也很自然

## 什么时候选 Redis

Redis 适合这几类场景：

- 团队已经把 Redis 作为默认基础设施，想少引入新组件
- 你需要 Catga 的 Outbox / Inbox / Idempotency / EventStore / Flow 存储
- 你希望 RabbitMQ 或 NATS 之外，还有一条成本更低、接受度更高的生产接入路径
- 你能接受“高级 broker 语义不如 RabbitMQ 丰富，但落地足够稳”

如果你的重点是传统企业消息中间件能力，优先看 [RabbitMQ 生产接入指南](./rabbitmq-production.md)。如果你的重点是 JetStream 一体化世界，优先看 [NATS 生产接入指南](./nats-production.md)。

## 能力边界

Catga 当前对 Redis 的支持分成两层：

- `Catga.Transport.Redis` 负责消息传输
- `Catga.Persistence.Redis` 负责持久化存储

其中：

- transport 支持 Redis Pub/Sub 与 Streams 场景
- 竞争消费由 `AddRedisCompetingConsumer<TMessage>()` 提供
- persistence 覆盖 Outbox / Inbox / Idempotency / EventStore / Snapshot / Flow / Projection / Subscription / Distributed Lock
- transport 和 persistence 都要求显式注册序列化器

## 生产最小接入

推荐基础组合：

- `Catga`
- `Catga.Transport.Redis`
- `Catga.Persistence.Redis`
- `Catga.Serialization.MemoryPack`

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")!;

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redisConnectionString)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices(options =>
    {
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
    });

builder.Services.AddRedisTransport(options =>
{
    options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
    options.ChannelPrefix = "prod.orders.";
    options.ConsumerGroup = "orders-api";
    options.ConsumerName = Environment.MachineName;
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.PoolSize = Math.Max(Environment.ProcessorCount, 4);
});

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

这基本就是 Redis 生产路径的标准接法。

## 生产推荐组合

### Redis 传输 + Redis 持久化

这是 Redis 在 Catga 里的主推荐方案。

```csharp
var redis = builder.Configuration.GetConnectionString("Redis")!;

var catga = builder.Services
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

优点：

- 基础设施统一
- 接法最短
- 功能覆盖完整
- 对多数标准业务系统已经够用

### RabbitMQ 传输 + Redis 持久化

如果组织内 RabbitMQ 是标准 broker，而你又需要 Catga 持久化能力，这是非常常见的组合。

```csharp
var redis = builder.Configuration.GetConnectionString("Redis")!;

builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRabbitMqTransport(builder.Configuration["RabbitMQ:Uri"]!, options =>
{
    options.Exchange = "catga.prod";
    options.Prefix = "prod.orders.";
    options.PrefetchCount = 64;
});
```

### NATS 传输 + Redis 持久化

适合你要用 NATS 做轻量传输，但状态仍然希望留在 Redis 的场景。

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(builder.Configuration.GetConnectionString("Redis"))
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddNatsTransport("nats://nats:4222");
```

## 关键配置项

### 传输层

`RedisTransportOptions` 里，生产最常用的是这些：

| 配置项 | 作用 | 生产建议 |
|------|------|------|
| `ChannelPrefix` | Channel / Stream 前缀 | 必须做环境和系统隔离 |
| `Naming` | 自定义消息类型到 channel 的映射 | 有统一命名规范时再启用 |
| `ConsumerGroup` | Streams 消费组 | 做竞争消费或可靠流处理时设置 |
| `ConsumerName` | Streams 消费者名 | 建议绑定实例名 |
| `Batch` | 自动批量发送配置 | 高吞吐再开 |
| `MaxQueueLength` | 批量队列上限 | 开启 batch 时一起评估 |
| `RequestTimeout` | 请求-应答默认超时 | 建议显式设置 |
| `RegistConnection` | 是否自动注册连接池 | 默认即可，已有连接时再关闭 |
| `ConfigurationOptions` | Redis 连接配置 | 生产建议显式传入 |
| `PoolSize` | 连接池大小 | 按 CPU 和并发压测调 |
| `SelectionStrategy` | 池中连接选择策略 | 默认 `RoundRobin`，高竞争场景再评估 `LoadBased` |

### 持久化层

`RedisPersistenceOptions` 主要控制 key 前缀和保留时间：

| 配置项 | 作用 |
|------|------|
| `IdempotencyKeyPrefix` | 幂等 key 前缀 |
| `IdempotencyExpiry` | 幂等记录过期时间 |
| `InboxKeyPrefix` | Inbox key 前缀 |
| `InboxProcessedRetention` | 已处理 Inbox 记录保留时间 |
| `InboxDefaultLockDuration` | Inbox 默认锁时长 |
| `OutboxKeyPrefix` | Outbox key 前缀 |
| `OutboxPublishedRetention` | 已发布 Outbox 记录保留时间 |
| `OutboxPollingInterval` | Outbox 扫描间隔 |
| `OutboxBatchSize` | Outbox 批量处理大小 |

## Redis persistence 覆盖范围

`UseRedis(...)` 会把 Catga 常见状态类能力基本接满：

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

这也是 Redis 在 Catga 里非常适合做“默认生产后端”的原因。

## 竞争消费

Redis 竞争消费基于 Streams。

```csharp
builder.Services.AddRedisCompetingConsumer<OrderSubmitted>(
    streamKey: "stream:orders.submitted");
```

要点：

- `stream:` 前缀可以显式写出来，便于区分 Pub/Sub 和 Streams
- 如果没写完整前缀，`ChannelPrefix` 会自动参与解析
- 多实例共享同一个 stream key 时可以竞争消费

## 连接与容量建议

### 连接池

- 不要默认把 `PoolSize` 拉到很大
- 先从 `Environment.ProcessorCount` 或略高于 CPU 核数开始
- 压测时同时观察 Redis CPU、命令延迟和应用线程池

### 命名隔离

建议统一约定：

- `prod.orders.` 这种逻辑前缀给 transport 用
- `catga:outbox:`、`catga:inbox:` 这种 key 前缀给 persistence 用
- 不同环境不要共用前缀

### Outbox 扫描

- `OutboxPollingInterval` 太小会给 Redis 带来持续扫描压力
- `OutboxBatchSize` 太大又会增加单批处理时延
- 这两个值要一起压测，不要单独看

## 可观测性

Redis transport 和 Redis persistence 都接入了 Catga 健康检查 / tracing 体系。生产建议至少补齐：

- `AddCatgaHealthChecks()`
- `/health`
- OpenTelemetry trace / metrics

相关文档：

- [OpenTelemetry 集成](../articles/opentelemetry-integration.md)
- [Hosting 配置](../guides/hosting-configuration.md)
- [可观测性索引](../observability/README.md)

## 常见坑

### 1. 只配了 `AddRedisTransport(...)`，没配 `UseRedis(...)`

这样只有传输，没有 Outbox / EventStore / Flow 等持久化能力。

### 2. 已有 `IConnectionMultiplexer`，却又让 transport 重复注册连接

这时要改用现有连接接入，或者把 `RegistConnection` 关掉，避免双重连接管理。

### 3. `ConsumerGroup` / `ConsumerName` 设计随意

Redis Streams 的组名和消费者名会直接影响消息消费行为，实例命名必须稳定、可定位。

### 4. 把 Redis 当成无限容量事件库

Redis 可以承接 Catga 的事件与状态存储，但容量规划、保留周期、冷热分层仍然要做。

## 结论

如果你要在 Catga 里找一条最务实、最容易成为“默认生产答案”的 broker 路线，Redis 是当前最现实的一条：

- 基础设施接受度高
- 传输和持久化都齐
- 跟 RabbitMQ / NATS 组合也灵活

对大多数标准业务系统，`Redis 传输 + Redis 持久化` 或 `RabbitMQ 传输 + Redis 持久化` 都已经足够进入稳定生产阶段。
