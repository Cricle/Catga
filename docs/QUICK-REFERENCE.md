# Catga Quick Reference

快速 API 参考，方便查找常用代码片段。

---

## 📦 安装

```bash
# 核心包
dotnet add package Catga
dotnet add package Catga.InMemory

# 序列化（选择一个）
dotnet add package Catga.Serialization.MemoryPack  # AOT 兼容（推荐）
dotnet add package Catga.Serialization.Json        # 开发友好

# Source Generator
dotnet add package Catga.SourceGenerator

# ASP.NET Core
dotnet add package Catga.AspNetCore

# 调试器（可选）
dotnet add package Catga.Debugger
dotnet add package Catga.Debugger.AspNetCore
```

---

## 🚀 基础配置

### Program.cs

```csharp
using Catga;

var builder = WebApplication.CreateBuilder(args);

// 1. 配置 Catga
builder.Services
    .AddCatga()                     // 核心服务
    .UseMemoryPack()                // 序列化器
    .ForDevelopment();              // 开发模式

// 2. 传输层
builder.Services.AddInMemoryTransport();  // 内存（开发）
// builder.Services.AddNatsTransport();   // NATS（生产）

// 3. 自动注册（Source Generator）
builder.Services.AddGeneratedHandlers();   // 所有 Handler
builder.Services.AddGeneratedServices();   // 所有 [CatgaService]

var app = builder.Build();
app.Run();
```

---

## 📝 消息定义

### 命令（有返回值）

```csharp
using Catga.Messages;
using MemoryPack;

// 命令
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId,
    decimal Amount
) : IRequest<OrderResult>;

// 结果
[MemoryPackable]
public partial record OrderResult(
    string OrderId,
    DateTime CreatedAt
);
```

### 命令（无返回值）

```csharp
[MemoryPackable]
public partial record SendEmailCommand(
    string To,
    string Subject,
    string Body
) : IRequest;  // 无泛型参数
```

### 事件（通知）

```csharp
[MemoryPackable]
public partial record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal Amount,
    DateTime CreatedAt
) : IEvent;
```

---

## 🎯 Handler 实现

### SafeRequestHandler（推荐）

```csharp
using Catga;
using Catga.Core;
using Catga.Exceptions;

public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly IOrderRepository _repository;
    private readonly ICatgaMediator _mediator;

    public CreateOrderHandler(
        IOrderRepository repository,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger) : base(logger)
    {
        _repository = repository;
        _mediator = mediator;
    }

    // 无需 try-catch！
    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrderCommand request,
        CancellationToken ct)
    {
        // 验证（直接抛异常）
        if (request.Amount <= 0)
            throw new CatgaException("Amount must be positive");

        // 业务逻辑
        var orderId = Guid.NewGuid().ToString("N");
        await _repository.SaveAsync(orderId, request.Amount, ct);

        // 发布事件
        await _mediator.PublishAsync(new OrderCreatedEvent(
            orderId,
            request.CustomerId,
            request.Amount,
            DateTime.UtcNow
        ), ct);

        // 直接返回结果
        return new OrderResult(orderId, DateTime.UtcNow);
    }
}
```

### 无返回值 Handler

```csharp
public class SendEmailHandler : SafeRequestHandler<SendEmailCommand>
{
    public SendEmailHandler(ILogger<SendEmailHandler> logger) : base(logger) { }

    protected override async Task HandleCoreAsync(
        SendEmailCommand request,
        CancellationToken ct)
    {
        await _emailService.SendAsync(request.To, request.Subject, request.Body);
        Logger.LogInformation("Email sent to {To}", request.To);
    }
}
```

### 自定义错误处理（新功能）

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    private string? _orderId;
    private bool _inventoryReserved;

    protected override async Task<OrderResult> HandleCoreAsync(...)
    {
        // 保存订单
        _orderId = await _repository.SaveAsync(...);

        // 预留库存
        await _inventory.ReserveAsync(_orderId, ...);
        _inventoryReserved = true;

        // 处理支付（可能失败）
        if (!await _payment.ValidateAsync(...))
            throw new CatgaException("Payment failed");

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
        if (_inventoryReserved && _orderId != null)
            await _inventory.ReleaseAsync(_orderId, ...);
        if (_orderId != null)
            await _repository.DeleteAsync(_orderId, ...);

        // 返回详细错误
        var metadata = new ResultMetadata();
        metadata.Add("OrderId", _orderId ?? "N/A");
        metadata.Add("RollbackCompleted", "true");

        return new CatgaResult<OrderResult>
        {
            IsSuccess = false,
            Error = $"Order creation failed: {exception.Message}. All changes rolled back.",
            Metadata = metadata
        };
    }
}
```

### 事件 Handler

```csharp
using Catga.Handlers;

public class OrderCreatedEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public OrderCreatedEmailHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        await _emailService.SendOrderConfirmationAsync(@event.OrderId);
    }
}
```

---

## 🔧 服务注册

### 自动注册（推荐）

```csharp
// 使用 [CatgaService] 属性
[CatgaService(ServiceLifetime.Scoped, ServiceType = typeof(IOrderRepository))]
public class OrderRepository : IOrderRepository
{
    // 实现...
}

// Program.cs
builder.Services.AddGeneratedServices();  // 自动注册所有 [CatgaService]
```

### 手动注册

```csharp
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
```

---

## 🌐 ASP.NET Core 集成

### Minimal API

```csharp
using Catga.AspNetCore;

var app = builder.Build();

// 方式 1: 使用扩展方法（推荐）
app.MapCatgaRequest<CreateOrderCommand, OrderResult>("/api/orders")
    .WithName("CreateOrder")
    .WithTags("Orders");

// 方式 2: 手动注入 Mediator
app.MapPost("/api/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// 查询
app.MapGet("/api/orders/{id}", async (string id, IOrderRepository repo) =>
{
    var order = await repo.GetByIdAsync(id);
    return order != null ? Results.Ok(order) : Results.NotFound();
});
```

### 控制器

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrdersController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        if (result.IsSuccess)
            return Ok(result.Value);

        return BadRequest(new { error = result.Error });
    }
}
```

---

## 🔍 使用 Mediator

### 发送命令

```csharp
// 有返回值
var result = await mediator.SendAsync<CreateOrder, OrderResult>(command);
if (result.IsSuccess)
{
    var orderResult = result.Value;
    Console.WriteLine($"Order created: {orderResult.OrderId}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}

// 无返回值
var result = await mediator.SendAsync(new SendEmailCommand(...));
```

### 发布事件

```csharp
// 自动调用所有 IEventHandler<OrderCreatedEvent>
await mediator.PublishAsync(new OrderCreatedEvent(
    orderId,
    customerId,
    amount,
    DateTime.UtcNow
));
```

---

## 🐛 调试器

### 基础配置

```csharp
// Program.cs
if (builder.Environment.IsDevelopment())
{
    // 添加调试器服务
    builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
    {
        options.Mode = DebuggerMode.Development;
        options.SamplingRate = 1.0;  // 100% 采样
        options.CaptureVariables = true;
        options.CaptureCallStacks = true;
    });

    // ... 构建 app

    // 映射调试界面
    app.MapCatgaDebugger("/debug");  // http://localhost:5000/debug
}
```

### 消息捕获（Source Generator）

```csharp
using Catga.Debugger.Core;

[MemoryPackable]
[GenerateDebugCapture]  // 自动生成 AOT 兼容的变量捕获
public partial record CreateOrderCommand(
    string CustomerId,
    decimal Amount
) : IRequest<OrderResult>;
```

---

## 🚀 分布式配置

### NATS 传输

```csharp
builder.Services.AddNatsTransport(options =>
{
    options.Url = "nats://localhost:4222";
    options.ConnectionPoolSize = 10;
});
```

### Redis 持久化

```csharp
builder.Services.AddRedisStores(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "MyApp:";
});
```

---

## 🎨 .NET Aspire

### AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var nats = builder.AddNats("nats");
var redis = builder.AddRedis("redis");

var api = builder.AddProject<Projects.OrderSystem_Api>("api")
    .WithReference(nats)
    .WithReference(redis)
    .WithReplicas(3);

builder.Build().Run();
```

### Service

```csharp
var builder = WebApplication.CreateBuilder(args);

// Aspire Service Defaults
builder.AddServiceDefaults();  // OpenTelemetry, Health Checks, Service Discovery

// Catga
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForProduction();

builder.Services.AddNatsTransport();  // 自动从 Aspire 获取配置

var app = builder.Build();
app.MapDefaultEndpoints();  // /health, /alive, /ready
app.Run();
```

---

## 📊 错误处理

### CatgaException

```csharp
// 业务异常（自动转换为 CatgaResult.Failure）
throw new CatgaException("Order not found");
throw new CatgaException("Insufficient stock", innerException);
```

### CatgaResult

```csharp
// 成功
var success = CatgaResult<OrderResult>.Success(new OrderResult(...));

// 失败
var failure = CatgaResult<OrderResult>.Failure("Order validation failed");

// 检查结果
if (result.IsSuccess)
{
    var value = result.Value;
}
else
{
    var error = result.Error;
    var exception = result.Exception;
    var metadata = result.Metadata;
}
```

### ResultMetadata

```csharp
var metadata = new ResultMetadata();
metadata.Add("OrderId", orderId);
metadata.Add("Timestamp", DateTime.UtcNow.ToString("O"));
metadata.Add("UserAction", "CreateOrder");

var result = new CatgaResult<OrderResult>
{
    IsSuccess = false,
    Error = "Failed",
    Metadata = metadata
};

// 读取
var allMetadata = result.Metadata?.GetAll();
var orderId = result.Metadata?.Get("OrderId");
```

---

## 🔒 生产配置

### 最小配置

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()           // AOT 兼容
    .ForProduction();          // 生产模式

builder.Services.AddNatsTransport(options =>
{
    options.Url = builder.Configuration["Nats:Url"];
    options.MaxReconnectAttempts = 10;
});

builder.Services.AddRedisStores(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
});
```

### 优雅关闭

```csharp
builder.Services.AddCatgaBuilder(b => b.UseGracefulLifecycle());

// 框架自动处理 SIGTERM 信号
```

---

## 📖 更多资源

- [完整文档](./INDEX.md)
- [快速开始](./QUICK-START.md)
- [OrderSystem 示例](../examples/OrderSystem.Api/)
- [性能报告](./PERFORMANCE-REPORT.md)

---

**常用模式都在这里！保存此页面以便快速查找。** ⭐
