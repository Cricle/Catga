# Catga

<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

**⚡ 简洁、高性能的 .NET CQRS 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**零反射 · 高性能 · 可插拔 · 简单至上**

[快速开始](#-快速开始) · [核心特性](#-核心特性) · [架构设计](#-架构设计) · [文档](https://cricle.github.io/Catga/) · [示例](./examples/)

</div>

---

## ✨ 核心特性

### 🚀 极致性能

- **零反射**：AOT 友好，无运行时反射
- **零分配**：内存池优化，GC 压力极小
- **高吞吐**：Command/Query < 1μs，Event 广播 < 500ns
- **Span 优化**：使用 `Span<T>` 实现零拷贝

### 🎯 简洁设计

- **6 个核心文件夹**：清晰的代码组织（从 14 个精简至 6 个）
- **10 个错误代码**：明确的错误语义
- **最小 API**：删除 50+ 冗余抽象
- **2 行启动**：最简配置，开箱即用

### 🔌 可插拔架构

**传输层**:
- `Catga.Transport.InMemory` - 进程内
- `Catga.Transport.Redis` - 基于 Redis Pub/Sub & Streams
- `Catga.Transport.Nats` - 基于 NATS JetStream

**持久化层**:
- `Catga.Persistence.InMemory` - 内存存储
- `Catga.Persistence.Redis` - Redis 持久化
- `Catga.Persistence.Nats` - NATS KeyValue Store

**序列化器**:
- `Catga.Serialization.Json` - System.Text.Json (AOT 优化)
- `Catga.Serialization.MemoryPack` - 高性能二进制

### 🌐 生产就绪

- **Outbox/Inbox**：确保消息可靠性
- **幂等性**：自动去重处理
- **分布式追踪**：OpenTelemetry 集成
- **Snowflake ID**：分布式唯一 ID 生成

---

## 📦 快速开始

### 安装

```bash
dotnet add package Catga
dotnet add package Catga.Serialization.Json
dotnet add package Catga.Transport.InMemory
```

### 2 行代码启动

```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册 Catga
builder.Services.AddCatga()
    .UseJsonSerializer()
    .UseInMemoryTransport();

var app = builder.Build();
app.Run();
```

### 定义消息

```csharp
using Catga.Abstractions;

// Command
public record CreateOrderCommand(
    string CustomerId, 
    List<OrderItem> Items
) : IRequest<OrderCreatedResult>;

// Result
public record OrderCreatedResult(long OrderId, decimal TotalAmount);

// Event
public record OrderCreatedEvent(long OrderId, string CustomerId) : IEvent;
```

### 实现 Handler

```csharp
using Catga.Abstractions;
using Catga.Core;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request, 
        CancellationToken cancellationToken)
    {
        // 验证
        if (request.Items.Count == 0)
            return CatgaResult<OrderCreatedResult>.Failure("订单不能为空");

        // 业务逻辑
        var orderId = GenerateOrderId();
        var total = request.Items.Sum(x => x.Price * x.Quantity);

        // 发布事件
        await _mediator.PublishAsync(
            new OrderCreatedEvent(orderId, request.CustomerId), 
            cancellationToken);

        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(orderId, total));
    }
}
```

### 发送请求

```csharp
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);
    return result.ToHttpResult(); // 自动转换为 HTTP 响应
});
```

---

## 🏗️ 架构设计

### 核心文件夹结构

```
src/Catga/
├── Abstractions/       # 所有接口定义 (15 files)
│   ├── IRequest<T>, IEvent
│   ├── IRequestHandler<,>
│   ├── IMessageTransport
│   ├── IEventStore, IOutboxStore, IInboxStore
│   └── IMessageSerializer
│
├── Core/              # 核心实现 (22 files)
│   ├── CatgaResult<T>
│   ├── ErrorCodes
│   ├── SnowflakeIdGenerator
│   ├── HandlerCache
│   ├── MemoryPoolManager
│   └── ValidationHelper
│
├── DependencyInjection/  # DI 扩展 (3 files)
│   ├── CatgaServiceBuilder
│   └── CorrelationIdDelegatingHandler
│
├── Pipeline/          # 管道系统
│   ├── PipelineExecutor
│   └── Behaviors/
│       ├── LoggingBehavior
│       ├── ValidationBehavior
│       ├── OutboxBehavior
│       ├── InboxBehavior
│       ├── IdempotencyBehavior
│       └── RetryBehavior
│
├── Observability/     # 监控
│   ├── CatgaActivitySource
│   └── CatgaDiagnostics
│
├── CatgaMediator.cs   # Mediator 实现
└── Serialization.cs   # 序列化基类
```

### 简化原则

**删除的冗余抽象**:
- ❌ `IRpcClient` / `IRpcServer` - 未使用
- ❌ `IDistributedCache` - 过度设计
- ❌ `IDistributedLock` - 过度设计
- ❌ `AggregateRoot` / DDD 基类 - 强制架构
- ❌ `SafeRequestHandler` - 不必要的抽象
- ❌ `ResultMetadata` - 复杂度过高

**保留的核心功能**:
- ✅ CQRS (Command/Query/Event)
- ✅ Outbox/Inbox 模式
- ✅ 幂等性处理
- ✅ 分布式追踪
- ✅ 管道行为
- ✅ 可插拔传输/持久化

---

## 📊 性能基准

```
BenchmarkDotNet v0.13.12, Windows 11

| Method                    | Mean      | Allocated |
|-------------------------- |----------:|----------:|
| Command_Execution         | 723 ns    | 448 B     |
| Query_Execution           | 681 ns    | 424 B     |
| Event_Publish             | 412 ns    | 320 B     |
| Event_Publish_10_Handlers | 2.8 μs    | 1.2 KB    |
| Snowflake_ID_Generation   | 45 ns     | 0 B       |
| JSON_Serialize            | 485 ns    | 256 B     |
| MemoryPack_Serialize      | 128 ns    | 128 B     |
```

*测试环境: AMD Ryzen 9 7950X, 64GB RAM, .NET 9.0*

---

## 🎯 错误处理

### 10 个核心错误代码

```csharp
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string HandlerFailed = "HANDLER_FAILED";
    public const string PipelineFailed = "PIPELINE_FAILED";
    public const string PersistenceFailed = "PERSISTENCE_FAILED";
    public const string TransportFailed = "TRANSPORT_FAILED";
    public const string SerializationFailed = "SERIALIZATION_FAILED";
    public const string LockFailed = "LOCK_FAILED";
    public const string Timeout = "TIMEOUT";
    public const string Cancelled = "CANCELLED";
    public const string Unknown = "UNKNOWN";
}
```

### 使用 CatgaResult

```csharp
// 成功
return CatgaResult<T>.Success(value);

// 失败 - 简单错误
return CatgaResult<T>.Failure("用户不存在");

// 失败 - 带错误码
return CatgaResult<T>.Failure(ErrorInfo.Validation("手机号格式错误"));

// 检查结果
if (result.IsSuccess)
{
    var data = result.Value;
}
else
{
    var error = result.Error;
    var code = result.ErrorCode;
}
```

---

## 🔧 高级功能

### Outbox 模式（可靠消息发送）

```csharp
builder.Services.AddCatga()
    .UseJsonSerializer()
    .UseRedisTransport(options => options.ConnectionString = "localhost:6379")
    .AddRedisOutbox();

// 自动将事件保存到 Outbox，异步可靠发送
await mediator.PublishAsync(new OrderCreatedEvent(...));
```

### Inbox 模式（消息去重）

```csharp
builder.Services.AddCatga()
    .AddRedisInbox() // 自动过滤重复消息
    .AddIdempotency(); // 幂等性处理
```

### 分布式追踪

```csharp
builder.Services.AddCatga()
    .AddDistributedTracing(); // 自动集成 OpenTelemetry

// 自动传播追踪上下文
await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);
```

### 自定义 Behavior

```csharp
public class CustomBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 前置逻辑
        Console.WriteLine($"Before: {typeof(TRequest).Name}");

        var result = await next();

        // 后置逻辑
        Console.WriteLine($"After: {result.IsSuccess}");

        return result;
    }
}

// 注册
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CustomBehavior<,>));
```

---

## 📚 文档

- [快速开始](docs/articles/getting-started.md)
- [架构设计](docs/architecture/ARCHITECTURE.md)
- [错误处理](docs/guides/error-handling.md)
- [内存优化](docs/guides/memory-optimization-guide.md)
- [分布式追踪](docs/observability/DISTRIBUTED-TRACING-GUIDE.md)
- [部署指南](docs/deployment/)
- [示例项目](examples/OrderSystem.Api/)

---

## 🤝 贡献

欢迎贡献！请查看 [贡献指南](CONTRIBUTING.md)。

---

## 📄 许可证

本项目采用 [MIT](LICENSE) 许可证。

---

## 🌟 设计哲学

**Simple > Perfect**
- 更少的文件夹 → 更易导航
- 更少的抽象 → 更易理解
- 更少的代码 → 更易维护

**Focused > Comprehensive**
- 专注 CQRS 核心
- 删除未使用的功能
- 保持 API 最小化

**Fast > Feature-Rich**
- 性能优先
- 零分配优化
- AOT 兼容

---

<div align="center">

**Made with ❤️ for .NET developers**

**如果觉得有用，请给个 ⭐ Star！**

</div>
