# Catga

<div align="center">

**🚀 高性能、100% AOT 兼容的 .NET 9 CQRS 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**零反射 · 源生成器 · 完全抽象 · 生产就绪**

[快速开始](#-快速开始) · [核心特性](#-核心特性) · [文档](./docs/articles/getting-started.md) · [示例](./examples/MinimalApi/)

</div>

---

## 📖 简介

Catga 是专为 .NET 9 和 Native AOT 设计的现代化 CQRS 框架，通过**可插拔架构**和**源生成器**实现极致性能和灵活性。

### 🎯 核心价值

- ⚡ **极致性能** - 零反射、ArrayPool 优化、Span<T> 零拷贝
- 🔥 **100% AOT 兼容** - 所有组件支持 Native AOT 编译
- 🔌 **完全可插拔** - 传输层、持久化层、序列化层均可替换
- 🌐 **分布式就绪** - 支持 NATS、Redis 传输与持久化
- 🎨 **最小配置** - 2 行代码启动，自动依赖注入
- 🔍 **完整可观测** - OpenTelemetry + Jaeger 原生集成

### 🌟 创新特性

1. **可插拔架构** - 传输、持久化、序列化完全抽象，随时切换实现
2. **Source Generator** - 零反射，编译时代码生成，AOT 优先
3. **SafeRequestHandler** - 零 try-catch，自动错误处理和回滚
4. **ArrayPool 优化** - 统一的内存管理和编码/解码优化
5. **.NET Aspire 集成** - 原生支持云原生开发

---

## 🏗️ 架构设计

### 可插拔分层架构

```
┌─────────────────────────────────────┐
│         Application Layer            │  你的业务代码
│    (Commands, Events, Handlers)      │
└─────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────┐
│       Serialization Layer            │  可选择
│  • Catga.Serialization.Json          │  ├─ JSON (AOT 部分支持)
│  • Catga.Serialization.MemoryPack    │  └─ MemoryPack (100% AOT)
└─────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────┐
│      Infrastructure Layer            │  可选择
│  传输层:                              │
│  • Catga.Transport.InMemory          │  ├─ 内存（开发/测试）
│  • Catga.Transport.Nats              │  ├─ NATS (Pub/Sub + JetStream)
│  • Catga.Transport.Redis             │  └─ Redis (Pub/Sub + Streams)
│                                      │
│  持久化层:                            │
│  • Catga.Persistence.InMemory        │  ├─ 内存（开发/测试）
│  • Catga.Persistence.Nats            │  ├─ NATS (JetStream Streams)
│  • Catga.Persistence.Redis           │  └─ Redis (优化 Outbox/Inbox)
└─────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────┐
│           Core Library               │  抽象接口
│              Catga                   │  • IMessageSerializer
│                                      │  • IMessageTransport
│                                      │  • IEventStore
│                                      │  • IOutboxStore / IInboxStore
└─────────────────────────────────────┘
```

### 核心设计原则

1. **依赖倒置** - 依赖抽象而非具体实现
2. **单一职责** - 每个库只负责一个领域
3. **开放封闭** - 对扩展开放，对修改封闭
4. **接口隔离** - 最小化接口依赖

---

## 🚀 快速开始

### 1. 安装核心包

```bash
# 核心框架
dotnet add package Catga

# 选择序列化器（二选一）
dotnet add package Catga.Serialization.Json           # JSON (兼容性好)
dotnet add package Catga.Serialization.MemoryPack     # MemoryPack (100% AOT)

# 选择传输层（开发环境推荐内存，生产环境推荐 NATS/Redis）
dotnet add package Catga.Transport.InMemory           # 内存传输
# dotnet add package Catga.Transport.Nats             # NATS 传输
# dotnet add package Catga.Transport.Redis            # Redis 传输

# 选择持久化层（可选）
dotnet add package Catga.Persistence.InMemory         # 内存持久化
# dotnet add package Catga.Persistence.Nats           # NATS 持久化
# dotnet add package Catga.Persistence.Redis          # Redis 持久化

# Source Generator（自动注册）
dotnet add package Catga.SourceGenerator

# ASP.NET Core 集成（可选）
dotnet add package Catga.AspNetCore
```

### 2. 定义消息

```csharp
using Catga.Messages;
using MemoryPack;

// 使用 MemoryPack 实现 AOT 友好序列化
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(string OrderId, DateTime CreatedAt);

[MemoryPackable]
public partial record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent;
```

### 3. 实现 Handler

```csharp
using Catga.Handlers;

// 命令 Handler - 使用 SafeRequestHandler 自动处理异常
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) : base(logger)
    {
        _logger = logger;
    }

    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request,
        CancellationToken ct)
    {
        // 业务逻辑，框架自动捕获异常并转换为 CatgaResult
        if (request.Amount <= 0)
            throw new CatgaException("Amount must be positive");

        _logger.LogInformation("Creating order {OrderId}", request.OrderId);

        // 保存订单...
        await Task.Delay(10, ct); // 模拟数据库操作

        return new OrderResult(request.OrderId, DateTime.UtcNow);
    }
}

// 事件 Handler - 处理 OrderCreatedEvent
public class SendEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<SendEmailHandler> _logger;

    public SendEmailHandler(ILogger<SendEmailHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        _logger.LogInformation("Sending email for order {@event.OrderId}");
        await Task.Delay(5, ct); // 模拟发送邮件
    }
}
```

### 4. 配置服务

```csharp
using Catga;
using Catga.Serialization.MemoryPack;

var builder = WebApplication.CreateBuilder(args);

// 1. 添加 Catga 核心
builder.Services.AddCatga();

// 2. 选择序列化器
builder.Services.AddMessageSerializer<MemoryPackMessageSerializer>();

// 3. 选择传输层
builder.Services.AddInMemoryTransport();

// 4. 选择持久化层（可选）
builder.Services.AddInMemoryPersistence();

// 5. 自动注册所有 Handler（Source Generator）
builder.Services.AddGeneratedHandlers();

var app = builder.Build();

// 6. 使用 Catga
app.MapPost("/orders", async (CreateOrder cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(cmd);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error);
});

app.Run();
```

**就这么简单！** 无需手动注册 Handler，无需 try-catch，框架自动处理一切。

---

## 🎯 核心特性

### 1. 可插拔架构 - 随时切换实现

#### 开发环境（内存实现）

```csharp
services.AddCatga()
    .AddMessageSerializer<JsonMessageSerializer>()
    .AddInMemoryTransport()
    .AddInMemoryPersistence();
```

#### 生产环境（NATS + Redis）

```csharp
services.AddCatga()
    .AddMessageSerializer<MemoryPackMessageSerializer>()  // 100% AOT
    .AddNatsTransport(options =>
{
    options.Url = "nats://localhost:4222";
    })
    .AddNatsPersistence()
    .AddRedisPersistence(options =>
    {
        options.Configuration = "localhost:6379";
});
```

**只需修改配置，无需改动业务代码！**

### 2. 序列化抽象 - AOT 友好

所有传输和持久化组件都使用 `IMessageSerializer` 抽象：

```csharp
public interface IMessageSerializer
{
    byte[] Serialize<T>(T obj);
    T? Deserialize<T>(byte[] data);
}
```

**实现**：
- ✅ `JsonMessageSerializer` - 使用 `System.Text.Json`（部分 AOT 支持）
- ✅ `MemoryPackMessageSerializer` - 使用 `MemoryPack`（100% AOT 支持）
- ✅ 自定义实现 - 实现 `IMessageSerializer` 接口即可

**切换序列化器**：

```csharp
// 开发环境：使用 JSON（可读性好）
services.AddMessageSerializer<JsonMessageSerializer>();

// 生产环境：使用 MemoryPack（性能最优）
services.AddMessageSerializer<MemoryPackMessageSerializer>();
```

### 3. SafeRequestHandler - 零异常处理

**传统方式**：充满 try-catch

```csharp
// ❌ 传统方式
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    try
    {
        var order = await _orderService.CreateAsync(request);
        return Ok(order);
    }
    catch (ValidationException ex)
    {
        _logger.LogWarning(ex, "Validation failed");
        return BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error");
        return StatusCode(500, "Internal error");
    }
}
```

**Catga 方式**：框架自动处理

```csharp
// ✅ Catga 方式
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request,
        CancellationToken ct)
    {
        // 直接抛出异常，框架自动转换为 CatgaResult.Failure
        if (request.Amount <= 0)
            throw new CatgaException("Amount must be positive");

        var order = await _repository.SaveAsync(...);
        return new OrderResult(order.Id, order.CreatedAt);
    }

    // 可选：自定义错误处理和自动回滚
    protected override async Task<CatgaResult<OrderResult>> OnBusinessErrorAsync(
        CreateOrder request,
        CatgaException exception,
        CancellationToken ct)
    {
        _logger.LogWarning("Order creation failed, rolling back...");

        // 自动回滚逻辑
        await RollbackChangesAsync();

        return CatgaResult<OrderResult>.Failure(
            $"Order creation failed: {exception.Message}. All changes rolled back.",
            exception);
    }
}
```

详见：[自定义错误处理指南](./docs/guides/custom-error-handling.md)

### 4. ArrayPool 优化 - 零分配

Catga 提供统一的 `ArrayPoolHelper` 工具类，用于所有编码/解码操作：

```csharp
// UTF8 编码/解码（零分配）
var bytes = ArrayPoolHelper.GetBytes("Hello");
var str = ArrayPoolHelper.GetString(bytes);

// Base64 编码/解码（零分配）
var base64 = ArrayPoolHelper.ToBase64String(bytes);
var decoded = ArrayPoolHelper.FromBase64String(base64);
```

**内部实现**：使用 `ArrayPool<byte>` 和 `Span<T>` 实现零拷贝操作。

### 5. Source Generator - 零配置

```csharp
// 自动注册所有 Handler
builder.Services.AddGeneratedHandlers();   // 发现所有 IRequestHandler, IEventHandler

// 自动注册所有服务
builder.Services.AddGeneratedServices();   // 发现所有 [CatgaService] 标记的服务
```

**生成的代码**在编译时创建，零运行时开销，100% AOT 兼容。

详见：[Source Generator 使用指南](./docs/guides/source-generator.md)

### 6. 事件驱动架构

```csharp
// 定义事件
[MemoryPackable]
public partial record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent;

// 多个 Handler 可以处理同一个事件
public class SendEmailHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        await _emailService.SendAsync($"Order {@event.OrderId} created");
    }
}

public class UpdateInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        await _inventory.ReserveAsync(@event.OrderId, ...);
    }
}

// 发布事件（自动调用所有 Handler）
await _mediator.PublishAsync(new OrderCreatedEvent(orderId, amount));
```

---

## 📦 NuGet 包

### 核心包

| 包名 | 用途 | AOT |
|------|------|-----|
| `Catga` | 核心框架（抽象接口） | ✅ |
| `Catga.SourceGenerator` | 源生成器 | ✅ |
| `Catga.AspNetCore` | ASP.NET Core 集成 | ✅ |

### 序列化层

| 包名 | 用途 | AOT |
|------|------|-----|
| `Catga.Serialization.Json` | JSON 序列化 | ⚠️ 部分 |
| `Catga.Serialization.MemoryPack` | MemoryPack 序列化 | ✅ 100% |

### 传输层

| 包名 | 用途 | AOT |
|------|------|-----|
| `Catga.Transport.InMemory` | 内存传输（开发/测试） | ✅ |
| `Catga.Transport.Nats` | NATS 传输 | ✅ |
| `Catga.Transport.Redis` | Redis 传输 | ✅ |

### 持久化层

| 包名 | 用途 | AOT |
|------|------|-----|
| `Catga.Persistence.InMemory` | 内存持久化（开发/测试） | ✅ |
| `Catga.Persistence.Nats` | NATS JetStream 持久化 | ✅ |
| `Catga.Persistence.Redis` | Redis 持久化 | ✅ |

---

## 🎨 完整示例

### OrderSystem - 订单系统

完整的电商订单系统，展示所有 Catga 功能：

**功能演示**：
- ✅ 订单创建成功流程
- ❌ 订单创建失败 + 自动回滚
- 📢 事件驱动（多个 Handler）
- 🔍 查询分离（Read Models）
- 🎯 自定义错误处理
- 📊 OpenTelemetry 追踪（Jaeger）

**运行示例**：

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

详见：[OrderSystem 文档](./examples/OrderSystem.Api/README.md)

---

## 📊 性能

基于 BenchmarkDotNet 的真实测试结果：

| 操作 | 平均耗时 | 分配内存 | 吞吐量 |
|------|---------|---------|--------|
| 命令处理 | 17.6 μs | 408 B | 56K QPS |
| 查询处理 | 16.1 μs | 408 B | 62K QPS |
| 事件发布 | 428 ns | 0 B | 2.3M QPS |
| MemoryPack 序列化 | 48 ns | 0 B | 20M/s |
| 分布式 ID 生成 | 485 ns | 0 B | 2M/s |

**关键优势**：
- ⚡ 命令处理 < 20μs
- 🔥 事件发布接近零分配
- 📦 MemoryPack 比 JSON 快 4-8x
- 🎯 并发场景线性扩展

完整报告：[性能基准文档](./docs/PERFORMANCE-REPORT.md)

---

## 📚 文档

### 快速入门
- **[Getting Started](./docs/articles/getting-started.md)** - 5 分钟快速上手
- **[Architecture](./docs/articles/architecture.md)** - 架构设计详解
- **[Configuration Guide](./docs/articles/configuration.md)** - 完整配置选项
- **[Native AOT Deployment](./docs/articles/aot-deployment.md)** - AOT 部署指南

### 核心概念
- [消息定义](./docs/api/messages.md) - IRequest, IEvent
- [Mediator API](./docs/api/mediator.md) - ICatgaMediator
- [完整文档索引](./docs/INDEX.md)

### 示例项目
- **[MinimalApi](./examples/MinimalApi/)** - 最简单的示例
- **[OrderSystem](./examples/OrderSystem.Api/)** - 完整的订单系统
- [序列化配置](./docs/guides/serialization.md) - IMessageSerializer

### 可观测性
- [分布式追踪指南](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md) - 跨服务链路
- [Jaeger 完整指南](./docs/observability/JAEGER-COMPLETE-GUIDE.md) - 搜索技巧
- [监控指南](./docs/production/MONITORING-GUIDE.md) - Prometheus/Grafana

### 高级功能
- [分布式事务](./docs/patterns/DISTRIBUTED-TRANSACTION-V2.md) - Catga Pattern
- [AOT 序列化指南](./docs/aot/serialization-aot-guide.md)
- [Source Generator](./docs/guides/source-generator.md)

### 部署
- [Native AOT 发布](./docs/deployment/native-aot-publishing.md)
- [Kubernetes 部署](./docs/deployment/kubernetes.md)

---

## 🤝 贡献

欢迎贡献！请查看 [CONTRIBUTING.md](./CONTRIBUTING.md)

---

## 📄 许可证

MIT License - 详见 [LICENSE](./LICENSE)

---

## 🙏 致谢

- [MediatR](https://github.com/jbogard/MediatR) - 灵感来源
- [MassTransit](https://github.com/MassTransit/MassTransit) - 分布式模式
- [MemoryPack](https://github.com/Cysharp/MemoryPack) - 序列化
- [NATS](https://nats.io/) - 消息传输
- [OpenTelemetry](https://opentelemetry.io/) - 可观测性

---

<div align="center">

**⭐ 如果这个项目对你有帮助，请给我们一个 Star！**

Made with ❤️ by Catga Contributors

</div>
