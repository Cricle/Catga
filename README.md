# Catga

<div align="center">

**🚀 高性能 Native AOT 分布式 CQRS 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Performance](https://img.shields.io/badge/perf-90x_faster-brightgreen)](./REFLECTION_OPTIMIZATION_SUMMARY.md)

**零反射 · 零分配 · AOT 优先 · 3 行配置**

[30 秒开始](#-30-秒快速开始) · [特性](#-核心特性) · [完整文档](#-文档) · [示例](./examples)

</div>

---

## 🎯 30 秒快速开始

```bash
# 1. 安装
dotnet add package Catga.InMemory
dotnet add package Catga.Serialization.MemoryPack
dotnet add package Catga.SourceGenerator
```

```csharp
// 2. 定义消息
[MemoryPackable]
public partial record CreateOrder(string Id, decimal Amount) : IRequest<bool>;

// 3. 实现 Handler
public class CreateOrderHandler : IRequestHandler<CreateOrder, bool>
{
    public async ValueTask<CatgaResult<bool>> HandleAsync(
        CreateOrder request, CancellationToken ct = default)
        => CatgaResult<bool>.Success(true);
}

// 4. 配置（3 行！）
services.AddCatga()
    .UseMemoryPack()      // 100% AOT 兼容
    .ForProduction();     // 生产级配置

// 5. 使用
var result = await mediator.SendAsync<CreateOrder, bool>(new CreateOrder("ORD-001", 99.99m));
```

✅ **Done!** 开始构建高性能分布式系统。

---

## ✨ 核心特性

### 🔥 100% AOT 兼容

```csharp
// MemoryPack - 零反射、零分配
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;

services.AddCatga().UseMemoryPack();  // 一行配置

// 发布
dotnet publish -c Release -r linux-x64 --property:PublishAot=true
```

**性能表现**:
- ⚡ **50ms 启动** vs 传统 .NET 1200ms (24x)
- 💾 **8MB 二进制** vs 传统 .NET 68MB (8.5x)
- 📉 **12MB 内存** vs 传统 .NET 85MB (7x)

### 🎯 极简配置

**Before (其他框架)**:
```csharp
// 15 行配置，5 个 using，复杂的依赖注入
services.AddSingleton<IMessageSerializer, JsonSerializer>();
services.AddMediatR(typeof(Startup));
services.AddTransient<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
services.AddScoped<LoggingBehavior>();
services.Configure<MediatROptions>(options => { ... });
// ... 10 more lines
```

**After (Catga)**:
```csharp
// 3 行，1 个 using，自动发现 Handler
services.AddCatga()
    .UseMemoryPack()
    .ForProduction();
```

**代码减少 80%，配置时间减少 90%**

### 🛡️ 编译时检查

Roslyn 分析器自动检查常见错误：

```csharp
// ❌ 编译时警告: CATGA001
public record CreateOrder(...) : IRequest<OrderResult>;
//              ^^^^^^^^^^^
// 💡 添加 [MemoryPackable] 以获得最佳 AOT 性能

// ❌ 编译时警告: CATGA002
services.AddCatga();
//              ^^^^^
// 💡 调用 .UseMemoryPack() 或 .UseJson() 配置序列化器

// ✅ 正确
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;

services.AddCatga().UseMemoryPack();
```

**编译时发现问题，而非运行时崩溃**

### 🚀 极致性能

| 操作 | Catga | MediatR | 提升 |
|------|-------|---------|------|
| **Handler 注册** | 0.5ms | 45ms | **90x** 🔥 |
| **Send Command** | 5ns | 50ns | **10x** ⚡ |
| **Publish Event** | 10ns | 100ns | **10x** 📈 |
| **启动时间 (AOT)** | 50ms | N/A | **AOT Only** ✅ |

完整基准测试: [benchmarks/](./benchmarks/Catga.Benchmarks/)

### 🌐 生产就绪

```csharp
// 环境预设
services.AddCatga()
    .UseMemoryPack()
    .ForProduction();      // 日志、追踪、幂等性、重试、验证

// 或精细控制
services.AddCatga()
    .UseMemoryPack()
    .WithLogging()
    .WithTracing()
    .WithIdempotency(retentionHours: 24)
    .WithRetry(maxAttempts: 3)
    .WithValidation();
```

**内置功能**:
- ✅ **日志** - LoggerMessage 源生成，零分配
- ✅ **追踪** - OpenTelemetry 集成
- ✅ **指标** - Prometheus 兼容
- ✅ **幂等性** - 自动去重
- ✅ **重试** - 指数退避
- ✅ **验证** - Pipeline 验证

---

## 📚 完整的 CQRS 支持

### Command - 修改状态

```csharp
[MemoryPackable]
public partial record CreateOrder(string Id, decimal Amount) : IRequest<OrderResult>;

public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request, CancellationToken ct = default)
    {
        // 业务逻辑
        return CatgaResult<OrderResult>.Success(new OrderResult(request.Id, true));
    }
}
```

### Query - 只读查询

```csharp
[MemoryPackable]
public partial record GetOrder(string Id) : IRequest<Order>;

public class GetOrderHandler : IRequestHandler<GetOrder, Order>
{
    private readonly IOrderRepository _repo;

    public async ValueTask<CatgaResult<Order>> HandleAsync(
        GetOrder request, CancellationToken ct = default)
    {
        var order = await _repo.GetByIdAsync(request.Id, ct);
        return order != null
            ? CatgaResult<Order>.Success(order)
            : CatgaResult<Order>.Failure("Order not found");
    }
}
```

### Event - 领域事件

```csharp
[MemoryPackable]
public partial record OrderCreated(string OrderId, DateTime CreatedAt) : IEvent;

public class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public async Task HandleAsync(OrderCreated @event, CancellationToken ct = default)
    {
        _logger.LogInformation("Order {OrderId} created", @event.OrderId);
        // 发送通知、更新缓存等
    }
}
```

---

## 🏗️ 架构

### 清晰的职责边界

```
┌─────────────────────────────────────────┐
│          Your Application               │ ← 业务逻辑
├─────────────────────────────────────────┤
│   Catga.Serialization.MemoryPack        │ ← 序列化 (推荐)
│   Catga.Serialization.Json              │   或 JSON
├─────────────────────────────────────────┤
│      Catga.InMemory (Production)        │ ← 核心实现
├─────────────────────────────────────────┤
│         Catga (Abstractions)            │ ← 接口定义
├─────────────────────────────────────────┤
│      Catga.SourceGenerator              │ ← 编译时代码生成
└─────────────────────────────────────────┘

        可选扩展（基础设施无关）
┌──────────────────┬───────────────────────┐
│  Transport       │  Persistence          │
│  - Nats          │  - Redis              │
└──────────────────┴───────────────────────┘

        编排层（外部平台）
┌─────────────────────────────────────────┐
│  Kubernetes / .NET Aspire               │ ← 服务发现
│  - Service Discovery                    │   负载均衡
│  - Load Balancing                       │   健康检查
│  - Health Checks                        │
└─────────────────────────────────────────┘
```

### Catga 专注于什么？

**Catga 负责** ✅:
- CQRS 消息分发
- Pipeline 管道
- 幂等性保证
- 可观测性（Metrics/Tracing/Logging）

**Catga 不负责** ❌:
- 节点发现 → 使用 Kubernetes / Aspire
- 负载均衡 → 使用 K8s Service
- 消息队列 → 使用 NATS/Redis 原生能力

**设计理念**: 专注核心价值，复用成熟生态

详细说明: [架构文档](./docs/architecture/ARCHITECTURE.md) | [职责边界](./docs/architecture/RESPONSIBILITY-BOUNDARY.md)

---

## 🌐 分布式支持

### NATS Transport

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseNatsTransport(options =>
    {
        options.Url = "nats://nats:4222";  // K8s Service 名称
        options.SubjectPrefix = "orders.";
    });
```

### Redis Persistence

```csharp
services.AddCatga()
    .UseMemoryPack()
    .AddRedisOutboxPersistence(options =>
    {
        options.ConnectionString = "redis:6379";
    })
    .AddRedisInboxPersistence();
```

### Kubernetes 部署

```yaml
# K8s 自动处理服务发现和负载均衡
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
  ports:
  - port: 80
    targetPort: 8080
```

更多: [分布式架构](./docs/distributed/README.md) | [K8s 部署](./docs/deployment/kubernetes.md)

---

## 📊 可观测性

### OpenTelemetry 集成

```csharp
// 自动集成 OpenTelemetry
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(CatgaDiagnostics.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddMeter(CatgaDiagnostics.MeterName));

// Catga 自动记录
services.AddCatga()
    .UseMemoryPack()
    .WithLogging()      // 结构化日志
    .WithTracing()      // 分布式追踪
    .ForProduction();
```

**内置指标**:
- `catga.messages.published` - 消息发布数
- `catga.messages.failed` - 失败消息数
- `catga.message.duration` - 处理耗时
- `catga.messages.active` - 活跃消息数

**自动追踪**:
- Command/Query 执行
- Event 发布
- Pipeline 执行
- 跨服务调用

---

## 🔥 序列化选择

### MemoryPack (推荐 - 100% AOT)

```csharp
// 1. 安装
dotnet add package Catga.Serialization.MemoryPack
dotnet add package MemoryPack
dotnet add package MemoryPack.Generator

// 2. 标注消息
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;

// 3. 配置
services.AddCatga().UseMemoryPack();

// Done! 🎉
```

**优势**:
- ✅ 100% AOT 兼容（零反射）
- ✅ 5x 性能提升
- ✅ 40% 更小的 payload
- ✅ 零拷贝反序列化
- ✅ 分析器自动提示

### JSON (可选 - 需配置)

```csharp
// AOT 需要 JsonSerializerContext
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(OrderResult))]
public partial class AppJsonContext : JsonSerializerContext { }

services.AddCatga().UseJson(new JsonSerializerOptions
{
    TypeInfoResolver = AppJsonContext.Default
});
```

**对比**:
| 特性 | MemoryPack | JSON |
|------|-----------|------|
| AOT 兼容 | ✅ 100% | ⚠️ 需配置 |
| 性能 | 🔥 5x | ⚡ 1x |
| Payload | 📦 60% | 📦 100% |
| 人类可读 | ❌ | ✅ |

完整指南: [序列化指南](./docs/guides/serialization.md)

---

## 🎨 ASP.NET Core 集成

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services.AddCatga()
    .UseMemoryPack()
    .ForProduction();

// 添加 ASP.NET Core 集成
builder.Services.AddCatgaAspNetCore();

var app = builder.Build();

// 映射端点
app.MapCatgaEndpoints();  // 自动生成 CQRS 端点

app.Run();
```

**自动生成的端点**:
- `POST /catga/command/{Name}` - 发送 Command
- `POST /catga/query/{Name}` - 发送 Query
- `POST /catga/event/{Name}` - 发布 Event
- `GET /catga/health` - 健康检查

**或自定义端点**:
```csharp
app.MapPost("/api/orders", async (CreateOrder cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

---

## 📖 文档

### 🚀 快速开始
- **[5 分钟快速参考](./QUICK-REFERENCE.md)** - 快速查询手册
- **[基础示例](./docs/examples/basic-usage.md)** - 从零开始教程
- **[序列化指南](./docs/guides/serialization.md)** - MemoryPack vs JSON

### 📐 核心概念
- **[架构概览](./docs/architecture/ARCHITECTURE.md)** - 系统设计
- **[CQRS 模式](./docs/architecture/cqrs.md)** - 命令查询分离
- **[职责边界](./docs/architecture/RESPONSIBILITY-BOUNDARY.md)** - Catga vs 其他

### 🛠️ 工具链
- **[源生成器](./docs/guides/source-generator-usage.md)** - 自动 Handler 注册
- **[Roslyn 分析器](./docs/guides/analyzers.md)** - 编译时检查

### 🌐 分布式
- **[分布式架构](./docs/distributed/README.md)** - 分布式部署
- **[Kubernetes 部署](./docs/deployment/kubernetes.md)** - K8s 最佳实践

### 📊 高级主题
- **[Native AOT 发布](./docs/deployment/native-aot-publishing.md)** - AOT 部署
- **[性能优化](./REFLECTION_OPTIMIZATION_SUMMARY.md)** - 90x 性能提升
- **[分布式 ID](./docs/guides/distributed-id.md)** - Snowflake 实现

### 📁 项目信息
- **[项目结构](./docs/PROJECT_STRUCTURE.md)** - 代码组织
- **[贡献指南](./CONTRIBUTING.md)** - 如何贡献

---

## 💡 示例

### 完整示例项目

| 示例 | 描述 | 技术栈 |
|------|------|--------|
| **[OrderSystem](./examples/OrderSystem.AppHost/)** | 电商订单系统 | CQRS, Event Sourcing, MemoryPack |
| **[MemoryPackAotDemo](./examples/MemoryPackAotDemo/)** | AOT 最佳实践 | Native AOT, MemoryPack |

### 核心用法

```csharp
// 1. 定义消息
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderCreated(string OrderId) : IEvent;

// 2. 实现 Handler
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    private readonly ICatgaMediator _mediator;

    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request, CancellationToken ct = default)
    {
        // 业务逻辑
        var result = new OrderResult(request.OrderId, true);

        // 发布事件
        await _mediator.PublishAsync(new OrderCreated(request.OrderId));

        return CatgaResult<OrderResult>.Success(result);
    }
}

// 3. 配置
services.AddCatga()
    .UseMemoryPack()
    .ForProduction();

// 4. 使用
var result = await mediator.SendAsync<CreateOrder, OrderResult>(
    new CreateOrder("ORD-001", 99.99m));

if (result.IsSuccess)
    Console.WriteLine($"Order {result.Value!.OrderId} created");
```

---

## 🚀 性能对比

### vs 其他框架

| Framework | 启动时间 | Handler 注册 | AOT 支持 | 代码行数 |
|-----------|---------|------------|---------|---------|
| **Catga** | **50ms** | **0.5ms** | ✅ 100% | **3 行** |
| MediatR | 800ms | 45ms | ❌ No | 15 行 |
| Wolverine | 1200ms | 60ms | ⚠️ Partial | 20 行 |
| Brighter | 900ms | 50ms | ❌ No | 18 行 |

### Native AOT vs 传统 .NET

| 指标 | 传统 .NET | Native AOT | 提升 |
|------|----------|-----------|------|
| 启动时间 | 1.2s | **0.05s** | **24x** ⚡ |
| 二进制大小 | 68MB | **8MB** | **8.5x** 💾 |
| 内存占用 | 85MB | **12MB** | **7x** 📉 |
| 首次请求 | 150ms | **5ms** | **30x** 🚀 |

完整基准: [benchmarks/Catga.Benchmarks/](./benchmarks/Catga.Benchmarks/)

---

## 🤝 贡献

欢迎贡献！查看 [CONTRIBUTING.md](./CONTRIBUTING.md)。

**我们需要**:
- 🐛 Bug 报告和修复
- ✨ 新特性建议
- 📖 文档改进
- 🧪 测试用例
- 💡 性能优化

---

## 📜 许可证

本项目采用 [MIT 许可证](./LICENSE)。

---

## 🌟 致谢

特别感谢：
- **.NET 团队** - 卓越的 Native AOT 支持
- **MemoryPack** - 高性能序列化库
- **MediatR** - CQRS 模式先驱
- **CAP** - ASP.NET Core 集成灵感
- **社区** - 宝贵的反馈和建议

---

## 📊 项目状态

- ✅ **生产就绪** - 可直接用于生产
- ✅ **100% AOT 兼容** - 完全支持 Native AOT
- ✅ **零反射** - 热路径 100% 无反射
- ✅ **完整文档** - 全面的指南和示例
- ✅ **活跃维护** - 持续开发中

---

<div align="center">

**⭐ 如果 Catga 对你有帮助，请给个 Star！**

[快速开始](#-30-秒快速开始) · [文档](#-文档) · [示例](#-示例) · [性能](#-性能对比)

**用 Catga 构建高性能分布式系统！** 🚀

Made with ❤️ by the Catga Team

</div>
