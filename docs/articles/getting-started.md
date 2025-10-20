# 快速开始

本指南将帮助你在 5 分钟内上手 Catga。

---

## 📦 安装

### 1. 安装核心包

```bash
dotnet add package Catga
dotnet add package Catga.Serialization.Json
dotnet add package Catga.Transport.InMemory
```

### 2. 可选包

**生产环境传输层**:
```bash
dotnet add package Catga.Transport.Redis
dotnet add package Catga.Transport.Nats
```

**生产环境持久化**:
```bash
dotnet add package Catga.Persistence.Redis
dotnet add package Catga.Persistence.Nats
```

**高性能序列化**:
```bash
dotnet add package Catga.Serialization.MemoryPack
```

---

## 🚀 2 行代码启动

### ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册 Catga（2 行）
builder.Services.AddCatga()
    .UseJsonSerializer()
    .UseInMemoryTransport();

var app = builder.Build();
app.Run();
```

### 控制台应用

```csharp
var services = new ServiceCollection();

services.AddCatga()
    .UseJsonSerializer()
    .UseInMemoryTransport();

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<ICatgaMediator>();
```

---

## 📝 定义消息

### Command (命令)

```csharp
using Catga.Abstractions;

public record CreateOrderCommand(
    string CustomerId,
    List<OrderItem> Items,
    string ShippingAddress
) : IRequest<OrderCreatedResult>;

public record OrderItem(string ProductId, int Quantity, decimal Price);
```

### Result (结果)

```csharp
public record OrderCreatedResult(
    long OrderId,
    decimal TotalAmount,
    DateTime CreatedAt
);
```

### Event (事件)

```csharp
public record OrderCreatedEvent(
    long OrderId,
    string CustomerId,
    decimal TotalAmount
) : IEvent;
```

### Query (查询)

```csharp
public record GetOrderQuery(long OrderId) : IRequest<Order?>;

public record Order(
    long Id,
    string CustomerId,
    decimal TotalAmount,
    string Status
);
```

---

## 🛠️ 实现 Handler

### Command Handler

```csharp
using Catga.Abstractions;
using Catga.Core;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly ICatgaMediator _mediator;
    private readonly IOrderRepository _repository;

    public CreateOrderHandler(ICatgaMediator mediator, IOrderRepository repository)
    {
        _mediator = mediator;
        _repository = repository;
    }

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 验证
        if (request.Items.Count == 0)
            return CatgaResult<OrderCreatedResult>.Failure(
                "订单不能为空", 
                ErrorCodes.ValidationFailed);

        // 2. 业务逻辑
        var orderId = GenerateOrderId();
        var totalAmount = request.Items.Sum(x => x.Price * x.Quantity);
        
        var order = new Order
        {
            Id = orderId,
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = totalAmount,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.SaveAsync(order, cancellationToken);

        // 3. 发布事件
        await _mediator.PublishAsync(
            new OrderCreatedEvent(orderId, request.CustomerId, totalAmount),
            cancellationToken);

        // 4. 返回结果
        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(orderId, totalAmount, order.CreatedAt));
    }

    private long GenerateOrderId()
    {
        // 使用 Snowflake ID 生成器
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
```

### Query Handler

```csharp
public class GetOrderHandler : IRequestHandler<GetOrderQuery, Order?>
{
    private readonly IOrderRepository _repository;

    public GetOrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<CatgaResult<Order?>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        return CatgaResult<Order?>.Success(order);
    }
}
```

### Event Handler

```csharp
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "订单 {OrderId} 已创建，客户 {CustomerId}，金额 {Amount}",
            @event.OrderId, @event.CustomerId, @event.TotalAmount);

        // 发送通知、更新库存等
        await Task.CompletedTask;
    }
}
```

---

## 🌐 在 ASP.NET Core 中使用

### Minimal API

```csharp
using Catga;
using Catga.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatga()
    .UseJsonSerializer()
    .UseInMemoryTransport();

var app = builder.Build();

// Command - 创建订单
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand command) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);
    return result.ToHttpResult(); // 自动转换为 HTTP 响应
});

// Query - 查询订单
app.MapGet("/orders/{id:long}", async (ICatgaMediator mediator, long id) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, Order?>(new GetOrderQuery(id));
    return result.ToHttpResult();
});

// Event - 发布事件
app.MapPost("/events/order-created", async (ICatgaMediator mediator, OrderCreatedEvent @event) =>
{
    await mediator.PublishAsync(@event);
    return Results.Ok();
});

app.Run();
```

### Controller

```csharp
using Catga;
using Catga.AspNetCore;
using Microsoft.AspNetCore.Mvc;

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
        var result = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);
        return result.ToActionResult(); // 转换为 ActionResult
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(long id)
    {
        var result = await _mediator.SendAsync<GetOrderQuery, Order?>(new GetOrderQuery(id));
        return result.ToActionResult();
    }
}
```

---

## ✅ 错误处理

### 返回失败结果

```csharp
// 简单错误
if (order == null)
    return CatgaResult<Order?>.Failure("订单不存在");

// 带错误码
if (!IsValidCustomer(request.CustomerId))
    return CatgaResult<OrderCreatedResult>.Failure(
        "客户不存在",
        ErrorCodes.ValidationFailed);

// 使用 ErrorInfo
return CatgaResult<OrderCreatedResult>.Failure(
    ErrorInfo.Validation("手机号格式不正确"));
```

### 检查结果

```csharp
var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);

if (result.IsSuccess)
{
    var data = result.Value;
    Console.WriteLine($"订单创建成功: {data.OrderId}");
}
else
{
    Console.WriteLine($"错误: {result.Error}");
    Console.WriteLine($"错误码: {result.ErrorCode}");
}
```

### 转换为 HTTP 响应

```csharp
// 自动转换
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand command) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);
    
    // 自动根据 ErrorCode 返回正确的 HTTP 状态码
    return result.ToHttpResult();
    // ValidationFailed -> 422
    // HandlerFailed -> 400
    // PersistenceFailed -> 503
    // ...
});
```

---

## 🔧 配置选项

### 自定义配置

```csharp
builder.Services.AddCatga(options =>
{
    options.EnableAutoDiscovery = true;  // 自动发现 Handlers
    options.DefaultTimeout = TimeSpan.FromSeconds(30);
    options.EnableDetailedErrors = true; // 详细错误信息
});
```

### 添加 Behaviors

```csharp
builder.Services.AddCatga()
    .UseJsonSerializer()
    .UseInMemoryTransport()
    .AddLogging()           // 日志记录
    .AddValidation()        // 参数验证
    .AddDistributedTracing() // 分布式追踪
    .AddRetry();            // 重试逻辑
```

---

## 🚀 生产环境配置

### 使用 Redis

```csharp
builder.Services.AddCatga()
    .UseJsonSerializer()
    .UseRedisTransport(options =>
    {
        options.ConnectionString = "localhost:6379";
        options.DefaultChannel = "catga:events";
    })
    .AddRedisOutbox()  // Outbox 模式
    .AddRedisInbox();  // Inbox 模式
```

### 使用 NATS

```csharp
builder.Services.AddCatga()
    .UseMemoryPackSerializer() // 高性能序列化
    .UseNatsTransport(options =>
    {
        options.Url = "nats://localhost:4222";
        options.StreamName = "CATGA";
    })
    .AddNatsOutbox()
    .AddNatsInbox();
```

---

## 📚 下一步

- [架构设计](../architecture/ARCHITECTURE.md) - 了解内部设计
- [错误处理](../guides/error-handling.md) - 深入错误处理
- [分布式追踪](../observability/DISTRIBUTED-TRACING-GUIDE.md) - 集成 OpenTelemetry
- [内存优化](../guides/memory-optimization-guide.md) - 性能优化
- [示例项目](../../examples/OrderSystem.Api/) - 完整示例

---

<div align="center">

**开始构建你的 CQRS 应用吧！**

</div>
