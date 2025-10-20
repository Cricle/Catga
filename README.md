# Catga

<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

**⚡ 现代化、高性能的 .NET CQRS/Event Sourcing 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**零反射 · 源生成 · 可插拔 · 生产就绪**

[快速开始](#-快速开始) · [核心特性](#-核心特性) · [架构设计](#-架构设计) · [文档](https://cricle.github.io/Catga/) · [示例](./examples/)

</div>

---

## ✨ 核心特性

### 🚀 高性能

- **零反射**：所有代码生成均在编译时完成
- **零分配**：使用 `ArrayPool<T>` 和 `MemoryPool<T>` 优化内存
- **AOT 友好**：100% 支持 Native AOT 编译
- **高吞吐**：序列化 < 500 ns，DI解析 ~72 ns

### 🔌 可插拔架构

- **传输层可选**：InMemory / Redis / NATS
- **持久化层可选**：InMemory / Redis / NATS JetStream
- **序列化器可选**：JSON / MemoryPack / 自定义
- **独立演化**：每个组件独立发布，按需选择

### 🎯 开发体验

- **最小配置**：2 行代码启动
- **Source Generator**：自动注册 Handler，零配置
- **类型安全**：强类型消息定义
- **异常处理**：自动错误处理和回滚

### 🌐 分布式就绪

- **Outbox/Inbox 模式**：保证消息可靠性
- **Event Sourcing**：完整的事件溯源支持
- **分布式追踪**：内置 OpenTelemetry 集成
- **.NET Aspire**：原生云原生开发支持

---

## 📋 快速开始

### 1. 安装包

```bash
# 核心框架
dotnet add package Catga

# 选择传输层（开发推荐 InMemory，生产推荐 NATS/Redis）
dotnet add package Catga.Transport.InMemory

# 选择持久化层（可选）
dotnet add package Catga.Persistence.InMemory

# Source Generator（自动注册）
dotnet add package Catga.SourceGenerator
```

### 2. 配置服务

```csharp
using Catga;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 核心 + 传输层 + 持久化层
builder.Services
    .AddCatga()
    .AddInMemoryTransport()
    .AddInMemoryPersistence();

var app = builder.Build();
app.Run();
```

### 3. 定义消息

```csharp
using Catga;

// 命令（用于修改状态）
public record CreateOrderCommand(string ProductName, decimal Amount) 
    : IRequest<OrderResult>;

// 响应
public record OrderResult(Guid OrderId, DateTime CreatedAt);

// 事件（表示已发生的事实）
public record OrderCreatedEvent(Guid OrderId, string ProductName, decimal Amount) 
    : INotification;
```

### 4. 实现处理器

```csharp
using Catga;

// 命令处理器
public class CreateOrderCommandHandler 
    : IRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        ICatgaMediator mediator,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<OrderResult> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating order for {ProductName}", request.ProductName);

        // 业务逻辑
        var orderId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        // 发布事件
        await _mediator.PublishAsync(
            new OrderCreatedEvent(orderId, request.ProductName, request.Amount),
            cancellationToken);

        return new OrderResult(orderId, createdAt);
    }
}

// 事件处理器（可以有多个）
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Order {@OrderId} created", @event.OrderId);
        // 发送邮件、更新库存等...
    }
}
```

### 5. 使用 Mediator

```csharp
app.MapPost("/orders", async (
    CreateOrderCommand command,
    ICatgaMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command, ct);
    return Results.Ok(result);
});
```

**就这么简单！** 🎉

---

## 🏗️ 架构设计

### 分层架构

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                       │
│                (Your Business Logic)                         │
│        Commands, Queries, Events, Handlers                  │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                    Infrastructure Layer                      │
│                                                              │
│  Transport:                  Persistence:                   │
│  • InMemory (Dev/Test)       • InMemory (Dev/Test)          │
│  • Redis (Pub/Sub/Streams)   • Redis (Hash/ZSet)            │
│  • NATS (Core/JetStream)     • NATS (JetStream)             │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                        Core Library                          │
│                          (Catga)                             │
│                                                              │
│  Abstractions:                                              │
│  • ICatgaMediator        • IMessageTransport                │
│  • IRequest<T>           • IEventStore                      │
│  • INotification         • IOutboxStore / IInboxStore       │
│  • IMessageSerializer                                       │
└─────────────────────────────────────────────────────────────┘
```

### 核心概念

#### CQRS (Command Query Responsibility Segregation)

```csharp
// Command - 修改状态（一对一）
public record CreateOrderCommand(...) : IRequest<OrderResult>;
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderResult> { }

// Query - 读取数据（一对一）
public record GetOrderQuery(...) : IRequest<OrderDto>;
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto> { }

// Event - 已发生的事实（一对多）
public record OrderCreatedEvent(...) : INotification;
public class EmailHandler : IEventHandler<OrderCreatedEvent> { }
public class InventoryHandler : IEventHandler<OrderCreatedEvent> { }
```

#### 消息传输

```csharp
// 发送命令/查询（等待响应）
var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

// 发布事件（异步通知）
await mediator.PublishAsync(new OrderCreatedEvent(...));

// 批量发布事件
await mediator.PublishBatchAsync(events);
```

#### Outbox/Inbox 模式

**Outbox 模式**（保证消息至少发送一次）:
```
1. 业务逻辑 + Outbox 消息 → 同一事务
2. 后台轮询 Outbox → 发送到消息队列
3. 标记为已发送
```

**Inbox 模式**（保证消息至多处理一次）:
```
1. 接收消息 → 检查 Inbox（幂等性）
2. 如果已处理 → 跳过
3. 如果未处理 → 处理 + Inbox 记录 → 同一事务
```

---

## 📦 NuGet 包

### 核心包

| 包名 | 描述 | 版本 |
|------|------|------|
| [Catga](https://www.nuget.org/packages/Catga) | 核心框架（抽象接口） | ![NuGet](https://img.shields.io/nuget/v/Catga.svg) |
| [Catga.SourceGenerator](https://www.nuget.org/packages/Catga.SourceGenerator) | 源代码生成器 | ![NuGet](https://img.shields.io/nuget/v/Catga.SourceGenerator.svg) |
| [Catga.AspNetCore](https://www.nuget.org/packages/Catga.AspNetCore) | ASP.NET Core 集成 | ![NuGet](https://img.shields.io/nuget/v/Catga.AspNetCore.svg) |

### 传输层

| 包名 | 描述 | 特性 |
|------|------|------|
| [Catga.Transport.InMemory](https://www.nuget.org/packages/Catga.Transport.InMemory) | 内存传输 | 开发/测试 |
| [Catga.Transport.Redis](https://www.nuget.org/packages/Catga.Transport.Redis) | Redis 传输 | QoS 0 (Pub/Sub)<br>QoS 1 (Streams) |
| [Catga.Transport.Nats](https://www.nuget.org/packages/Catga.Transport.Nats) | NATS 传输 | Core / JetStream |

### 持久化层

| 包名 | 描述 | 特性 |
|------|------|------|
| [Catga.Persistence.InMemory](https://www.nuget.org/packages/Catga.Persistence.InMemory) | 内存持久化 | FusionCache |
| [Catga.Persistence.Redis](https://www.nuget.org/packages/Catga.Persistence.Redis) | Redis 持久化 | Hash / Sorted Set |
| [Catga.Persistence.Nats](https://www.nuget.org/packages/Catga.Persistence.Nats) | NATS 持久化 | JetStream Streams |

### 序列化层

| 包名 | 描述 | AOT |
|------|------|-----|
| [Catga.Serialization.Json](https://www.nuget.org/packages/Catga.Serialization.Json) | JSON 序列化 | ⚠️ 部分支持 |
| [Catga.Serialization.MemoryPack](https://www.nuget.org/packages/Catga.Serialization.MemoryPack) | MemoryPack 序列化 | ✅ 100% 支持 |

### 可选包

| 包名 | 描述 |
|------|------|
| [Catga.Hosting.Aspire](https://www.nuget.org/packages/Catga.Hosting.Aspire) | .NET Aspire 集成 |

---

## 🎯 配置示例

### 开发环境（内存实现）

```csharp
builder.Services
    .AddCatga()
    .AddInMemoryTransport()
    .AddInMemoryPersistence();
```

### 生产环境（Redis）

```csharp
builder.Services
    .AddCatga()
    .AddRedisTransport(options =>
    {
        options.Configuration = "localhost:6379";
        options.DefaultQoS = QoSLevel.QoS1; // 使用 Streams（可靠）
    })
    .AddRedisPersistence(options =>
    {
        options.Configuration = "localhost:6379";
    });
```

### 生产环境（NATS）

```csharp
builder.Services
    .AddCatga()
    .AddNatsTransport(options =>
    {
        options.Url = "nats://localhost:4222";
    })
    .AddNatsPersistence(options =>
    {
        options.Url = "nats://localhost:4222";
        options.StreamName = "CATGA_EVENTS";
    });
```

### 混合环境（Redis 传输 + NATS 持久化）

```csharp
builder.Services
    .AddCatga()
    .AddRedisTransport(options =>
    {
        options.Configuration = "localhost:6379";
    })
    .AddNatsPersistence(options =>
    {
        options.Url = "nats://localhost:4222";
    });
```

---

## 📊 性能基准

基于 BenchmarkDotNet 的真实测试结果 (AMD Ryzen 7 5800H, .NET 9.0):

| 操作 | 平均耗时 | 分配内存 | 比率 |
|------|---------|---------|------|
| **DI 自动注册** | 72.38 ns | 128 B | **比手动快 6%** ⚡ |
| **MemoryPack 序列化** | 267.2 ns | 1.13 KB | **1.0x (基准)** |
| **MemoryPack 反序列化** | 206.7 ns | 1.17 KB | **0.77x** |
| **MemoryPack 往返** | 584.9 ns | 2.3 KB | **1.82x** |
| **JSON 序列化 (池化)** | 666.7 ns | 1.63 KB | **2.5x** |
| **JSON 反序列化** | 1,061.0 ns | 1.17 KB | **3.97x** |
| **JSON 往返** | 1,926.5 ns | 2.8 KB | **7.21x** |

**性能亮点**：
- ⚡ **源生成器**: 自动注册性能优于手动注册 6%
- 🚀 **MemoryPack**: 比 JSON 快 2.5-4x，序列化 < 300ns
- 💾 **内存优化**: ArrayPool + MemoryPool，最小分配
- 🔥 **零开销**: DI 解析 < 80ns，纳秒级性能

完整报告: [性能基准文档](./docs/BENCHMARK-RESULTS.md)

---

## 📚 文档

### 快速入门

- [**Getting Started**](./docs/articles/getting-started.md) - 5 分钟快速上手
- [**架构设计**](./docs/articles/architecture.md) - 深入理解架构
- [**配置指南**](./docs/articles/configuration.md) - 完整配置选项
- [**AOT 部署**](./docs/articles/aot-deployment.md) - Native AOT 发布

### 核心概念

- [**CQRS 模式**](./docs/architecture/cqrs.md) - Command/Query 分离
- [**架构概览**](./docs/architecture/overview.md) - 系统架构设计
- [**职责边界**](./docs/architecture/RESPONSIBILITY-BOUNDARY.md) - 组件职责划分

### 使用指南

- [**序列化配置**](./docs/guides/serialization.md) - JSON/MemoryPack 配置
- [**Source Generator**](./docs/guides/source-generator.md) - 自动代码生成
- [**错误处理**](./docs/guides/custom-error-handling.md) - 异常处理最佳实践
- [**自动注册**](./docs/guides/auto-di-registration.md) - 依赖注入自动注册

### 可观测性

- [**OpenTelemetry 集成**](./docs/articles/opentelemetry-integration.md) - 分布式追踪
- [**分布式追踪指南**](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md) - 跨服务链路
- [**Jaeger 完整指南**](./docs/observability/JAEGER-COMPLETE-GUIDE.md) - 链路搜索技巧
- [**监控指南**](./docs/production/MONITORING-GUIDE.md) - Prometheus/Grafana

### 高级主题

- [**分布式事务**](./docs/patterns/DISTRIBUTED-TRANSACTION-V2.md) - Catga 事务模式
- [**AOT 序列化**](./docs/aot/serialization-aot-guide.md) - AOT 兼容序列化
- [**分布式部署**](./docs/distributed/README.md) - 分布式系统架构

### 部署

- [**Native AOT 发布**](./docs/deployment/native-aot-publishing.md) - AOT 编译发布
- [**Kubernetes 部署**](./docs/deployment/kubernetes.md) - K8s 部署指南
- [**Kubernetes 架构**](./docs/distributed/KUBERNETES.md) - K8s 架构设计

### API 参考

- [**Mediator API**](./docs/api/mediator.md) - ICatgaMediator 接口
- [**消息定义**](./docs/api/messages.md) - IRequest/INotification
- [**完整文档索引**](./docs/INDEX.md) - 所有文档列表

---

## 💡 示例项目

### MinimalApi - 最简示例

最简单的 Catga 应用，展示核心功能：

```bash
cd examples/MinimalApi
dotnet run
```

[查看代码](./examples/MinimalApi/) | [阅读文档](./examples/MinimalApi/README.md)

### OrderSystem - 完整订单系统

生产级电商订单系统，展示所有 Catga 功能：

**功能演示**:
- ✅ 订单创建成功流程
- ❌ 订单创建失败 + 自动回滚
- 📢 事件驱动（多个 Handler）
- 🔍 查询分离（Read Models）
- 🎯 自定义错误处理
- 📊 OpenTelemetry 追踪（Jaeger）
- 🚀 .NET Aspire 集成

**运行示例**:

```bash
cd examples/OrderSystem.AppHost
dotnet run

# 访问 UI
http://localhost:5000              # OrderSystem UI
http://localhost:16686             # Jaeger UI
http://localhost:18888             # Aspire Dashboard

# 测试 API
curl -X POST http://localhost:5000/demo/order-success
curl -X POST http://localhost:5000/demo/order-failure
```

[查看代码](./examples/OrderSystem.Api/) | [阅读文档](./examples/OrderSystem.Api/README.md)

---

## 🔍 为什么选择 Catga？

### vs MediatR

| 特性 | Catga | MediatR |
|------|-------|---------|
| **性能** | 72ns DI解析 | ~100ns+ |
| **AOT 支持** | ✅ 100% | ❌ 部分 |
| **分布式** | ✅ 内置 NATS/Redis | ❌ 需要扩展 |
| **Outbox/Inbox** | ✅ 内置 | ❌ 需要自己实现 |
| **Event Sourcing** | ✅ 完整支持 | ❌ 不支持 |
| **Source Generator** | ✅ 自动注册 (快6%) | ⚠️ 手动注册 |
| **内存优化** | ✅ ArrayPool+MemoryPool | ⚠️ 标准分配 |

### vs MassTransit

| 特性 | Catga | MassTransit |
|------|-------|-------------|
| **学习曲线** | ✅ 简单 | ⚠️ 复杂 |
| **配置复杂度** | ✅ 最小 | ⚠️ 较高 |
| **AOT 支持** | ✅ 100% | ❌ 不支持 |
| **内存占用** | ✅ 极低 | ⚠️ 较高 |
| **适用场景** | CQRS/ES | 企业服务总线 |

### vs CAP

| 特性 | Catga | CAP |
|------|-------|-----|
| **CQRS** | ✅ 原生支持 | ❌ 不支持 |
| **Event Sourcing** | ✅ 完整支持 | ❌ 不支持 |
| **AOT 支持** | ✅ 100% | ❌ 不支持 |
| **传输层** | NATS/Redis/InMemory | RabbitMQ/Kafka/等 |
| **适用场景** | CQRS/ES 应用 | 最终一致性事务 |

---

## 📚 文档

### 📖 入门指南
- [快速开始](./docs/articles/getting-started.md) - 5分钟入门教程
- [架构概览](./docs/architecture/overview.md) - 系统架构设计
- [配置指南](./docs/articles/configuration.md) - 详细配置说明

### 🎯 核心功能
- [CQRS 模式](./docs/architecture/cqrs.md) - CQRS 架构详解
- [分布式 ID](./docs/guides/distributed-id.md) - 高性能 ID 生成器
- [消息序列化](./docs/guides/serialization.md) - JSON/MemoryPack 对比
- [Source Generator](./docs/guides/source-generator.md) - 自动化代码生成

### 🚀 高级主题
- [内存优化](./docs/guides/memory-optimization-plan.md) - 零分配优化指南
- [AOT 部署](./docs/articles/aot-deployment.md) - Native AOT 最佳实践
- [分布式追踪](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md) - OpenTelemetry 集成
- [Kubernetes 部署](./docs/deployment/kubernetes.md) - K8s 部署指南

### 📊 性能与监控
- [性能报告](./docs/PERFORMANCE-REPORT.md) - 基准测试结果
- [Benchmark 结果](./docs/BENCHMARK-RESULTS.md) - 详细性能数据
- [监控指南](./docs/production/MONITORING-GUIDE.md) - 生产环境监控

### 🔧 开发者工具
- [Analyzers](./docs/guides/analyzers.md) - 代码分析器
- [自定义错误处理](./docs/guides/custom-error-handling.md) - 错误处理策略
- [AI 学习指南](./AI-LEARNING-GUIDE.md) - 给AI的使用说明

### 🌐 在线资源
- **官方文档**: [https://cricle.github.io/Catga/](https://cricle.github.io/Catga/)
- **API 参考**: [在线 API 文档](https://cricle.github.io/Catga/api.html)
- **示例代码**: [./examples/](./examples/)

---

## 🤝 贡献

欢迎贡献！我们欢迎：

- 🐛 Bug 报告
- ✨ 功能请求
- 📖 文档改进
- 💻 代码贡献

请查看 [CONTRIBUTING.md](./CONTRIBUTING.md) 了解详情。

---

## 📝 更新日志

查看 [CHANGELOG.md](./docs/CHANGELOG.md) 了解每个版本的变更。

---

## 🙏 致谢

Catga 受以下优秀项目启发：

- [**MediatR**](https://github.com/jbogard/MediatR) - CQRS 模式实现
- [**MassTransit**](https://github.com/MassTransit/MassTransit) - 分布式消息模式
- [**MemoryPack**](https://github.com/Cysharp/MemoryPack) - 高性能序列化
- [**NATS**](https://nats.io/) - 云原生消息系统
- [**OpenTelemetry**](https://opentelemetry.io/) - 可观测性标准

---

## 📄 许可证

本项目采用 [MIT License](./LICENSE) 开源。

---

## 🌟 Star History

如果这个项目对你有帮助，请给我们一个 Star！⭐

[![Star History Chart](https://api.star-history.com/svg?repos=Cricle/Catga&type=Date)](https://star-history.com/#Cricle/Catga&Date)

---

## 📞 联系我们

- **GitHub Issues**: [提交问题](https://github.com/Cricle/Catga/issues)
- **GitHub Discussions**: [参与讨论](https://github.com/Cricle/Catga/discussions)
- **官方文档**: [https://cricle.github.io/Catga/](https://cricle.github.io/Catga/)

---

<div align="center">

**Made with ❤️ by Catga Contributors**

[⬆ 回到顶部](#catga)

</div>
