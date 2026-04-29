# RabbitMQ 生产接入指南

这篇文档解决的是：你已经决定在 Catga 里使用 RabbitMQ 作为消息传输层，生产环境应该怎么接，哪些能力是 RabbitMQ transport 原生提供的，哪些能力需要额外搭配持久化模块。

## 什么时候选 RabbitMQ

RabbitMQ 适合这几类场景：

- 团队已经把 RabbitMQ 当成标准 broker，运维体系和告警体系都现成
- 你需要稳定的 AMQP 队列语义、`exchange / queue / routing key` 模型
- 你需要基于 broker 做优先级队列、延迟交换机、共享队列竞争消费
- 你更看重“经典消息中间件接入”而不是把 broker 同时作为事件存储

如果你的重点是“消息传输 + 事件存储 + KV/Flow 持久化尽量放在同一套基础设施里”，NATS 或 Redis 往往更顺手。

## 能力边界

Catga 当前对 RabbitMQ 的定位很明确：

- `Catga.Transport.RabbitMQ` 负责消息传输
- 竞争消费由 `AddRabbitMqCompetingConsumer<TMessage>()` 提供
- 延迟投递依赖 RabbitMQ delayed message exchange 插件
- 优先级队列由 `MaxPriority` 开启
- 当前没有 RabbitMQ 持久化模块

这意味着如果你还需要下面这些能力，就要额外接入 Redis 或 NATS 持久化：

- Outbox / Inbox
- Idempotency Store
- Event Store / Snapshot Store
- Flow / Saga 状态存储
- Projection Checkpoint / Subscription Store
- Dead Letter Queue 持久化

## 生产最小接入

推荐的基础组合：

- `Catga`
- `Catga.Transport.RabbitMQ`
- `Catga.Serialization.MemoryPack`

```csharp
using Catga.DependencyInjection;
using Catga.Transport.RabbitMQ.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForProduction()
    .AddHostedServices(options =>
    {
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
        options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    });

builder.Services.AddRabbitMqTransport(options =>
{
    options.Uri = builder.Configuration["RabbitMQ:Uri"]!;
    options.Exchange = "catga.prod";
    options.ExchangeType = "topic";
    options.Prefix = "prod.orders.";
    options.DeclareExchange = true;
    options.DurableExchange = true;
    options.DurableQueues = true;
    options.AutoDeleteQueues = false;
    options.PrefetchCount = 64;
    options.RequestTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

注意：

- `AddCatga()` 之后必须注册序列化器，推荐 `UseMemoryPack()`
- `AddRabbitMqTransport(...)` 本身不会替你注册持久化存储
- 如果你开启了托管服务，建议同时启用健康检查

## 生产推荐组合

### 只做传输

适合“RabbitMQ 已经是组织标准答案，但业务状态不需要 Catga 持久化”的场景。

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForProduction()
    .AddHostedServices();

builder.Services.AddRabbitMqTransport("amqp://user:pass@rabbitmq:5672/%2f", options =>
{
    options.Exchange = "catga.prod";
    options.Prefix = "prod.billing.";
    options.PrefetchCount = 64;
});
```

### RabbitMQ 传输 + Redis 持久化

这是 RabbitMQ 生产接入里最常见、也最均衡的组合。

```csharp
using Catga.DependencyInjection;

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(builder.Configuration.GetConnectionString("Redis"))
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRabbitMqTransport(options =>
{
    options.Uri = builder.Configuration["RabbitMQ:Uri"]!;
    options.Exchange = "catga.prod";
    options.Prefix = "prod.orders.";
    options.PrefetchCount = 64;
    options.DurableQueues = true;
});
```

这套组合适合：

- RabbitMQ 负责业务消息流转
- Redis 负责 Outbox / Inbox / Idempotency / EventStore / Flow 等状态存储
- 团队不想再额外引入 JetStream 运维面

### RabbitMQ 传输 + NATS 持久化

只有在你已经有 NATS JetStream 基础设施，并且明确想把事件存储放到 JetStream 上时再用。

```csharp
builder.Services.AddNatsConnection("nats://nats:4222");

builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseNats(options =>
    {
        options.StreamPrefix = "CATGA";
        options.FlowBucketName = "flows";
        options.DslFlowBucketName = "dslflows";
    })
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRabbitMqTransport(options =>
{
    options.Uri = builder.Configuration["RabbitMQ:Uri"]!;
    options.Exchange = "catga.prod";
    options.Prefix = "prod.orders.";
});
```

## 关键配置项

`RabbitMqTransportOptions` 里和生产最相关的项主要是这些：

| 配置项 | 作用 | 生产建议 |
|------|------|------|
| `Uri` | RabbitMQ 连接地址 | 不要写死在代码里，统一走配置中心或 Secret |
| `Exchange` | 交换机名称 | 按环境隔离，例如 `catga.prod` |
| `ExchangeType` | 交换机类型 | 默认 `topic`，大多数业务足够 |
| `UseDelayedExchange` | 开启延迟交换机 | 只有确认插件已安装时再开 |
| `DeclareExchange` | 启动时自动声明交换机 | 自运维团队预建资源时可关，否则建议开 |
| `DurableExchange` | 交换机持久化 | 生产建议开 |
| `Prefix` | 路由前缀 | 必须做环境和系统隔离 |
| `MessageTtlMs` | 队列 TTL | 只在明确需要过期淘汰时设置 |
| `MaxPriority` | 优先级队列 | 只在业务真正需要优先级时开启 |
| `PrefetchCount` | 消费者预取数 | 从 `32` 或 `64` 起调，结合处理耗时压测 |
| `DurableQueues` | 队列持久化 | 生产建议开 |
| `AutoDeleteQueues` | 无消费者时自动删队列 | 生产通常关闭 |
| `RequestTimeout` | 请求-应答默认超时 | 先用 30 秒，再按 SLA 收紧 |
| `EndpointNaming` | 自定义消息到 endpoint 的映射 | 跨服务统一命名时再用 |

## 延迟和优先级

RabbitMQ 是当前 Catga 三个主力 broker 里，对“broker 原生高级投递特性”支持最直接的一种。

### 延迟投递

```csharp
builder.Services.AddRabbitMqTransport(options =>
{
    options.Uri = builder.Configuration["RabbitMQ:Uri"]!;
    options.Exchange = "catga.prod";
    options.UseDelayedExchange = true;
    options.ExchangeType = "topic";
});
```

前提：

- broker 已安装 delayed message exchange 插件
- 生产环境的变更流程允许声明 `x-delayed-message` 类型交换机

### 优先级队列

```csharp
builder.Services.AddRabbitMqTransport(options =>
{
    options.Uri = builder.Configuration["RabbitMQ:Uri"]!;
    options.MaxPriority = 10;
});
```

建议：

- 只有少数消息确实需要抢占执行时再开
- 不要把优先级当成“更快的默认路径”，否则容易把队列行为复杂化

## 竞争消费

RabbitMQ 竞争消费基于共享队列。

```csharp
builder.Services.AddRabbitMqCompetingConsumer<OrderSubmitted>(
    queueName: "orders.submitters",
    routingKey: "orders.submitted");
```

要点：

- 多实例只要使用同一个 `queueName`，就会竞争同一条消息流
- 如果 `routingKey` 不写，默认使用 `queueName`
- `Prefix` 会自动参与队列名和路由键解析，避免和其他系统冲突

## 运维建议

### 命名隔离

至少隔离三层：

- 环境：`dev / staging / prod`
- 系统：例如 `orders / billing / crm`
- bounded context：例如 `write / read / integration`

一个实用例子：

```csharp
options.Exchange = "catga.prod";
options.Prefix = "prod.orders.write.";
```

### 并发与背压

- `PrefetchCount` 不要默认拉满
- 先根据单条消息处理耗时估算并发，再做压测
- 如果消息处理依赖数据库，broker 并发要和数据库池容量一起看

### 超时和失败处理

- 给 `RequestTimeout` 设明确值，不要依赖无限等待
- 需要失败留痕时，配合 `UseDeadLetterQueue()` 和持久化 DLQ
- 需要“先落库再投递”时，必须配合 Outbox，而不是只靠 broker ACK

## 可观测性

RabbitMQ transport 已接入 Catga 健康检查和 tracing 基础设施，生产建议至少补齐三件事：

- `builder.Services.AddHealthChecks().AddCatgaHealthChecks()`
- `app.MapHealthChecks("/health")`
- 按 [OpenTelemetry 集成](../articles/opentelemetry-integration.md) 接出 trace / metrics

相关文档：

- [Hosting 配置](../guides/hosting-configuration.md)
- [可观测性索引](../observability/README.md)
- [Monitoring Guide](../production/MONITORING-GUIDE.md)

## 常见坑

### 1. 只注册了 `AddCatga()`，没注册序列化器

Catga transport 运行时需要 `IMessageSerializer`。生产环境直接用 `UseMemoryPack()` 最稳。

### 2. 开了 `UseDelayedExchange`，但 broker 没装插件

这种情况下交换机声明会失败，服务启动期就会出问题。

### 3. 以为 RabbitMQ transport 自带 Outbox / EventStore

当前没有 RabbitMQ persistence 模块。需要可靠消息和业务状态存储时，额外接 Redis 或 NATS。

### 4. `Prefix` 没做环境隔离

开发、测试、生产共用一个 broker 时，这是最容易引发串流量的配置错误。

## 结论

RabbitMQ 在 Catga 里最适合作为“标准企业 broker”的传输层接入：

- 传输能力成熟
- 竞争消费、优先级、延迟投递路径清晰
- 和 Redis 持久化组合时，生产落地成本最低

如果你想要的是“消息传输和状态存储尽量收敛到同一套 broker 世界观”，优先看 [NATS 生产接入指南](./nats-production.md) 和 [Redis 生产接入指南](./redis-production.md)。
