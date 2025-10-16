# Catga

<div align="center">

**🚀 高性能、100% AOT 兼容的分布式 CQRS 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-191%20passed-brightgreen)]()

**零反射 · 零分配 · 高性能 · 简单易用**

[Quick Start](#-快速开始) · [Features](#-core-features) · [Documentation](./docs/INDEX.md) · [Examples](./examples/OrderSystem.Api/)

</div>

---

## 📖 简介

Catga 是一个专为 .NET 9 和 Native AOT 设计的高性能 CQRS/中介者框架，提供：

- ⚡ **Ultimate Performance**: < 1μs command handling, zero-allocation design
- 🔥 **100% AOT Compatible**: MemoryPack serialization, Source Generator auto-registration
- 🛡️ **Compile-Time Safety**: Roslyn analyzers detect configuration errors
- 🌐 **Distributed Ready**: NATS, Redis transport & persistence
- 🎯 **Minimal Config**: 2 lines to start, auto-DI for everything
- 🔍 **Full Observability**: OpenTelemetry, Health Checks, .NET Aspire
- 🚀 **Production Ready**: Graceful shutdown, auto-recovery, graceful lifecycle
- ⏪ **Time-Travel Debugging**: 🌟 **业界首创** - 完整的流程回放和调试（零开销）

---

## 🌟 创新特性：Time-Travel Debugging

Catga 包含**业界首创**的 CQRS 时间旅行调试系统：

```csharp
// 1. 启用调试器（一行代码）
builder.Services.AddCatgaDebuggerWithAspNetCore();

// 2. 消息自动捕获（使用 Source Generator）
[MemoryPackable]
[GenerateDebugCapture]  // 自动生成 AOT 兼容的变量捕获
public partial record CreateOrderCommand(...) : IRequest<Result>;

// 3. 访问调试界面
// http://localhost:5000/debug - Vue 3 现代化 UI
```

**功能亮点**：
- ✅ **时间旅行回放** - 回到任意时刻，查看完整执行过程
- ✅ **宏观/微观视图** - 系统级 + 单流程级双重视角
- ✅ **零开销设计** - 生产环境 <0.01% 性能影响
- ✅ **AOT 兼容** - Source Generator 自动生成，无反射
- ✅ **Vue 3 UI** - 现代化、实时更新的调试界面
- ✅ **智能采样** - 自适应采样率，感知 CPU/内存

详见：[Debugger 文档](./docs/DEBUGGER.md) | [OrderSystem 示例](./examples/README-ORDERSYSTEM.md)

---

## 🚀 快速开始

### 安装

```bash
# 核心包 + 内存实现
dotnet add package Catga
dotnet add package Catga.InMemory

# 序列化 (100% AOT)
dotnet add package Catga.Serialization.MemoryPack

# Source Generator (自动注册)
dotnet add package Catga.SourceGenerator
```

### 30 Second Example

```csharp
// 1. Define messages (MemoryPack = AOT-friendly)
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderCreated>;

[MemoryPackable]
public partial record OrderCreated(string OrderId, DateTime CreatedAt);

// 2. Implement handler - NO try-catch needed!
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderCreated>
{
    protected override async Task<OrderCreated> HandleCoreAsync(CreateOrder request, CancellationToken ct)
    {
        // Just business logic!
        return new OrderCreated(request.OrderId, DateTime.UtcNow);
    }
}

// 3. Define service - auto-registered!
[CatgaService(Catga.ServiceLifetime.Singleton, ServiceType = typeof(IOrderRepo))]
public class OrderRepo : IOrderRepo { }

// 4. Configure (2 lines!)
builder.Services.AddCatga().UseMemoryPack().ForDevelopment();
builder.Services.AddGeneratedHandlers();  // Auto-register all handlers
builder.Services.AddGeneratedServices();  // Auto-register all services

// 5. Use
var result = await mediator.SendAsync<CreateOrder, OrderCreated>(new CreateOrder("ORD-001", 99.99m));
// Console auto-shows: [abc12345] CreateOrder ✅ (0.8ms)
```

**That's it!** 🎉

**Code reduction: 80%** vs traditional approach

---

## ✨ Core Features

### 🔥 Zero Configuration with Source Generators

```csharp
// 1. Implement service - auto-registered
[CatgaService(Catga.ServiceLifetime.Singleton, ServiceType = typeof(IOrderRepo))]
public class OrderRepo : IOrderRepo { }

// 2. Implement handler - no try-catch needed
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    protected override async Task<OrderResult> HandleCoreAsync(CreateOrder cmd, CancellationToken ct)
    {
        // Just business logic
        if (error) throw new CatgaException("error");
        return result;
    }
}

// 3. Register - one line!
builder.Services.AddGeneratedHandlers();   // Auto-register all handlers
builder.Services.AddGeneratedServices();   // Auto-register all services
```

**Code reduction: 80%!**

### 🔥 100% Native AOT Support

```csharp
// MemoryPack - zero-reflection, high-performance binary serialization
[MemoryPackable]
public partial record MyCommand(...) : IRequest<MyResult>;

// Source Generator - 编译时生成注册代码
services.AddCatga().AddInMemoryTransport();

// 发布为 Native AOT
dotnet publish -c Release -r linux-x64 --property:PublishAot=true
```

**实测性能对比** (vs 传统 .NET + 反射):

| 指标 | Catga (AOT) | 传统框架 | 提升 |
|------|------------|---------|------|
| 启动时间 | 50ms | 1200ms | **24x** |
| 二进制大小 | 8MB | 68MB | **8.5x** |
| 内存占用 | 12MB | 85MB | **7x** |
| 命令处理 | ~0.8μs | ~15μs | **18x** |

📖 [AOT 完整指南](./docs/deployment/native-aot-publishing.md)

### ⚡ 极致性能

```csharp
// 零分配设计 - ValueTask, ArrayPool, Span<T>
public async ValueTask<CatgaResult<T>> HandleAsync(...) { }

// 分布式 ID 生成 (Snowflake) - ~80ns, 零分配
var id = idGenerator.NextId();

// 批量操作 - 单次网络往返
await mediator.PublishBatchAsync(events);
```

**性能基准测试**:

```
BenchmarkDotNet v0.13.12, .NET 9.0
Intel Core i7-12700K, 1 CPU, 20 logical cores

| Method               | Mean      | Allocated |
|----------------------|-----------|-----------|
| SendCommand          | 0.814 μs  | -         |
| PublishEvent         | 0.722 μs  | -         |
| SnowflakeId          | 82.3 ns   | -         |
| Concurrent1000       | 8.15 ms   | 24 KB     |
```

🏆 [完整性能报告](./benchmarks/README.md)

### 🛡️ 编译时安全

Catga 提供 Roslyn 分析器，在编译时检测常见错误：

```csharp
// ❌ CATGA001: 缺少 [MemoryPackable] 属性
public record MyCommand(...) : ICommand<Result>;
//            ~~~~~~~~~
// 💡 Quick Fix: 添加 [MemoryPackable] 和 partial 关键字

// ❌ CATGA002: 未配置序列化器
services.AddCatga().AddInMemoryTransport();
//                  ~~~~~~~~~~~~~~~~~~~
// 💡 Quick Fix: 添加 .UseMemoryPackSerializer()
```

📖 [分析器文档](./docs/guides/analyzers.md)

### 🌐 分布式架构

```csharp
// NATS JetStream - 高性能消息传输
services.AddCatga()
    .AddNatsTransport(options =>
{
    options.Url = "nats://localhost:4222";
        options.SubjectPrefix = "myapp";
    })
    .UseMemoryPackSerializer();

// Redis - 分布式缓存、锁、幂等性
services.AddRedisDistributedCache(...)
        .AddRedisIdempotencyStore();

// QoS 保证 - AtMostOnce, AtLeastOnce, ExactlyOnce
public record ImportantCommand(...) : ICommand<Result>
{
    public QualityOfService QoS => QualityOfService.ExactlyOnce;
}
```

📖 [分布式架构](./docs/distributed/ARCHITECTURE.md) | [Kubernetes 部署](./docs/deployment/kubernetes.md)

### 🎯 ASP.NET Core 集成

```csharp
using Catga.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services.AddCatga()
    .AddInMemoryTransport()
    .UseMemoryPack();

var app = builder.Build();

// Minimal API helpers
app.MapCatgaRequest<CreateOrder, OrderCreated>("/api/orders");
app.MapCatgaQuery<GetOrderById, OrderDetail>("/api/orders/{orderId}");

app.Run();

// 自动生成端点:
// POST /commands/{CommandType}
// GET  /queries/{QueryType}
// POST /events/{EventType}
```

📖 [ASP.NET Core 指南](./docs/examples/basic-usage.md)

### 🔍 Native Debugging

```csharp
// Enable with one line
builder.Services.AddCatga()
    .UseMemoryPack()
    .WithDebug();  // ← Auto message flow tracking!

// Console automatically shows:
// [abc12345] CreateOrder ✅ (0.8ms)
//   ├─ Handler: CreateOrderHandler (0.8ms)
//   └─ Events: OrderCreated → 2 handlers (0.3ms)

// Query flows via API
GET /debug/flows              // List active flows
GET /debug/flows/{id}         // Flow details
GET /debug/stats              // Statistics
```

**Features**:
- ✅ Real-time console output with colors
- ✅ < 2MB memory (object pooling)
- ✅ < 0.5μs overhead
- ✅ NATS + Redis metadata support
- ✅ Zero-copy design

### 🔭 Full Observability

**OpenTelemetry**:
- ASP.NET Core instrumentation
- HTTP client instrumentation
- Catga tracing & metrics
- Aspire Dashboard integration

**Health Checks**:
- `/health` - Overall health
- `/health/live` - Liveness probe
- `/health/ready` - Readiness probe

**Time-Travel Debugging** (New!):
- Vue 3 debug UI with real-time updates
- Replay any flow at macro/micro level
- <0.01μs overhead with adaptive sampling
- Production-safe (0.1% sampling)

```csharp
// Enable in development
builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
{
    options.Mode = DebuggerMode.Development;
    options.SamplingRate = 1.0; // 100% capture
});

// Map UI and APIs
app.MapCatgaDebugger("/debug");

// Access:
// UI:  http://localhost:5000/debug
// API: http://localhost:5000/debug-api/*
```

📖 [Aspire Integration](./examples/OrderSystem.AppHost/README.md) | [Debugger Guide](./docs/DEBUGGER.md) | [Debugger Plan](./docs/CATGA-DEBUGGER-PLAN.md)

---

## 📦 NuGet 包

| 包名 | 描述 | 版本 |
|------|------|------|
| **Catga** | 核心框架 | [![NuGet](https://img.shields.io/nuget/v/Catga.svg)](https://www.nuget.org/packages/Catga/) |
| **Catga.InMemory** | 内存实现（开发/测试） | [![NuGet](https://img.shields.io/nuget/v/Catga.InMemory.svg)](https://www.nuget.org/packages/Catga.InMemory/) |
| **Catga.Serialization.MemoryPack** | MemoryPack 序列化（推荐） | [![NuGet](https://img.shields.io/nuget/v/Catga.Serialization.MemoryPack.svg)](https://www.nuget.org/packages/Catga.Serialization.MemoryPack/) |
| **Catga.Serialization.Json** | JSON 序列化 | [![NuGet](https://img.shields.io/nuget/v/Catga.Serialization.Json.svg)](https://www.nuget.org/packages/Catga.Serialization.Json/) |
| **Catga.Transport.Nats** | NATS 传输层 | [![NuGet](https://img.shields.io/nuget/v/Catga.Transport.Nats.svg)](https://www.nuget.org/packages/Catga.Transport.Nats/) |
| **Catga.Persistence.Redis** | Redis 持久化 | [![NuGet](https://img.shields.io/nuget/v/Catga.Persistence.Redis.svg)](https://www.nuget.org/packages/Catga.Persistence.Redis/) |
| **Catga.AspNetCore** | ASP.NET Core 集成 | [![NuGet](https://img.shields.io/nuget/v/Catga.AspNetCore.svg)](https://www.nuget.org/packages/Catga.AspNetCore/) |
| **Catga.Debugger** | Time-Travel 调试核心 | [![NuGet](https://img.shields.io/nuget/v/Catga.Debugger.svg)](https://www.nuget.org/packages/Catga.Debugger/) |
| **Catga.Debugger.AspNetCore** | 调试器 UI + APIs | [![NuGet](https://img.shields.io/nuget/v/Catga.Debugger.AspNetCore.svg)](https://www.nuget.org/packages/Catga.Debugger.AspNetCore/) |
| **Catga.SourceGenerator** | Source Generator | [![NuGet](https://img.shields.io/nuget/v/Catga.SourceGenerator.svg)](https://www.nuget.org/packages/Catga.SourceGenerator/) |

---

## 📚 文档

### 快速入门
- [30 秒快速开始](#-快速开始)
- [文档总索引](./docs/INDEX.md) 📚
- [OrderSystem 完整示例](./examples/README-ORDERSYSTEM.md) 🌟
- [API 速查](./docs/QUICK-REFERENCE.md)

### 核心概念
- [CQRS 模式](./docs/architecture/cqrs.md)
- [架构概览](./docs/architecture/ARCHITECTURE.md)
- [消息类型](./docs/api/messages.md)
- [Source Generator](./docs/guides/source-generator.md)

### 🌟 创新特性
- **[Time-Travel Debugger](./docs/DEBUGGER.md)** - 完整调试指南
- **[Debugger 架构设计](./CATGA-DEBUGGER-PLAN.md)** - 详细技术方案
- **[Source Generator 调试捕获](./docs/SOURCE-GENERATOR-DEBUG-CAPTURE.md)** - AOT 兼容

### 高级主题
- [序列化指南](./docs/guides/serialization.md)
- [分布式 ID](./docs/guides/distributed-id.md)
- [Roslyn 分析器](./docs/guides/analyzers.md)
- [Graceful Lifecycle](./docs/guides/graceful-lifecycle.md)

### 部署与运维
- [Native AOT 发布](./docs/deployment/native-aot-publishing.md)
- [Kubernetes 部署](./docs/deployment/kubernetes.md)
- [生产环境配置](./docs/deployment/production-config.md)

### 示例项目
- 🌟 **[OrderSystem 完整演示](./examples/README-ORDERSYSTEM.md)** - CQRS + 多 Handlers + Debugger
- [AppHost Orchestration](./examples/OrderSystem.AppHost/README.md) - Aspire 集群

---

## 🏗️ 项目结构

```
Catga/
├── src/
│   ├── Catga/                          # 核心框架
│   ├── Catga.InMemory/                 # 内存实现
│   ├── Catga.Serialization.MemoryPack/ # MemoryPack 序列化
│   ├── Catga.Serialization.Json/       # JSON 序列化
│   ├── Catga.Transport.Nats/           # NATS 传输
│   ├── Catga.Persistence.Redis/        # Redis 持久化
│   ├── Catga.AspNetCore/               # ASP.NET Core 集成
│   └── Catga.SourceGenerator/          # Source Generator + 分析器
├── tests/
│   └── Catga.Tests/                    # 单元测试 (191 个测试)
├── benchmarks/
│   └── Catga.Benchmarks/               # 性能基准测试
├── examples/
│   └── OrderSystem.AppHost/            # .NET Aspire 示例
└── docs/                               # 完整文档
```

---

## 🧪 测试覆盖

```
✅ 191 个单元测试全部通过
✅ 70 个性能基准测试达标
✅ 65% 代码覆盖率
```

**测试统计**:
- 核心功能: 26 个测试
- 序列化器: 36 个测试 (MemoryPack 18 + JSON 18)
- 传输层: 19 个测试 (InMemory)
- 现有测试: 110 个测试

运行测试:
```bash
dotnet test
```

---

## 🤝 贡献

欢迎贡献！请查看 [贡献指南](CONTRIBUTING.md)。

### 开发环境
- .NET 9 SDK
- Visual Studio 2022 17.8+ 或 JetBrains Rider 2024.1+
- 可选: Docker (用于 NATS/Redis 测试)

### 构建

```bash
# 还原依赖
dotnet restore

# 构建
dotnet build -c Release

# 运行测试
dotnet test -c Release

# 运行基准测试
cd benchmarks/Catga.Benchmarks
dotnet run -c Release
```

---

## 📊 性能对比

**vs MediatR**:
- 启动时间: **24x** 更快 (AOT)
- 命令处理: **18x** 更快
- 内存分配: **零分配** vs 每次 ~240 bytes

**vs MassTransit**:
- 配置复杂度: **80%** 更少
- AOT 支持: ✅ vs ❌
- 二进制大小: **8MB** vs 不支持 AOT

**vs CAP**:
- 性能: **10x** 更快 (无反射)
- AOT 支持: ✅ vs ❌
- 内存占用: **7x** 更少

详见 [性能报告](./benchmarks/README.md)

---

📖 [Complete Documentation Index](./docs/INDEX.md) | [Framework Roadmap](./docs/FRAMEWORK-ROADMAP.md)

---

## 🛣️ Roadmap

### v1.0 (当前版本)
- ✅ 核心 CQRS/Mediator
- ✅ 100% AOT 支持
- ✅ MemoryPack 序列化
- ✅ NATS/Redis 集成
- ✅ Source Generator
- ✅ Roslyn 分析器
- ✅ ASP.NET Core 集成

### v1.1 (✅ Completed)
- ✅ **SafeRequestHandler** - No try-catch needed, just business logic
- ✅ **Auto DI Registration** - ServiceType + ImplType support
- ✅ **Zero-Reflection Event Router** - Compile-time code generation
- ✅ **Graceful Lifecycle** - Shutdown & auto-recovery
- ✅ **Native Debugging** - Message flow tracking with < 0.5μs overhead
- ✅ **Aspire Integration** - OpenTelemetry, health checks, resilience
- ✅ **Event Sourcing** - EventStore + Repository pattern

### v2.0 (Future)
- 🔮 GraphQL integration
- 🔮 RabbitMQ transport
- 🔮 Enhanced distributed tracing
- 🔮 Real-time debug UI

---

## 📄 许可证

本项目基于 [MIT 许可证](LICENSE)。

---

## 🙏 致谢

感谢以下优秀项目的启发：
- [MediatR](https://github.com/jbogard/MediatR) - 经典 .NET 中介者模式
- [MemoryPack](https://github.com/Cysharp/MemoryPack) - 高性能序列化
- [NATS](https://nats.io/) - 云原生消息系统
- [CAP](https://github.com/dotnetcore/CAP) - 分布式事务解决方案

---

<div align="center">

**⭐ 如果 Catga 对你有帮助，请给个 Star！**

[GitHub](https://github.com/Cricle/Catga) · [NuGet](https://www.nuget.org/packages/Catga/) · [文档](./docs/README.md) · [示例](./examples/)

Made with ❤️ by [Catga Contributors](https://github.com/Cricle/Catga/graphs/contributors)

</div>
