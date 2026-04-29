# NATS 生产接入指南

这篇文档面向两种典型场景：

- 你要把 NATS 作为 Catga 的消息传输层
- 你要进一步把 JetStream / KV 用作 Catga 的持久化基础设施

在 Catga 现有实现里，NATS 是最接近“传输和持久化都能统一在一套 broker 体系里”的方案。

## 什么时候选 NATS

NATS 适合这几类场景：

- 你希望消息传输足够轻、吞吐高、部署形态云原生
- 你已经在使用 JetStream，并愿意把事件存储、Inbox、Outbox、Flow 状态继续放到 NATS 体系
- 你希望减少“消息 broker 一套、状态存储再一套”的组件数量
- 你需要竞争消费，但不想维护 RabbitMQ 的交换机 / 队列模型

如果团队更熟悉传统 AMQP 运维，或者非常依赖优先级队列、延迟交换机等 RabbitMQ 原生能力，RabbitMQ 会更自然。

## 能力边界

Catga 当前对 NATS 的分层是：

- `Catga.Transport.Nats` 负责消息传输
- `Catga.Persistence.Nats` 负责 JetStream / KV 持久化
- `AddNatsCompetingConsumer<TMessage>()` 提供 JetStream 竞争消费
- 传输层需要显式注册 `IMessageSerializer`
- 如果你要持久化或竞争消费，生产环境必须启用 JetStream

## 生产最小接入

推荐基础组合：

- `Catga`
- `Catga.Transport.Nats`
- `Catga.Serialization.MemoryPack`

```csharp
using Catga.DependencyInjection;
using NATS.Client.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<INatsConnection>(_ =>
{
    var opts = NatsOpts.Default with
    {
        Url = builder.Configuration["Nats:Url"]!
    };
    return new NatsConnection(opts);
});

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForProduction()
    .AddHostedServices(options =>
    {
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
    });

builder.Services.AddNatsTransport(new NatsTransportOptions
{
    SubjectPrefix = "prod.orders.",
    RequestTimeout = TimeSpan.FromSeconds(30)
});

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

如果你只想快速注册 transport，也可以直接：

```csharp
builder.Services.AddNatsTransport("nats://nats:4222");
```

但只要进入生产，通常还是建议自己显式注册 `INatsConnection`，把 URL、认证、TLS、重连策略统一放在一处。

## 生产推荐组合

### 只做传输

适合“团队已经有 NATS，但业务状态仍放数据库或其他存储”的场景。

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForProduction()
    .AddHostedServices();

builder.Services.AddNatsTransport("nats://nats:4222");
```

### NATS 传输 + NATS 持久化

这是 NATS 在 Catga 里的完整形态，也是最有代表性的组合。

```csharp
builder.Services.AddNatsConnection("nats://nats:4222");

var catga = builder.Services
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
    })
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddNatsTransport(new NatsTransportOptions
{
    SubjectPrefix = "prod.orders.",
    RequestTimeout = TimeSpan.FromSeconds(30)
});
```

这套组合会把下面这些能力都接到 NATS 体系里：

- `IEventStore`
- `IOutboxStore`
- `IInboxStore`
- `IIdempotencyStore`
- `IDeadLetterQueue`
- `IFlowStore`
- `IDslFlowStore`
- `ISnapshotStore`
- `IEnhancedSnapshotStore`
- `IProjectionCheckpointStore`
- `ISubscriptionStore`

### NATS 传输 + Redis 持久化

如果你已经用 NATS 做传输，但状态类能力想继续放 Redis，也完全可以这样接：

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

`NatsTransportOptions` 里，生产最常用的是这些：

| 配置项 | 作用 | 生产建议 |
|------|------|------|
| `SubjectPrefix` | Subject 前缀 | 强制做环境和系统隔离 |
| `Naming` | 自定义消息类型到 subject 的映射 | 有统一命名规范时再配 |
| `Batch` | 自动批量发送配置 | 高吞吐场景再开 |
| `MaxQueueLength` | 批量队列上限 | 开启 batch 时必须一起评估 |
| `RequestTimeout` | 请求-应答默认超时 | 先用 30 秒，再按 SLA 调整 |

### 持久化层

`NatsPersistenceOptions` 当前主要暴露的是名字，而不是 JetStream 全量调参：

| 配置项 | 作用 |
|------|------|
| `StreamPrefix` | 统一前缀 |
| `EventStreamName` | EventStore stream 名 |
| `OutboxStreamName` | Outbox stream 名 |
| `InboxStreamName` | Inbox stream 名 |
| `FlowBucketName` | Flow KV bucket 名 |
| `DslFlowBucketName` | DSL Flow KV bucket 名 |

这意味着：

- Catga 负责把核心 store 接起来
- 更细的 JetStream 副本数、存储类型、保留策略，优先走 NATS 原生配置和运维规范

## JetStream 前提

只要你用了下面任一能力，就应该把 JetStream 当成硬前提：

- `UseNats(...)`
- `AddNatsPersistence(...)`
- `AddNatsCompetingConsumer<TMessage>(...)`

建议：

- 生产启用多副本 JetStream
- stream / bucket 命名按环境隔离
- 把容量、保留策略、磁盘告警放进 NATS 平台侧标准配置

## 竞争消费

NATS 竞争消费基于 JetStream。

```csharp
builder.Services.AddNatsCompetingConsumer<OrderSubmitted>(
    subject: "orders.submitted",
    streamName: "ORDERS");
```

要点：

- 多实例使用相同配置时会共享消费负载
- `SubjectPrefix` 会自动参与 subject 解析
- `streamName` 建议显式写，便于和平台侧 stream 规范对齐

## 批量发送

NATS transport 支持自动批量发送配置，但它不是默认开启的能力。

适用场景：

- 大量小消息
- 你已经做过吞吐压测
- 你确认额外聚合延迟在业务上可接受

不建议的场景：

- 强交互型 request/reply
- 低延迟优先而非吞吐优先的链路

## 可观测性

NATS transport 已经接入 Catga tracing 和健康检查，生产建议至少补齐：

- `AddCatgaHealthChecks()`
- `/health`
- OpenTelemetry trace / metrics

相关文档：

- [OpenTelemetry 集成](../articles/opentelemetry-integration.md)
- [Hosting 配置](../guides/hosting-configuration.md)
- [可观测性索引](../observability/README.md)

## 常见坑

### 1. 只注册了 transport，没注册序列化器

`AddNatsTransport(...)` 仍然需要 `IMessageSerializer`。推荐直接 `UseMemoryPack()`。

### 2. 以为 `AddNatsTransport(string)` 就等于完整生产接入

它只帮你注册了 `INatsConnection` 和 transport。Outbox、EventStore、Flow 等状态能力不会自动出现。

### 3. JetStream 没开就上了持久化或竞争消费

这会直接影响 `UseNats(...)` 和 `AddNatsCompetingConsumer(...)` 的可用性。

### 4. Subject 前缀没做隔离

`prod.orders.`、`staging.orders.` 这种环境级隔离要一开始就定，否则后续迁移代价高。

## 结论

如果你的目标是：

- 传输和持久化尽量在一套 broker 体系里统一
- 保持较高吞吐和比较轻的云原生部署模型
- 同时需要 EventStore / Outbox / Inbox / Flow 等能力

那 NATS 是 Catga 当前最完整的一条 broker 生产路径。
