# Catga

面向现代 .NET 应用的 CQRS、消息协作和流程编排基础框架。

Catga 当前的核心方向是：

- `CQRS`
- `Event Sourcing`
- `Flow DSL`
- `Native AOT`
- `Redis / RabbitMQ / NATS` 生产接入

## 快速开始

### 1. 定义消息

```csharp
using Catga;

public record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;
public record OrderCreated(string OrderId, decimal Amount) : IEvent;
public record OrderResult(string OrderId, bool Success);
```

### 2. 定义处理器

```csharp
public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    public ValueTask<CatgaResult<OrderResult>> HandleAsync(CreateOrder request, CancellationToken ct)
    {
        var result = new OrderResult(request.OrderId, Success: true);
        return ValueTask.FromResult(CatgaResult<OrderResult>.Success(result));
    }
}

public sealed class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    public ValueTask HandleAsync(OrderCreated @event, CancellationToken ct)
    {
        return ValueTask.CompletedTask;
    }
}
```

### 3. 注册服务

开发 / 测试最小接法：

```csharp
using Catga.DependencyInjection;

var catga = services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

services.AddInMemoryTransport();
```

生产默认路径：

```csharp
using Catga.DependencyInjection;

var catga = services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redisConnectionString)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

services.AddRedisTransport(redisConnectionString);
```

## 关键规则

### 1. 一定要先有 serializer

`AddCatga()` 之后必须接上 serializer。当前默认推荐：

```csharp
services.AddCatga().UseMemoryPack();
```

### 2. transport 和 persistence 是两层

transport 负责消息收发：

- `AddInMemoryTransport()`
- `AddRedisTransport(...)`
- `AddNatsTransport(...)`
- `AddRabbitMqTransport(...)`

persistence 负责状态和可靠性存储：

- `UseInMemory()`
- `UseRedis(...)`
- `UseNats(...)`

### 3. 生产里通常要补 hosting 和 health checks

```csharp
services.AddHealthChecks()
    .AddCatgaHealthChecks();
```

## 常见生产路径

- `Redis + Redis`：默认生产答案
- `RabbitMQ + Redis`：企业标准 broker 路径
- `NATS + NATS`：一体化 broker 路径

## 可选能力

常见能力扩展：

- `UseRequestClient()`
- `WithCorrelationPropagation()`
- `WithMessageVersioning(...)`
- `WithAuthorization(...)`
- `AddFlows()`

示例：

```csharp
var catga = services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redisConnectionString)
    .UseRequestClient()
    .WithCorrelationPropagation()
    .AddHostedServices();
```

## 文档入口

- 配置入口：`docs/articles/configuration.md`
- broker 选型：`docs/deployment/broker-production-overview.md`
- 托管服务：`docs/guides/hosting-configuration.md`
- 性能基准：`docs/BENCHMARK-RESULTS.md`
