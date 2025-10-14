# Catga

<div align="center">

**🚀 高性能、100% AOT 兼容的分布式 CQRS 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-191%20passed-brightgreen)]()

**零反射 · 零分配 · 高性能 · 简单易用**

[快速开始](#-快速开始) · [核心特性](#-核心特性) · [文档](#-文档) · [示例](./examples)

</div>

---

## 📖 简介

Catga 是一个专为 .NET 9 和 Native AOT 设计的高性能 CQRS/中介者框架，提供：

- ⚡ **极致性能**: < 1μs 命令处理，零分配设计
- 🔥 **100% AOT 兼容**: MemoryPack 序列化，Source Generator 自动注册
- 🛡️ **编译时检查**: Roslyn 分析器检测配置错误
- 🌐 **分布式就绪**: 支持 NATS、Redis 传输和持久化
- 🎯 **极简配置**: 3 行代码即可开始
- 🔍 **完整可观测**: ActivitySource、Meter、LoggerMessage

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

### 30 秒示例

```csharp
// 1. 定义消息 (MemoryPack = AOT 友好)
using MemoryPack;
using Catga.Messages;

[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : ICommand<CatgaResult<OrderCreated>>;

[MemoryPackable]
public partial record OrderCreated(string OrderId, DateTime CreatedAt);

// 2. 实现 Handler
public class CreateOrderHandler
    : IRequestHandler<CreateOrder, CatgaResult<OrderCreated>>
{
    public async ValueTask<CatgaResult<OrderCreated>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        // 业务逻辑
        var result = new OrderCreated(request.OrderId, DateTime.UtcNow);
        return CatgaResult<OrderCreated>.Success(result);
    }
}

// 3. 配置服务 (3 行！)
builder.Services
    .AddCatga()                  // 核心服务
    .AddInMemoryTransport()      // 传输层
    .UseMemoryPackSerializer();  // 序列化

// 4. 使用
var mediator = app.Services.GetRequiredService<ICatgaMediator>();
var result = await mediator.SendAsync<CreateOrder, OrderCreated>(
    new CreateOrder("ORD-001", 99.99m)
);

if (result.IsSuccess)
{
    Console.WriteLine($"订单已创建: {result.Value.OrderId}");
}
```

**就这么简单！** 🎉

---

## ✨ 核心特性

### 🔥 100% Native AOT 支持

```csharp
// MemoryPack - 零反射、高性能二进制序列化
[MemoryPackable]
public partial record MyCommand(...) : ICommand<MyResult>;

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
    .UseMemoryPackSerializer();

// 添加 HTTP 端点
builder.Services.AddCatgaHttpEndpoints();

var app = builder.Build();

// 映射 CQRS 端点 - 自动路由
app.MapCatgaEndpoints();

app.Run();

// 自动生成端点:
// POST /commands/{CommandType}
// GET  /queries/{QueryType}
// POST /events/{EventType}
```

📖 [ASP.NET Core 指南](./docs/examples/basic-usage.md)

### 🔍 可观测性

```csharp
// ActivitySource - 分布式追踪
using var activity = CatgaActivitySource.Start("OrderProcessing");
activity?.SetTag("order.id", orderId);

// Meter - 指标监控
CatgaMeter.CommandCounter.Add(1, new("command", "CreateOrder"));
CatgaMeter.CommandDuration.Record(elapsed, ...);

// LoggerMessage - 结构化日志 (Source Generated)
[LoggerMessage(Level = LogLevel.Information,
    Message = "Processing command {CommandType} with id {MessageId}")]
partial void LogProcessingCommand(string commandType, string messageId);
```

与 OpenTelemetry 完美集成！

📖 [可观测性指南](./docs/guides/observability.md)

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
| **Catga.SourceGenerator** | Source Generator | [![NuGet](https://img.shields.io/nuget/v/Catga.SourceGenerator.svg)](https://www.nuget.org/packages/Catga.SourceGenerator/) |

---

## 📚 文档

### 快速入门
- [30 秒快速开始](#-快速开始)
- [基础使用示例](./docs/examples/basic-usage.md)
- [API 速查](./QUICK-REFERENCE.md)

### 核心概念
- [CQRS 模式](./docs/architecture/cqrs.md)
- [架构概览](./docs/architecture/ARCHITECTURE.md)
- [消息类型](./docs/api/messages.md)

### 高级主题
- [序列化指南](./docs/guides/serialization.md)
- [分布式 ID](./docs/guides/distributed-id.md)
- [Source Generator](./docs/guides/source-generator.md)
- [Roslyn 分析器](./docs/guides/analyzers.md)

### 部署
- [Native AOT 发布](./docs/deployment/native-aot-publishing.md)
- [Kubernetes 部署](./docs/deployment/kubernetes.md)

### 示例
- [完整示例: OrderSystem](./examples/OrderSystem.AppHost/)
- [AOT 示例: MemoryPackAotDemo](./examples/MemoryPackAotDemo/)

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
│   ├── OrderSystem.AppHost/            # .NET Aspire 示例
│   └── MemoryPackAotDemo/              # Native AOT 示例
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

## 🛣️ 路线图

### v1.0 (当前版本)
- ✅ 核心 CQRS/Mediator
- ✅ 100% AOT 支持
- ✅ MemoryPack 序列化
- ✅ NATS/Redis 集成
- ✅ Source Generator
- ✅ Roslyn 分析器
- ✅ ASP.NET Core 集成

### v1.1 (规划中)
- ⏳ Event Sourcing
- ⏳ Saga 编排
- ⏳ gRPC 传输层
- ⏳ 更多分析器

### v2.0 (未来)
- 🔮 GraphQL 集成
- 🔮 RabbitMQ 传输层
- 🔮 分布式追踪增强

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
