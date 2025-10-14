# Catga 快速参考

> **5 分钟**从零到上手 Catga 🚀
> 适合：已了解 CQRS 基础概念，需要快速查询 API 的开发者

[返回主文档](./README.md) · [完整教程](./docs/examples/basic-usage.md) · [架构设计](./docs/architecture/ARCHITECTURE.md)

---

## ⚡ 最简配置（仅需 3 行！）

```csharp
using Catga.DependencyInjection;

services.AddCatga()
    .UseMemoryPack()      // 100% AOT 兼容，推荐
    .ForProduction();     // 日志+追踪+幂等性+重试+验证
```

**就这么简单！** Handler 自动发现，无需手动注册。

---

## 📦 安装

### 核心包（必需）

```bash
# 核心 + MemoryPack序列化（推荐）
dotnet add package Catga.InMemory
dotnet add package Catga.Serialization.MemoryPack
dotnet add package Catga.SourceGenerator

# 或使用 JSON 序列化
dotnet add package Catga.Serialization.Json
```

### 可选包

```bash
# ASP.NET Core 集成
dotnet add package Catga.AspNetCore

# NATS 传输层
dotnet add package Catga.Transport.Nats

# Redis 持久化
dotnet add package Catga.Persistence.Redis
```

---

## 🎯 消息定义

### Command - 有返回值，修改状态

```csharp
[MemoryPackable]  // ← AOT 必需！分析器会提示
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(string OrderId, bool Success);
```

### Query - 有返回值，只读

```csharp
[MemoryPackable]
public partial record GetOrder(string OrderId) : IRequest<Order?>;

[MemoryPackable]
public partial record Order(string Id, string UserId, decimal Amount);
```

### Event - 无返回值，通知

```csharp
[MemoryPackable]
public partial record OrderCreated(string OrderId, DateTime CreatedAt) : IEvent;
```

**关键点**:
- ✅ `[MemoryPackable]` - MemoryPack 必需
- ✅ `partial` - Source Generator 必需
- ✅ `record` - 推荐（不可变）
- ✅ 继承 `IRequest<TResponse>` 或 `IEvent`

---

## 🛠️ Handler 实现

### Request Handler (Command/Query)

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    private readonly IOrderRepository _repo;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(IOrderRepository repo, ILogger<CreateOrderHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 业务逻辑
            var order = new Order(request.OrderId, "user-123", request.Amount);
            await _repo.SaveAsync(order, cancellationToken);

            _logger.LogInformation("Order {OrderId} created", request.OrderId);

            return CatgaResult<OrderResult>.Success(
                new OrderResult(request.OrderId, Success: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order {OrderId}", request.OrderId);
            return CatgaResult<OrderResult>.Failure("Order creation failed", ex);
        }
    }
}
```

### Event Handler

```csharp
public class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    private readonly IEmailService _emailService;

    public OrderCreatedHandler(IEmailService emailService)
        => _emailService = emailService;

    public async Task HandleAsync(OrderCreated @event, CancellationToken cancellationToken = default)
    {
        // 发送通知、更新缓存等
        await _emailService.SendOrderConfirmationAsync(@event.OrderId, cancellationToken);
    }
}
```

**关键点**:
- ✅ 实现 `IRequestHandler<TRequest, TResponse>` 或 `IEventHandler<TEvent>`
- ✅ 使用 `ValueTask` (Command/Query) 或 `Task` (Event)
- ✅ 返回 `CatgaResult<T>` 而非直接返回 `T`
- ✅ Handler 自动注册，无需手动 `services.AddTransient`

---

## ⚙️ 配置

### 环境预设（推荐）

```csharp
// 🏭 生产环境 - 所有功能启用
services.AddCatga()
    .UseMemoryPack()
    .ForProduction();
    // ✅ 日志、追踪、幂等性、重试、验证、DLQ

// 🔧 开发环境 - 详细日志
services.AddCatga()
    .UseMemoryPack()
    .ForDevelopment();
    // ✅ 详细日志、追踪，❌ 幂等性（便于调试）

// ⚡ 高性能场景 - 最小开销
services.AddCatga()
    .UseMemoryPack()
    .ForHighPerformance();
    // ❌ 日志、追踪，✅ 核心功能

// 🎯 最小化 - 极致轻量
services.AddCatga()
    .UseMemoryPack()
    .Minimal();
    // ❌ 所有可选功能
```

### 精细控制

```csharp
services.AddCatga()
    .UseMemoryPack()
    .WithLogging(enabled: true)                 // 结构化日志
    .WithTracing(enabled: true)                 // 分布式追踪
    .WithIdempotency(                           // 幂等性
        enabled: true,
        retentionHours: 24)
    .WithRetry(                                 // 重试
        enabled: true,
        maxAttempts: 3)
    .WithValidation(enabled: true)              // 验证
    .WithDeadLetterQueue(                       // 死信队列
        enabled: true,
        maxSize: 1000);
```

---

## 🚀 使用

### 发送 Command/Query

```csharp
public class OrderService
{
    private readonly ICatgaMediator _mediator;

    public OrderService(ICatgaMediator mediator) => _mediator = mediator;

    // Command
    public async Task<OrderResult> CreateOrderAsync(string orderId, decimal amount)
    {
        var result = await _mediator.SendAsync<CreateOrder, OrderResult>(
            new CreateOrder(orderId, amount));

        if (!result.IsSuccess)
            throw new Exception(result.Error);

        return result.Value!;
    }

    // Query
    public async Task<Order?> GetOrderAsync(string orderId)
    {
        var result = await _mediator.SendAsync<GetOrder, Order?>(
            new GetOrder(orderId));

        return result.IsSuccess ? result.Value : null;
    }
}
```

### 发布 Event

```csharp
public class OrderService
{
    private readonly ICatgaMediator _mediator;

    public async Task NotifyOrderCreatedAsync(string orderId)
    {
        // Fire-and-forget
        await _mediator.PublishAsync(new OrderCreated(orderId, DateTime.UtcNow));
    }
}
```

### Result 处理

```csharp
// 创建 Success
return CatgaResult<OrderResult>.Success(result);
return CatgaResult<OrderResult>.Success(result, metadata: new Dictionary<string, string>
{
    ["TraceId"] = Activity.Current?.Id
});

// 创建 Failure
return CatgaResult<OrderResult>.Failure("Order not found");
return CatgaResult<OrderResult>.Failure("Database error", exception);

// 检查结果
if (result.IsSuccess)
{
    var value = result.Value;       // TResponse
    var metadata = result.Metadata; // Dictionary<string, string>?
}
else
{
    var error = result.Error;       // string
    var exception = result.Exception; // Exception?
}
```

---

## 🔥 序列化器选择

### MemoryPack (推荐 - 100% AOT)

```csharp
// 安装
dotnet add package Catga.Serialization.MemoryPack
dotnet add package MemoryPack
dotnet add package MemoryPack.Generator

// 配置
services.AddCatga().UseMemoryPack();

// 标注消息
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;
```

**优势**: ✅ 100% AOT · ✅ 5x 性能 · ✅ 40% 更小 · ✅ 零反射

### JSON (可选)

```csharp
// 安装
dotnet add package Catga.Serialization.Json

// 默认配置（不推荐 AOT）
services.AddCatga().UseJson();

// AOT 配置（推荐）
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(OrderResult))]
public partial class AppJsonContext : JsonSerializerContext { }

services.AddCatga().UseJson(new JsonSerializerOptions
{
    TypeInfoResolver = AppJsonContext.Default
});
```

**优势**: ✅ 人类可读 · ⚠️ 需配置 AOT

详细对比: [序列化指南](./docs/guides/serialization.md)

---

## 🌐 分布式

### NATS Transport

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseNatsTransport(options =>
    {
        options.Url = "nats://nats:4222";      // K8s Service 名称
        options.SubjectPrefix = "catga.";
    });
```

### Redis Persistence

```csharp
// Outbox
services.AddRedisOutboxPersistence(options =>
{
    options.ConnectionString = "redis:6379";
    options.KeyPrefix = "outbox:";
});

// Inbox
services.AddRedisInboxPersistence(options =>
{
    options.ConnectionString = "redis:6379";
    options.KeyPrefix = "inbox:";
});

// Cache
services.AddRedisDistributedCache();
```

---

## 🎨 ASP.NET Core

### 基本集成

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services.AddCatga()
    .UseMemoryPack()
    .ForProduction();

// 添加 ASP.NET Core 集成
builder.Services.AddCatgaAspNetCore(options =>
{
    options.EnableDashboard = true;
    options.DashboardPathPrefix = "/catga";
});

var app = builder.Build();

// 映射自动端点
app.MapCatgaEndpoints();

app.Run();
```

### 生成的端点

- `POST /catga/command/{Name}` - Send Command
- `POST /catga/query/{Name}` - Send Query
- `POST /catga/event/{Name}` - Publish Event
- `GET /catga/health` - Health check
- `GET /catga/nodes` - Node list

### 自定义端点

```csharp
app.MapPost("/api/orders", async (
    CreateOrder command,
    ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(command);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error);
})
.WithCatgaCommandMetadata<CreateOrder, OrderResult>()
.WithOpenApi();
```

---

## 📊 可观测性

### OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(CatgaDiagnostics.ActivitySourceName)  // Catga 追踪
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(CatgaDiagnostics.MeterName)            // Catga 指标
        .AddPrometheusExporter());
```

### 内置指标

| 指标 | 类型 | 描述 |
|------|------|------|
| `catga.messages.published` | Counter | 发布的消息数 |
| `catga.messages.failed` | Counter | 失败的消息数 |
| `catga.commands.executed` | Counter | 执行的命令数 |
| `catga.message.duration` | Histogram | 消息处理耗时 (ms) |
| `catga.messages.active` | ObservableGauge | 活跃消息数 |

### 结构化日志

```csharp
// LoggerMessage 自动生成，零分配
// 在 Handler 中直接使用 ILogger
_logger.LogInformation("Order {OrderId} created", orderId);

// Catga 自动记录
// - Command 执行开始/结束
// - Event 发布
// - Pipeline 执行
// - 错误和异常
```

---

## 🛠️ Pipeline Behaviors

### 自定义 Behavior

```csharp
public class ValidationBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest>? _validator;

    public ValidationBehavior(
        ILogger<ValidationBehavior<TRequest, TResponse>> logger,
        IValidator<TRequest>? validator = null)
        : base(logger)
    {
        _validator = validator;
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (_validator != null)
        {
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                return CatgaResult<TResponse>.Failure($"Validation failed: {errors}");
            }
        }

        return await next();
    }
}

// 注册
services.AddCatga()
    .UseMemoryPack()
    .Configure(options =>
    {
        options.EnableValidation = true;  // 启用内置验证 Behavior
    });

// 或手动添加
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

### 内置 Behaviors

| Behavior | 功能 | 启用方式 |
|----------|------|---------|
| `LoggingBehavior` | 结构化日志 | `.WithLogging()` |
| `TracingBehavior` | 分布式追踪 | `.WithTracing()` |
| `IdempotencyBehavior` | 幂等性保证 | `.WithIdempotency()` |
| `RetryBehavior` | 自动重试 | `.WithRetry()` |
| `ValidationBehavior` | 数据验证 | `.WithValidation()` |

---

## 📋 消息属性

### IMessage 接口

```csharp
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>, IMessage
{
    // 消息 ID（幂等性）
    public string MessageId { get; init; } = Guid.NewGuid().ToString();

    // 关联 ID（分布式追踪）
    public string? CorrelationId { get; init; }

    // QoS 级别
    public QualityOfService QoS { get; init; } = QualityOfService.AtLeastOnce;

    // 投递模式
    public DeliveryMode DeliveryMode { get; init; } = DeliveryMode.WaitForResult;
}
```

### QoS 级别

| 级别 | 描述 | 适用场景 |
|------|------|---------|
| `AtMostOnce` | 最多一次，不重试 | 非关键通知 |
| `AtLeastOnce` | 至少一次，会重试 | 大多数场景（默认） |
| `ExactlyOnce` | 精确一次，幂等性 | 支付、订单等关键操作 |

### Delivery Mode

| 模式 | 描述 | 适用场景 |
|------|------|---------|
| `WaitForResult` | 等待处理完成 | 需要结果的场景（默认） |
| `AsyncRetry` | 异步重试 | 可接受延迟的场景 |

---

## 🧪 测试

### 单元测试

```csharp
using Xunit;
using Microsoft.Extensions.DependencyInjection;

public class CreateOrderHandlerTests
{
    [Fact]
    public async Task CreateOrder_Should_ReturnSuccess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga()
            .UseMemoryPack()
            .Minimal();  // 最小化配置，便于测试

        services.AddTransient<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
        services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<CreateOrder, OrderResult>(
            new CreateOrder("ORD-001", 99.99m));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ORD-001", result.Value!.OrderId);
        Assert.True(result.Value.Success);
    }
}
```

---

## 🔍 调试

### 启用详细日志

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Catga", LogLevel.Trace);
```

### Activity 标签

```csharp
using var activity = CatgaDiagnostics.ActivitySource.StartActivity("MyOperation");
activity?.SetTag("order_id", orderId);
activity?.SetTag("amount", amount);
activity?.SetStatus(ActivityStatusCode.Ok);
```

---

## 🚀 Native AOT 发布

### 项目配置

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Catga.InMemory" />
    <PackageReference Include="Catga.Serialization.MemoryPack" />
    <PackageReference Include="Catga.SourceGenerator" />
    <PackageReference Include="MemoryPack" />
    <PackageReference Include="MemoryPack.Generator" />
  </ItemGroup>
</Project>
```

### 发布命令

```bash
# Windows
dotnet publish -c Release -r win-x64 --property:PublishAot=true

# Linux
dotnet publish -c Release -r linux-x64 --property:PublishAot=true

# macOS
dotnet publish -c Release -r osx-arm64 --property:PublishAot=true
```

### 验证

```bash
# 启动时间测试
time ./bin/Release/net9.0/linux-x64/publish/YourApp

# 二进制大小
ls -lh ./bin/Release/net9.0/linux-x64/publish/YourApp

# 内存占用
ps aux | grep YourApp
```

---

## 💡 常见模式

### 幂等性模式

```csharp
// 使用 MessageId 确保幂等性
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

// Catga 自动检查和缓存结果
var result1 = await mediator.SendAsync<CreateOrder, OrderResult>(command);
var result2 = await mediator.SendAsync<CreateOrder, OrderResult>(command); // 返回缓存结果
```

### 事件驱动模式

```csharp
// Command Handler 发布 Event
public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
    CreateOrder request, CancellationToken ct = default)
{
    // 1. 处理 Command
    var order = CreateOrderLogic(request);

    // 2. 发布 Event
    await _mediator.PublishAsync(new OrderCreated(order.Id, DateTime.UtcNow));

    return CatgaResult<OrderResult>.Success(new OrderResult(order.Id, true));
}

// 多个 Event Handler 可以订阅同一个 Event
public class OrderCreatedEmailHandler : IEventHandler<OrderCreated> { }
public class OrderCreatedCacheHandler : IEventHandler<OrderCreated> { }
public class OrderCreatedAnalyticsHandler : IEventHandler<OrderCreated> { }
```

---

## 📚 更多资源

- **[完整文档](./README.md#-文档)** - 所有文档索引
- **[示例项目](./examples/)** - 完整的示例代码
- **[架构设计](./docs/architecture/ARCHITECTURE.md)** - 深入理解架构
- **[性能基准](./benchmarks/Catga.Benchmarks/)** - 详细的性能数据
- **[贡献指南](./CONTRIBUTING.md)** - 如何贡献代码

---

<div align="center">

[返回主文档](./README.md) · [查看示例](./examples/) · [架构设计](./docs/architecture/ARCHITECTURE.md)

**Happy coding with Catga!** 🚀

</div>
