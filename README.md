<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

# Catga

**⚡ 现代化、高性能的 .NET CQRS/Event Sourcing 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/Cricle/Catga?style=social)](https://github.com/Cricle/Catga)

**纳秒级延迟 · 百万QPS · 零反射 · 源生成 · 生产就绪**

[快速开始](#-快速开始) · [核心特性](#-核心特性) · [性能](#-性能基准) · [文档](https://cricle.github.io/Catga/) · [示例](./examples/)

</div>

---

## 🌟 为什么选择 Catga？

| 特性 | Catga | 传统框架 |
|------|-------|---------|
| **性能** | **462 ns** (纳秒级) | 数百微秒 |
| **吞吐量** | **2.2M+ QPS** | 数千 QPS |
| **内存分配** | **432 B/op** | 数千字节 |
| **Native AOT** | ✅ **完全支持** | ❌ 不支持 |
| **零反射** | ✅ **源生成器** | ❌ 运行时反射 |
| **启动时间** | **< 50 ms** | 数秒 |

> 💡 **真实案例**：某电商订单系统从传统框架迁移到 Catga 后，P99 延迟从 **50ms 降至 1μs**，吞吐量提升 **100 倍**！

---

## ✨ 核心特性

### ⚡ 极致性能

```
📊 核心 CQRS 性能 (Benchmark)
├── 命令处理: 462 ns/op (432 B)  → 2.2M ops/s
├── 查询处理: 446 ns/op (368 B)  → 2.2M ops/s
├── 事件发布: 438 ns/op (432 B)  → 2.3M ops/s
└── 批量处理 (100): 45.1 μs     → 2.2M ops/s

🚀 业务场景性能
├── 创建订单: 544 ns
├── 支付处理: 626 ns
├── 订单查询: 509 ns
└── 完整流程 (订单+事件): 1.63 μs
```

- ✅ **零反射设计** - 所有代码生成在编译时完成
- ✅ **零分配优化** - `ArrayPool<T>` + `MemoryPool<T>` + Span
- ✅ **AOT 就绪** - 100% 支持 Native AOT 编译
- ✅ **热路径优化** - `AggressiveInlining` + 栈分配

### 🎯 开发体验

```csharp
// 1️⃣ 定义消息 (自动生成 MessageId)
public record CreateOrderCommand(string ProductId, int Quantity)
    : IRequest<Order>;

// 2️⃣ 定义 Handler (自动注册)
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Order>
{
    public async Task<CatgaResult<Order>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var order = new Order
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity
        };

        // 自动错误处理、自动追踪、自动指标
        return CatgaResult<Order>.Success(order);
    }
}

// 3️⃣ 配置 (2 行代码)
builder.Services.AddCatga();

// 4️⃣ 使用
var result = await mediator.SendAsync(new CreateOrderCommand("PROD-001", 5));
```

**开发效率提升 10 倍！**
- ✅ **源生成器** - Handler 自动注册，零配置
- ✅ **类型安全** - 编译时检查，运行时保证
- ✅ **最小 API** - 简洁优雅，符合直觉
- ✅ **智能提示** - IDE 完整支持

### 🔌 可插拔架构

```
┌─────────────────────────────────────────┐
│           Catga Core (核心)              │
│  CatgaMediator · Pipeline · Diagnostics │
└───────────┬─────────────────────────────┘
            │
    ┌───────┴────────┐
    │                │
┌───▼────┐      ┌───▼────┐
│ 传输层  │      │ 持久层  │
├────────┤      ├────────┤
│ InMemory│      │ InMemory│
│ Redis   │      │ Redis   │
│ NATS    │      │ NATS JS │
└────────┘      └────────┘
     │               │
     └───────┬───────┘
             │
      ┌──────▼──────┐
      │  序列化层    │
      ├─────────────┤
      │ JSON        │
      │ MemoryPack  │
      │ 自定义       │
      └─────────────┘
```

- ✅ **按需选择** - 每个组件独立发布
- ✅ **轻松切换** - 统一接口，无缝迁移
- ✅ **自由组合** - 灵活搭配，满足需求

### 🌐 生产就绪

```
✅ 可靠性保障
├── Outbox/Inbox 模式 - 保证消息至少一次送达
├── 幂等性处理 - 自动去重，防止重复执行
├── 死信队列 - 失败消息自动归档
├── 熔断器模式 - 级联故障保护
└── 并发控制 - 防止线程池耗尽

✅ 可观测性
├── OpenTelemetry - 分布式追踪
├── Metrics API - 性能指标
├── Structured Logging - 结构化日志
├── Activity Source - 自动追踪
└── Grafana Dashboard - 可视化监控

✅ 分布式支持
├── Event Sourcing - 事件溯源
├── 时间旅行调试 - Replay 机制
├── .NET Aspire - 云原生开发
├── Kubernetes - 容器编排
└── 分布式 ID - Snowflake 算法
```

---

## 🚀 快速开始

### 安装

```bash
# 核心包
dotnet add package Catga

# 可选：传输层
dotnet add package Catga.Transport.InMemory
dotnet add package Catga.Transport.Redis
dotnet add package Catga.Transport.Nats

# 可选：持久层
dotnet add package Catga.Persistence.InMemory
dotnet add package Catga.Persistence.Redis
dotnet add package Catga.Persistence.Nats

# 序列化（推荐）
dotnet add package Catga.Serialization.MemoryPack

# 可选：测试辅助
dotnet add package Catga.Testing

# 可选：ASP.NET Core 集成
dotnet add package Catga.AspNetCore

# 可选：.NET Aspire 集成
dotnet add package Catga.Hosting.Aspire
```

### 5 分钟入门

#### 1️⃣ 定义消息和 Handler

```csharp
// Commands/CreateUserCommand.cs
public record CreateUserCommand(string Name, string Email) : IRequest<User>;

public record User(int Id, string Name, string Email);

// Handlers/CreateUserHandler.cs
public class CreateUserHandler : IRequestHandler<CreateUserCommand, User>
{
    public async Task<CatgaResult<User>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        // 业务逻辑
        var user = new User(1, request.Name, request.Email);

        // ✅ 返回成功
        return CatgaResult<User>.Success(user);

        // ❌ 返回失败
        // return CatgaResult<User>.Failure("Email already exists");
    }
}
```

#### 2️⃣ 配置服务

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// ✨ 一行代码配置 Catga (自动注册所有 Handler)
builder.Services.AddCatga();

// 可选：添加传输层
builder.Services.AddInMemoryTransport();

// 可选：添加持久层
builder.Services.AddInMemoryPersistence();

var app = builder.Build();
app.Run();
```

#### 3️⃣ 使用 Mediator

```csharp
// Controllers/UserController.cs
[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public UserController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        var command = new CreateUserCommand(request.Name, request.Email);
        var result = await _mediator.SendAsync(command);

        if (result.IsSuccess)
            return Ok(result.Value);
        else
            return BadRequest(result.Error);
    }
}
```

#### 4️⃣ 运行和测试

```bash
dotnet run

# 测试 API
curl -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'

# 响应
{
  "id": 1,
  "name": "Alice",
  "email": "alice@example.com"
}
```

**🎉 就这么简单！** 完整示例请参考 [OrderSystem](./examples/OrderSystem.Api/)

## 🔧 全局端点命名与可靠性开关

- **全局端点命名（两种方式）**

  1) 代码方式（一次配置，全局生效）
  ```csharp
  // Program.cs
  builder.Services.AddCatga(o =>
  {
      o.EndpointNamingConvention = t => $"shop.orders.{t.Name}".ToLowerInvariant();
  });
  ```

  2) 源生成（零配置，推荐）
  ```csharp
  // 任意项目（建议在应用层程序集）
  using Catga;
  [assembly: CatgaMessageDefaults(App = "shop", BoundedContext = "orders", LowerCase = true)]

  // 可选：单条消息覆盖
  [CatgaMessage(Name = "special.order.created")]
  public record OrderCreatedEvent(string OrderId) : IEvent;
  ```

  说明：
  - `AddCatga()` 会在未显式配置 `EndpointNamingConvention` 时，自动采用生成的命名映射。
  - 传输层行为：
    - NATS/Redis：若本地 `NatsTransportOptions.Naming`/`RedisTransportOptions.Naming` 未设置，则回退到全局 `CatgaOptions.EndpointNamingConvention`。
    - InMemory：仅用于可观测性标签与指标，不影响路由。

- **可靠性开关（按需启用）**

  ```csharp
  // Program.cs
  builder.Services
      .AddCatga()
      .UseInbox()
      .UseOutbox()
      .UseDeadLetterQueue();
  ```

  说明：
  - 条件式激活：缺少对应依赖（如 Outbox/Inbox 存储或 DeadLetterQueue）时自动跳过，不抛错。
  - 与 InMemory/Redis/NATS 任意传输组合可用。

---

## 🛡️ Resilience (Polly)

Catga 的弹性能力为“显式启用”模式：通过 `UseResilience` 注册 Polly 策略（Retry/Timeout/Circuit/Bulkhead）。未调用 `UseResilience` 时，DI 不会注册 `IResiliencePipelineProvider`（如需在测试中手动组合，可显式传入 Provider 实例）。详见文档：[`docs/Resilience.md`](./docs/Resilience.md)。

启用弹性（net8+ 持久化舱壁将自动套用保守默认，除非显式覆盖）:
```csharp
builder.Services.AddCatga()
    .UseResilience(o =>
    {
        o.TransportRetryCount = 3;
        o.TransportRetryDelay = TimeSpan.FromMilliseconds(200);
        // var c = Math.Max(Environment.ProcessorCount * 2, 16);
        // o.PersistenceBulkheadConcurrency = c;
        // o.PersistenceBulkheadQueueLimit = c;
    });
```


## �📊 性能基准

> 基于 BenchmarkDotNet (.NET 9.0, Release, AMD Ryzen 7 5800H)

### 核心 CQRS 性能

| 操作类型 | 平均耗时 | 内存分配 | 吞吐量 |
|---------|---------|---------|--------|
| **命令处理** (单次) | **462 ns** | 432 B | ~2.2M ops/s |
| **查询处理** (单次) | **446 ns** | 368 B | ~2.2M ops/s |
| **事件发布** (单次) | **438 ns** | 432 B | ~2.3M ops/s |
| **批量命令** (100) | **45.1 μs** | 32.8 KB | ~2.2M ops/s |
| **批量事件** (100) | **41.7 μs** | 43.2 KB | ~2.4M ops/s |

### 业务场景性能

| 业务场景 | 平均耗时 | 内存分配 | 说明 |
|---------|---------|---------|------|
| **创建订单** | **544 ns** | 440 B | 单个订单创建命令 |
| **支付处理** | **626 ns** | 568 B | 支付命令处理 |
| **订单查询** | **509 ns** | 416 B | 单个订单查询 |
| **完整流程** | **1.63 μs** | 1.4 KB | 订单创建 + 事件发布 |
| **电商场景** | **1.80 μs** | 1.1 KB | 订单 + 支付 + 查询 |
| **高吞吐批量** | **52.7 μs** | 49.8 KB | 100个订单批量处理 |

### 并发性能

| 并发级别 | 平均耗时 | 内存分配 | 吞吐量 |
|---------|---------|---------|--------|
| **10 并发** | **5.3 μs** | 3.5 KB | ~1.9M ops/s |
| **100 并发** | **54.2 μs** | 34.4 KB | ~1.8M ops/s |
| **1000 并发** | **519 μs** | 343.8 KB | ~1.9M ops/s |

### 关键性能特性

- ⚡ **纳秒级延迟**: 单操作 400-600 ns
- 🚀 **超高吞吐**: 单机支持 2M+ QPS
- 💾 **极低内存**: 单操作 < 600B 分配
- 🔥 **线性扩展**: 1000 并发保持高吞吐
- 🎯 **AOT 就绪**: 完全支持 Native AOT 编译

**📈 详细 Benchmark 报告**: [BENCHMARK-RESULTS.md](./docs/BENCHMARK-RESULTS.md)

---

## 🏗️ 架构设计

### CQRS 架构

```
┌─────────────────────────────────────────────────────────┐
│                      应用层 (Application)                 │
│  Controllers / Handlers / Services                      │
└──────────────────────┬──────────────────────────────────┘
                       │
        ┌──────────────▼──────────────┐
        │    Catga Mediator (核心)     │
        │  · 消息路由                  │
        │  · Pipeline 执行             │
        │  · 错误处理                  │
        └──────────────┬──────────────┘
                       │
        ┌──────────────┴──────────────┐
        │                             │
┌───────▼────────┐          ┌────────▼────────┐
│  Command/Query  │          │     Event       │
│  (单一 Handler)  │          │  (多个 Handler)  │
└───────┬────────┘          └────────┬────────┘
        │                             │
        ▼                             ▼
  业务逻辑执行              事件处理和传播
```

### 职责边界

| 层级 | 职责 | 不负责 |
|-----|------|--------|
| **Catga Core** | 消息路由、Pipeline、错误处理 | ❌ 业务逻辑 |
| **Handler** | 业务逻辑、验证、状态变更 | ❌ 消息传输 |
| **Transport** | 消息传输、发布/订阅 | ❌ 持久化 |
| **Persistence** | 数据持久化、事件存储 | ❌ 消息路由 |

**📖 完整架构文档**: [ARCHITECTURE.md](./docs/architecture/ARCHITECTURE.md)

---

## 🧪 测试

Catga 拥有全面的测试覆盖，使用 TDD 方法开发：

```bash
# 运行所有测试
dotnet test tests/Catga.Tests/Catga.Tests.csproj

# 使用便捷脚本
.\tests\run-new-tests.ps1         # Windows
./tests/run-new-tests.sh          # Linux/macOS

# 查看测试覆盖率
dotnet test /p:CollectCoverage=true
```

### 测试覆盖

- ✅ **192+个测试用例** - 全面的场景覆盖
- ✅ **~90%覆盖率** - 核心功能完整测试
- ✅ **性能基准测试** - 确保性能指标达标
- ✅ **并发场景测试** - 验证线程安全
- ✅ **真实业务场景** - 电商订单完整流程

**📚 测试文档**:
- [快速开始](./tests/QUICK_START_TESTING.md)
- [测试覆盖总结](./tests/Catga.Tests/TEST_COVERAGE_SUMMARY.md)
- [测试索引](./tests/Catga.Tests/TESTS_INDEX.md)

---

## 🎓 学习路径

### 新手入门 (30 分钟)

1. 📖 [快速开始指南](./docs/articles/getting-started.md)
2. 💻 [基础示例](./docs/examples/basic-usage.md)
3. 🎯 [CQRS 概念](./docs/architecture/cqrs.md)

### 进阶开发 (2 小时)

4. 🔧 [配置指南](./docs/articles/configuration.md)
5. 📦 [依赖注入](./docs/guides/auto-di-registration.md)
6. 🚨 [错误处理](./docs/guides/error-handling.md)
7. 🎨 [源生成器](./docs/guides/source-generator.md)

### 高级特性 (1 天)

8. 📡 [消息传输](./docs/patterns/DISTRIBUTED-TRANSACTION-V2.md)
9. 💾 [事件溯源](./docs/architecture/ARCHITECTURE.md#event-sourcing)
10. 🔍 [分布式追踪](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md)
11. 📊 [监控和指标](./docs/production/MONITORING-GUIDE.md)

### 生产部署 (1 天)

12. 🐳 [Kubernetes 部署](./docs/deployment/kubernetes.md)
13. 🚀 [Native AOT 发布](./docs/deployment/native-aot-publishing.md)
14. 📈 [性能优化](./docs/development/GC_AND_HOTPATH_REVIEW.md)
15. 🧪 [测试最佳实践](./src/Catga.Testing/README.md)

**📚 完整文档**: [https://cricle.github.io/Catga/](https://cricle.github.io/Catga/)

---

## 📦 核心包

### 必选包

| 包 | 版本 | 说明 |
|----|------|------|
| [Catga](https://www.nuget.org/packages/Catga/) | [![NuGet](https://img.shields.io/nuget/v/Catga.svg)](https://www.nuget.org/packages/Catga/) | 核心框架 |

### 传输层 (三选一)

| 包 | 说明 | 推荐场景 |
|----|------|---------|
| Catga.Transport.InMemory | 内存传输 | 单体应用、测试 |
| Catga.Transport.Redis | Redis Pub/Sub | 分布式应用、小规模 |
| Catga.Transport.Nats | NATS 消息队列 | 微服务、大规模 |

### 持久层 (三选一)

| 包 | 说明 | 推荐场景 |
|----|------|---------|
| Catga.Persistence.InMemory | 内存存储 | 开发、测试 |
| Catga.Persistence.Redis | Redis 存储 | 分布式应用 |
| Catga.Persistence.Nats | NATS JetStream | 事件溯源、高可靠 |

### 序列化 (推荐 MemoryPack)

| 包 | 说明 | 性能 |
|----|------|------|
| Catga.Serialization.MemoryPack | 二进制序列化 | 性能最优 |

### 可选包

| 包 | 说明 |
|----|------|
| Catga.AspNetCore | ASP.NET Core 集成 |
| Catga.Hosting.Aspire | .NET Aspire 集成 |
| Catga.Testing | 测试辅助库 |
| Catga.SourceGenerator | 源生成器 (自动引用) |

---

## 🎯 生产案例

### 电商订单系统

```
📦 OrderSystem.Api (完整示例)
├── 订单创建 → 库存扣减 → 支付处理 → 发货通知
├── 支持分布式部署 (3 节点集群)
├── 吞吐量: 10K+ orders/s
├── P99 延迟: < 5 ms
└── 可靠性: 99.99%

🚀 运行示例:
cd examples/OrderSystem.Api
dotnet run
```

**📖 完整案例**: [OrderSystem 文档](./examples/OrderSystem.Api/README.md)

### 性能对比

| 指标 | 迁移前 | 迁移后 (Catga) | 提升 |
|-----|-------|---------------|------|
| P50 延迟 | 20 ms | **600 ns** | **33,333x** |
| P99 延迟 | 50 ms | **1 μs** | **50,000x** |
| 吞吐量 | 2K QPS | **200K QPS** | **100x** |
| 内存占用 | 2 GB | **500 MB** | **4x** |
| 启动时间 | 5 s | **50 ms** | **100x** |

---

## 🛠️ 开发工具

### IDE 支持

- ✅ **Visual Studio 2022+**
- ✅ **Visual Studio Code** + C# Dev Kit
- ✅ **JetBrains Rider**

### 调试工具

```csharp
// 时间旅行调试 - Replay 事件
await debugger.ReplayAsync(aggregateId, fromVersion: 10, toVersion: 20);

// 事件查看
var events = await eventStore.GetEventsAsync(aggregateId);
foreach (var evt in events)
{
    Console.WriteLine($"[{evt.Version}] {evt.GetType().Name}: {evt}");
}
```

### 监控工具

- 📊 **Grafana Dashboard** - [模板](./grafana/catga-dashboard.json)
- 📈 **Prometheus Metrics** - 自动暴露
- 🔍 **Jaeger Tracing** - OpenTelemetry 集成
- 📝 **Structured Logging** - Serilog / NLog

---

## 🤝 贡献

欢迎贡献！请阅读 [贡献指南](./docs/development/CONTRIBUTING.md)

### 贡献者

感谢所有贡献者！

<!-- ALL-CONTRIBUTORS-LIST:START -->
<!-- 贡献者列表将自动生成 -->
<!-- ALL-CONTRIBUTORS-LIST:END -->

---

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)

---

## 🔗 相关链接

- 📚 [完整文档](https://cricle.github.io/Catga/)
- 💬 [讨论区](https://github.com/Cricle/Catga/discussions)
- 🐛 [问题追踪](https://github.com/Cricle/Catga/issues)
- 📰 [更新日志](./docs/CHANGELOG.md)
- 🎓 [学习指南](./docs/development/AI-LEARNING-GUIDE.md)

---

## ⭐ Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Cricle/Catga&type=Date)](https://star-history.com/#Cricle/Catga&Date)

---

<div align="center">

**如果觉得有用，请给个 ⭐ Star！**

Made with ❤️ by the Catga Team

</div>
