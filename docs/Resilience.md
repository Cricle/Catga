# Catga 弹性与恢复指南

这篇文档解决的是：`UseResilience()` 什么时候需要显式调用，Catga 当前到底把哪些重试 / 超时 / 熔断能力接进了运行时。

## 当前结论

Catga 的弹性能力基于 Polly，入口是：

```csharp
services.AddCatga()
    .UseResilience();
```

它主要覆盖：

- retry
- timeout
- circuit breaker
- bulkhead / concurrency limiter

## 什么时候需要显式调用 `UseResilience()`

### 需要显式调用

如果你只是：

- `AddCatga()`
- 接 serializer
- 接 transport 或其他自定义基础设施

并且希望有完整弹性策略，就应该显式调用：

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseResilience();
```

### 通常不需要单独再调

当前这些 persistence 组合会自动确保默认 resilience provider 已注册：

- `UseInMemory()`
- `UseRedis(...)`
- `UseNats(...)`

例如：

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseRedis(redisConnectionString);
```

这类组合已经会把 resilience 主路径接起来。

### 特殊情况

`UseMediatorAutoBatching(...)` 在启用自动批处理时，也会确保默认 provider 存在。

但如果你需要明确控制策略参数，仍然建议显式写出 `UseResilience(...)`。

## 推荐接法

### 最小显式配置

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseResilience(options =>
    {
        options.TransportRetryCount = 3;
        options.TransportRetryDelay = TimeSpan.FromMilliseconds(200);
    });
```

### 和生产 persistence 组合使用

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseResilience(options =>
    {
        options.TransportRetryCount = 3;
        options.TransportRetryDelay = TimeSpan.FromMilliseconds(200);
    })
    .UseRedis(redisConnectionString)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();
```

## 当前运行时行为

Catga 里和弹性相关的主路径包括：

- mediator pipeline
- transport publish / send
- persistence 访问
- mediator auto-batching 刷新

不同 TFM 下底层实现会有差异：

- `net6`: Polly v7 路径
- `net8+`: Polly v8 `ResiliencePipeline` 路径

文档层面更重要的结论是：业务接入时不用围着 TFM 分支写代码，直接围绕 `UseResilience()` 配置即可。

## 可观测性

当前 resilience 相关指标 / 事件会进入标准 observability 路径。

常见指标包括：

- `catga.resilience.retries`
- `catga.resilience.timeouts`
- `catga.resilience.circuit.opened`
- `catga.resilience.bulkhead.rejected`

如果你要把这些指标接进 tracing / monitoring，继续看：

- [可观测性索引](./observability/README.md)
- [生产监控指南](./production/MONITORING-GUIDE.md)

## 常见问题

### Q: 为什么我没调 `UseResilience()` 也能跑？

A: 因为某些 `UseXxx()` persistence 组合会自动补默认 provider。

### Q: 那为什么还建议显式写？

A: 因为显式写出来更清楚：

- 配置意图更明确
- 策略参数更可控
- 文档和代码更容易对齐

### Q: 什么时候最该显式启用？

A: 这几类场景最值得显式写出：

- 生产 broker 接入
- 需要自定义 retry / timeout / bulkhead 参数
- 使用 mediator auto-batching
- 需要把 resilience 作为可观测性重点关注项

## 下一步看什么

- 宿主与后台服务：看 [guides/hosting-configuration.md](./guides/hosting-configuration.md)
- broker 生产接入：看 [deployment/broker-production-overview.md](./deployment/broker-production-overview.md)
- 监控与 tracing：看 [observability/README.md](./observability/README.md)
