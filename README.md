# 🎯 Catga

> **高性能、生产就绪的 .NET 分布式 CQRS 框架**

[![.NET 9+](https://img.shields.io/badge/.NET-9%2B-512BD4)](https://dotnet.microsoft.com)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-success)](https://learn.microsoft.com/dotnet/core/deploying/native-aot)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

---

## ✨ 特性

- 🎯 **CQRS + Mediator** - 命令查询职责分离
- 🌐 **分布式消息** - NATS 集成，云原生
- 🔄 **可靠消息** - Outbox/Inbox 模式
- 🎭 **Saga 事务** - 分布式事务协调
- 🛡️ **弹性设计** - 熔断、重试、限流
- ⚡ **高性能** - NativeAOT、零分配
- 📦 **模块化** - 按需引入功能

---

## 🚀 快速开始

### 安装

```bash
dotnet add package Catga
dotnet add package Catga.Nats    # 分布式消息（可选）
dotnet add package Catga.Redis   # Redis 存储（可选）
```

### 最简示例（30秒）

```csharp
// 1. 定义消息
public record CreateOrderCommand(string OrderId, decimal Amount) : ICommand;

public record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent;

// 2. 定义处理器
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    public async Task<Result> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // 创建订单逻辑
        Console.WriteLine($"订单 {command.OrderId} 已创建，金额: {command.Amount}");
        return Result.Success();
    }
}

// 3. 配置和使用
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga();  // 添加 Catga

var app = builder.Build();

// 发送命令
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.Run();
```

### 分布式微服务（5分钟）

```csharp
// OrderService - 订单服务
builder.Services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")       // NATS 消息传输
    .AddRedisCatgaStore("localhost:6379")        // Redis 状态存储
    .AddRedisOutbox()                             // 可靠消息发送
    .AddRedisInbox();                             // 幂等消息处理

// PaymentService - 支付服务
builder.Services.AddCatga()
    .AddNatsCatga("nats://localhost:4222");

// 跨服务调用 - 自动路由
var result = await mediator.SendAsync(new ProcessPaymentCommand(...));
```

---

## 📦 架构分层

Catga 采用**渐进增强**的架构，从核心到高级逐步引入功能。

### 🎯 核心层（必需）⭐⭐⭐⭐⭐

| 包 | 功能 | 使用场景 |
|---|------|---------|
| **Catga** | CQRS 核心 + Mediator | 所有项目 |

```csharp
services.AddCatga();  // 单体应用
```

### 🌐 分布式层（推荐）⭐⭐⭐⭐⭐

| 包 | 功能 | 使用场景 |
|---|------|---------|
| **Catga.Nats** | NATS 消息传输 | 微服务通信 |
| **Catga.Redis** | Redis 存储 | 状态持久化 |

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379");
```

### 🔄 可靠性层（推荐）⭐⭐⭐⭐⭐

| 功能 | 说明 | 使用场景 |
|-----|------|---------|
| **Outbox/Inbox** | 可靠消息投递 | 关键业务 |
| **Saga** | 分布式事务 | 跨服务流程 |
| **弹性设计** | 熔断、重试 | 生产环境 |

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisOutbox()     // 可靠消息
    .AddRedisInbox()      // 幂等处理
    .AddPipelineBehavior<CircuitBreakerBehavior>()  // 熔断
    .AddPipelineBehavior<RetryBehavior>();          // 重试
```

### 🔍 高级层（可选）⭐⭐⭐

| 功能 | 说明 | 使用场景 |
|-----|------|---------|
| **服务发现** | 动态服务发现 | 大规模微服务 |
| **流处理** | 实时流处理 | 数据管道 |

```csharp
// 服务发现（5种实现）
services.AddKubernetesServiceDiscovery();  // Kubernetes
services.AddConsulServiceDiscovery("http://consul:8500");  // Consul

// 流处理
var pipeline = StreamProcessor.From(eventStream)
    .Where(e => e.Type == "Order")
    .Batch(100)
    .Do(batch => ProcessBatch(batch));
```

### 🧪 实验性层（谨慎使用）⚠️

| 功能 | 状态 | 说明 |
|-----|------|------|
| **配置中心** | 🚧 实验性 | API 可能变化 |
| **事件溯源** | 🚧 实验性 | 功能不完整 |

---

## 📚 核心概念

### 1. CQRS

```csharp
// Command - 写操作
public record CreateOrderCommand(string OrderId, decimal Amount) : ICommand;

// Query - 读操作
public record GetOrderQuery(string OrderId) : IQuery<OrderDto>;

// Event - 领域事件
public record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent;
```

### 2. Pipeline Behaviors

```csharp
// 日志拦截器
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("处理: {Request}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("完成: {Request}", typeof(TRequest).Name);
        return response;
    }
}

services.AddPipelineBehavior<LoggingBehavior<,>>();
```

### 3. Saga 分布式事务

```csharp
var saga = new OrderSaga();
saga.AddStep<CreateOrderCommand, OrderCreatedEvent>()
    .Compensate<CancelOrderCommand>()
    .WithRetry(3)
    .WithTimeout(TimeSpan.FromMinutes(5));

saga.AddStep<ProcessPaymentCommand, PaymentProcessedEvent>()
    .Compensate<RefundPaymentCommand>();

await saga.ExecuteAsync(new CreateOrderCommand(...));
```

### 4. Outbox/Inbox 模式

```csharp
// 自动启用 Outbox 和 Inbox
services.AddCatga()
    .AddRedisOutbox()   // 消息不会丢失
    .AddRedisInbox();   // 自动去重

// 后台自动处理
// - Outbox: 定期发送未发送的消息
// - Inbox: 拒绝重复消息
```

---

## 🎯 使用场景

### ✅ 适合 Catga 的场景

- **微服务架构** - 服务间通信
- **CQRS 模式** - 命令查询分离
- **事件驱动** - 事件发布订阅
- **分布式事务** - Saga 编排
- **高性能要求** - NativeAOT、零分配

### ⚠️ 不太适合的场景

- **单体 CRUD** - 可能过度设计
- **简单应用** - 学习成本
- **非 .NET** - 仅支持 .NET 9+

---

## 📖 文档

| 文档 | 说明 |
|-----|------|
| [架构指南](ARCHITECTURE.md) | 完整的架构分层说明 |
| [快速参考](QUICK_REFERENCE.md) | 常用 API 速查 |
| [AOT 优化](AOT_FINAL_REPORT.md) | NativeAOT 兼容性 |
| [服务发现](docs/service-discovery/README.md) | 服务发现文档 |
| [流处理](docs/streaming/README.md) | 流处理文档 |
| [事件溯源](docs/patterns/event-sourcing.md) | 事件溯源（实验性） |

---

## 🎊 示例项目

| 示例 | 说明 | 复杂度 |
|-----|------|--------|
| [BasicExample](examples/BasicExample/) | CQRS 基础 | ⭐ |
| [NatsDistributed](examples/NatsDistributed/) | 分布式微服务 | ⭐⭐⭐ |
| [SagaDemo](examples/SagaDemo/) | Saga 分布式事务 | ⭐⭐⭐⭐ |
| [ServiceDiscoveryDemo](examples/ServiceDiscoveryDemo/) | 服务发现 | ⭐⭐⭐ |
| [StreamingDemo](examples/StreamingDemo/) | 流处理 | ⭐⭐⭐ |
| [AotDemo](examples/AotDemo/) | NativeAOT | ⭐⭐ |

---

## 🎯 推荐配置

### 单体应用

```csharp
services.AddCatga();
```

**复杂度**: ⭐⭐
**学习时间**: 30 分钟

---

### 微服务（基础）

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379");
```

**复杂度**: ⭐⭐⭐
**学习时间**: 1 小时

---

### 微服务（生产级）⭐ 推荐

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379")
    .AddRedisOutbox()
    .AddRedisInbox()
    .AddPipelineBehavior<CircuitBreakerBehavior>()
    .AddPipelineBehavior<RetryBehavior>();
```

**复杂度**: ⭐⭐⭐⭐
**学习时间**: 2-3 小时

---

## 🚀 性能

- ⚡ **NativeAOT** - 极速启动（< 100ms）
- 🧠 **低内存** - 零分配设计
- 📦 **小体积** - 编译后 < 20MB
- 🔥 **高吞吐** - 10万+ 消息/秒

---

## 🤝 贡献

欢迎贡献！请查看 [CONTRIBUTING.md](CONTRIBUTING.md)

---

## 📄 许可

MIT License - 详见 [LICENSE](LICENSE)

---

## 🙏 致谢

- [NATS](https://nats.io/) - 云原生消息系统
- [Redis](https://redis.io/) - 高性能存储
- [MediatR](https://github.com/jbogard/MediatR) - Mediator 模式灵感

---

## 📊 功能矩阵

| 功能 | 状态 | 推荐度 | 复杂度 |
|-----|------|--------|--------|
| CQRS 核心 | ✅ 稳定 | ⭐⭐⭐⭐⭐ | ⭐⭐ |
| NATS 传输 | ✅ 稳定 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| Redis 存储 | ✅ 稳定 | ⭐⭐⭐⭐⭐ | ⭐⭐ |
| Outbox/Inbox | ✅ 稳定 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| Saga 事务 | ✅ 稳定 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| 弹性设计 | ✅ 稳定 | ⭐⭐⭐⭐⭐ | ⭐⭐ |
| 服务发现 | ✅ 稳定 | ⭐⭐⭐ | ⭐⭐⭐⭐ |
| 流处理 | ✅ 稳定 | ⭐⭐⭐ | ⭐⭐⭐ |
| 配置中心 | 🚧 实验 | ⭐ | ⭐⭐⭐ |
| 事件溯源 | 🚧 实验 | ⭐ | ⭐⭐⭐⭐⭐ |

---

**开始使用 Catga，构建高性能分布式系统！** 🚀
