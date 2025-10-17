# Catga

<div align="center">

**🚀 高性能、100% AOT 兼容的 .NET 9 CQRS 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-194%20passed-brightgreen)]()

**零反射 · 零分配 · 源生成器 · 生产就绪**

[快速开始](#-快速开始) · [核心特性](#-核心特性) · [文档](./docs/INDEX.md) · [示例](./examples/OrderSystem.Api/)

</div>

---

## 📖 简介

Catga 是专为 .NET 9 和 Native AOT 设计的现代化 CQRS 框架，通过 **Source Generator** 和创新设计实现极致性能和开发体验。

### 🎯 核心价值

- ⚡ **极致性能** - <20μs 命令处理，零内存分配设计
- 🔥 **100% AOT 兼容** - MemoryPack 序列化，Source Generator 自动注册
- 🛡️ **编译时安全** - Roslyn 分析器检测配置错误
- 🌐 **分布式就绪** - NATS/Redis 传输与持久化
- 🎨 **最小配置** - 2 行代码启动，自动依赖注入
- 🔍 **完整可观测** - OpenTelemetry + Jaeger 原生集成
- 🚀 **生产级** - 优雅关闭、自动恢复、错误回滚

### 🌟 创新特性

1. **SafeRequestHandler** - 零 try-catch，自动错误处理和回滚
2. **Source Generator** - 零反射，编译时代码生成，AOT 优先
3. **OpenTelemetry Native** - 与 Jaeger 深度集成的分布式追踪
4. **Graceful Lifecycle** - 优雅的生命周期管理（关闭/恢复）
5. **.NET Aspire 集成** - 原生支持云原生开发

---

## 🚀 快速开始

### 安装包

```bash
# 核心框架 + 内存实现
dotnet add package Catga
dotnet add package Catga.InMemory

# AOT 兼容序列化
dotnet add package Catga.Serialization.MemoryPack

# Source Generator（自动注册）
dotnet add package Catga.SourceGenerator

# ASP.NET Core 集成（可选）
dotnet add package Catga.AspNetCore
```

### 30 秒示例

```csharp
using Catga;
using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using MemoryPack;

// 1. 定义消息（MemoryPack = AOT 友好）
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(string OrderId, DateTime CreatedAt);

// 2. 实现 Handler - 无需 try-catch！
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) : base(logger) { }

    // 只需编写业务逻辑，框架自动处理异常！
    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            throw new CatgaException("Amount must be positive");

        await SaveOrderAsync(request.OrderId, request.Amount, ct);
        return new OrderResult(request.OrderId, DateTime.UtcNow);
    }
}

// 3. 配置服务（Program.cs）
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddCatga()                    // 添加 Catga 核心
    .UseMemoryPack()               // 使用 MemoryPack 序列化
    .ForDevelopment();             // 开发模式

builder.Services.AddInMemoryTransport();  // 内存传输层

// 4. 自动注册所有 Handler（Source Generator）
builder.Services.AddGeneratedHandlers();   // 🎉 零配置，自动发现所有 Handler！

var app = builder.Build();

// 5. 使用 Mediator
app.MapPost("/orders", async (CreateOrder cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

app.Run();
```

**就这么简单！** 无需手动注册 Handler，无需 try-catch，框架自动处理一切。

---

## 🎯 核心特性

### 1. SafeRequestHandler - 零异常处理

传统方式需要大量 try-catch：

```csharp
// ❌ 传统方式：充满 try-catch
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

**Catga 方式**：框架自动处理异常

```csharp
// ✅ Catga 方式：零 try-catch
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request,
        CancellationToken ct)
    {
        // 直接抛出异常，框架自动转换为 CatgaResult.Failure
        if (!await _inventory.CheckStockAsync(request.Items, ct))
            throw new CatgaException("Insufficient stock");

        var order = await _repository.SaveAsync(...);
        return new OrderResult(order.Id, order.CreatedAt);
    }
}
```

### 2. 自定义错误处理和自动回滚

可以 override 虚方法实现自定义错误处理和自动回滚：

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    private string? _orderId;
    private bool _inventorySaved;

    protected override async Task<OrderResult> HandleCoreAsync(...)
    {
        // 1. 保存订单
        _orderId = await _repository.SaveAsync(...);

        // 2. 预留库存
        await _inventory.ReserveAsync(_orderId, ...);
        _inventorySaved = true;

        // 3. 处理支付（可能失败）
        if (!await _payment.ValidateAsync(...))
            throw new CatgaException("Payment validation failed");

        return new OrderResult(_orderId, DateTime.UtcNow);
    }

    // 自定义错误处理：自动回滚
    protected override async Task<CatgaResult<OrderResult>> OnBusinessErrorAsync(
        CreateOrder request,
        CatgaException exception,
        CancellationToken ct)
    {
        Logger.LogWarning("Order creation failed, rolling back...");

        // 反向回滚
        if (_inventorySaved && _orderId != null)
            await _inventory.ReleaseAsync(_orderId, ...);
        if (_orderId != null)
            await _repository.DeleteAsync(_orderId, ...);

        return CatgaResult<OrderResult>.Failure(
            $"Order creation failed: {exception.Message}. All changes rolled back.",
            exception);
    }
}
```

**完整示例**：查看 [OrderSystem.Api](./examples/OrderSystem.Api/Handlers/OrderCommandHandlers.cs)

### 3. Source Generator - 零反射，零配置

```csharp
// 自动注册所有 Handler
builder.Services.AddGeneratedHandlers();   // 发现所有 IRequestHandler, IEventHandler

// 自动注册所有服务
builder.Services.AddGeneratedServices();   // 发现所有 [CatgaService] 标记的服务

// 服务定义（自动注册）
[CatgaService(ServiceLifetime.Scoped, ServiceType = typeof(IOrderRepository))]
public class OrderRepository : IOrderRepository
{
    // 实现...
}
```

**生成的代码** 在编译时创建，零运行时开销，100% AOT 兼容。

### 4. 事件驱动架构

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

### 5. OpenTelemetry + Jaeger 原生集成

Catga 深度集成 OpenTelemetry 和 Jaeger，提供完整的分布式追踪：

```csharp
// ServiceDefaults（自动配置）
builder.AddServiceDefaults();  // 自动启用 OpenTelemetry

// 所有 Command/Event 自动追踪
await _mediator.SendAsync<CreateOrder, OrderResult>(cmd);
// ↓ 自动创建 Activity Span
// ↓ 设置 catga.type, catga.request.type, catga.correlation_id
// ↓ 记录成功/失败和执行时间

// 在 Jaeger UI 中搜索
// Tags: catga.type = command
// Tags: catga.correlation_id = {your-id}
```

**功能**：
- 🔗 **跨服务链路传播** - A → HTTP → B 自动接续
- 🏷️ **丰富的 Tags** - catga.type, catga.request.type, catga.correlation_id
- 📊 **Metrics 集成** - Prometheus/Grafana 直接可用
- 🎯 **零配置** - ServiceDefaults 一行搞定

详见：[分布式追踪指南](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md) | [Jaeger 完整指南](./docs/observability/JAEGER-COMPLETE-GUIDE.md)

### 6. .NET Aspire 集成

```csharp
// AppHost
var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.OrderSystem_Api>("api")
    .WithReplicas(3);  // 自动负载均衡

builder.AddProject<Projects.Dashboard>("dashboard")
    .WithReference(api);

builder.Build().Run();

// API Service
builder.AddServiceDefaults();  // OpenTelemetry, Health Checks, Service Discovery
builder.Services.AddCatga().UseMemoryPack().ForDevelopment();
app.MapDefaultEndpoints();     // /health, /alive, /ready
```

---

## 📦 NuGet 包

| 包名 | 用途 | AOT |
|------|------|-----|
| `Catga` | 核心框架 | ✅ |
| `Catga.InMemory` | 内存实现（开发） | ✅ |
| `Catga.SourceGenerator` | 源生成器 | ✅ |
| `Catga.Serialization.MemoryPack` | MemoryPack 序列化 | ✅ |
| `Catga.Serialization.Json` | JSON 序列化 | ⚠️ |
| `Catga.Transport.Nats` | NATS 传输 | ✅ |
| `Catga.Persistence.Redis` | Redis 持久化 | ✅ |
| `Catga.AspNetCore` | ASP.NET Core 集成 | ✅ |

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
curl http://localhost:5000/demo/compare
```

**关键流程**：

```csharp
// 成功流程
POST /demo/order-success
→ 检查库存 → 保存订单 → 预留库存 → 验证支付 → 发布事件
→ ✅ 订单创建成功

// 失败流程（自动回滚）
POST /demo/order-failure (PaymentMethod = "FAIL-CreditCard")
→ 检查库存 → 保存订单 → 预留库存 → 验证支付失败！
→ 触发 OnBusinessErrorAsync
→ 🔄 回滚：释放库存
→ 🔄 回滚：删除订单
→ 📢 发布 OrderFailedEvent
→ ❌ 所有变更已回滚
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

完整报告：[性能基准文档](./docs/PERFORMANCE-REPORT.md) | [测试结果](./docs/BENCHMARK-RESULTS.md)

---

## 📚 文档

### 快速入门
- [快速开始](./docs/QUICK-START.md) - 5 分钟上手
- [Quick Reference](./docs/QUICK-REFERENCE.md) - API 速查
- [完整文档索引](./docs/INDEX.md)

### 核心概念
- [消息定义](./docs/api/messages.md) - IRequest, IEvent
- [Mediator API](./docs/api/mediator.md) - ICatgaMediator
- [自定义错误处理](./docs/guides/custom-error-handling.md) - SafeRequestHandler
- [Source Generator](./docs/guides/source-generator.md) - 自动注册

### 可观测性
- [分布式追踪指南](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md) - 跨服务链路
- [Jaeger 完整指南](./docs/observability/JAEGER-COMPLETE-GUIDE.md) - 搜索技巧
- [监控指南](./docs/production/MONITORING-GUIDE.md) - Prometheus/Grafana

### 高级功能
- [分布式事务](./docs/patterns/DISTRIBUTED-TRANSACTION-V2.md) - Catga Pattern
- [.NET Aspire 集成](./docs/guides/debugger-aspire-integration.md)
- [AOT 序列化指南](./docs/aot/serialization-aot-guide.md)

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
