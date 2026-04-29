# Catga 托管服务配置指南

这篇文档解决的是：`AddHostedServices()` 到底会启用什么，什么时候该开，生产里怎么配。

如果你只记一条，记这个：

- `AddHostedServices()` 不是可有可无的装饰项，它决定了 Catga 的 transport 生命周期、恢复机制和 outbox 后台处理是否真正跑起来

## 托管服务包含什么

Catga 当前内置 3 个核心托管服务：

- `RecoveryHostedService`
- `TransportHostedService`
- `OutboxProcessorService`

它们分别负责：

- 恢复与健康自愈
- transport 初始化、停止接单、优雅停机
- outbox 后台扫描与投递

## 最小接法

### 开发 / 测试

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

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

### 生产

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
    .AddHostedServices(options =>
    {
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.Recovery.MaxRetries = 5;
        options.Recovery.RetryDelay = TimeSpan.FromSeconds(10);

        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
        options.OutboxProcessor.BatchSize = 100;
        options.OutboxProcessor.ErrorDelay = TimeSpan.FromSeconds(10);

        options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    });

builder.Services.AddRedisTransport(redis);

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

## `AddHostedServices()` 实际做了什么

`AddHostedServices()` 会根据 `HostingOptions` 注册：

- `RecoveryHostedService`
- `TransportHostedService`
- `OutboxProcessorService`

其中开关来自：

- `EnableAutoRecovery`
- `EnableTransportHosting`
- `EnableOutboxProcessor`

默认都是开启的。

## 配置项

### HostingOptions

| 配置项 | 默认值 | 作用 |
|------|------|------|
| `EnableAutoRecovery` | `true` | 是否注册恢复服务 |
| `EnableTransportHosting` | `true` | 是否托管 transport 生命周期 |
| `EnableOutboxProcessor` | `true` | 是否启用 outbox 后台处理 |
| `Recovery` | - | 恢复策略配置 |
| `OutboxProcessor` | - | outbox 处理配置 |
| `ShutdownTimeout` | `30s` | 优雅停机超时时间 |

### RecoveryOptions

| 配置项 | 默认值 | 作用 |
|------|------|------|
| `CheckInterval` | `30s` | 健康检查间隔 |
| `MaxRetries` | `3` | 最大恢复重试次数 |
| `RetryDelay` | `5s` | 基础重试延迟 |
| `EnableAutoRecovery` | `true` | 是否自动恢复 |
| `UseExponentialBackoff` | `true` | 是否指数退避 |

### OutboxProcessorOptions

| 配置项 | 默认值 | 作用 |
|------|------|------|
| `ScanInterval` | `5s` | outbox 扫描频率 |
| `BatchSize` | `100` | 每轮处理条数 |
| `ErrorDelay` | `10s` | 出错后的延迟 |
| `CompleteCurrentBatchOnShutdown` | `true` | 停机时是否完成当前批次 |

## 三个托管服务分别什么时候重要

### RecoveryHostedService

适合：

- 长时间运行服务
- 需要自动恢复 broker / persistence 组件
- 生产环境希望有基本自愈能力

### TransportHostedService

适合：

- 所有真实 broker 场景
- 需要优雅停机
- 不希望在进程退出时直接中断消息处理

### OutboxProcessorService

适合：

- 开启了 `UseOutbox()`
- 使用 `SendLaterAsync / SendAtAsync`
- 需要后台持续投递待发消息

如果你用了 outbox，但没启托管服务，这通常就是错误配置。

## 健康检查

Catga 当前健康检查入口是：

```csharp
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

默认会注册：

- `catga_transport`
- `catga_persistence`
- `catga_recovery`

标签约定：

- `ready`
- `live`

常见映射方式：

```csharp
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

## 常见组合

### InMemory 开发组合

```csharp
var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

builder.Services.AddInMemoryTransport();
```

### Redis 默认生产组合

```csharp
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

### RabbitMQ + Redis 组合

```csharp
var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRabbitMqTransport(rabbitMqUri);
```

### NATS + NATS 组合

```csharp
builder.Services.AddNatsConnection("nats://nats:4222");

var catga = builder.Services
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

## 常见错误

### 1. 把 `AddInMemoryTransport()` 链在 `CatgaServiceBuilder` 上

不对。

`AddInMemoryTransport()` 是 `IServiceCollection` 扩展，不是 `CatgaServiceBuilder` 扩展。

正确写法是分两段：

```csharp
var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

builder.Services.AddInMemoryTransport();
```

### 2. 开了 `UseOutbox()`，没启 `AddHostedServices()`

这样 outbox store 在，但后台处理器不一定跑起来。

### 3. 健康检查只映射 `/health`，不区分 `ready/live`

本地可以接受，生产最好拆开。

### 4. 把 hosting 当成 broker 专属配置

不是。即使是 InMemory，只要你要验证恢复、优雅停机、outbox 调度，hosting 仍然重要。

## 继续往下看

- 配置总入口：看 [configuration.md](../articles/configuration.md)
- broker 生产选型：看 [broker-production-overview.md](../deployment/broker-production-overview.md)
- 可观测性：看 [opentelemetry-integration.md](../articles/opentelemetry-integration.md)
