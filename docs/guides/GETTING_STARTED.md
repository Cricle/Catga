# 🚀 Catga 快速开始指南

---

## 📦 安装

### **通过 NuGet 安装**
```bash
# 核心框架
dotnet add package Catga

# NATS 集成（推荐）
dotnet add package Catga.Nats

# Redis 集成（可选）
dotnet add package Catga.Redis

# 序列化器（选择一个）
dotnet add package Catga.Serialization.Json         # System.Text.Json
dotnet add package Catga.Serialization.MemoryPack  # 高性能二进制
```

---

## ⚡ 5分钟快速开始

### **1. 定义消息**
```csharp
// 命令（Command）
public record CreateOrderCommand(string OrderId, decimal Amount) : IRequest<OrderResult>;

// 查询（Query）
public record GetOrderQuery(string OrderId) : IRequest<OrderDto>;

// 事件（Event）
public record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

// 响应
public record OrderResult(string OrderId, bool Success);
public record OrderDto(string OrderId, decimal Amount, string Status);
```

### **2. 创建 Handler**
```csharp
// 命令处理器
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 业务逻辑
        var result = new OrderResult(request.OrderId, true);
        return CatgaResult<OrderResult>.Success(result);
    }
}

// 事件处理器
public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // 处理事件
        Console.WriteLine($"订单已创建: {@event.OrderId}");
        await Task.CompletedTask;
    }
}
```

### **3. 配置服务**

#### **开发环境（自动扫描）**
```csharp
var builder = WebApplication.CreateBuilder(args);

// 一行代码自动配置
builder.Services.AddCatgaDevelopment();

var app = builder.Build();
app.Run();
```

#### **生产环境（100% AOT 兼容）**
```csharp
using Catga.Serialization.MemoryPack;

var builder = WebApplication.CreateBuilder(args);

// 1. 序列化器
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();

// 2. 核心服务
builder.Services.AddCatga();

// 3. 手动注册 Handlers
builder.Services.AddRequestHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>();
builder.Services.AddEventHandler<OrderCreatedEvent, OrderCreatedHandler>();

// 4. NATS 分布式（可选）
builder.Services.AddNatsDistributed("nats://localhost:4222");

var app = builder.Build();
app.Run();
```

### **4. 使用 Mediator**
```csharp
public class OrderController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrderController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
    {
        var result = await _mediator.SendAsync(command);

        if (result.IsSuccess)
            return Ok(result.Value);

        return BadRequest(result.Error);
    }

    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrder(string id)
    {
        var query = new GetOrderQuery(id);
        var result = await _mediator.SendAsync(query);

        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }
}
```

---

## 🎯 常用场景

### **场景 1: 单机应用（内存模式）**
```csharp
builder.Services.AddCatga();
builder.Services.AddRequestHandler<TRequest, TResponse, THandler>();
```

### **场景 2: NATS 分布式**
```csharp
builder.Services.AddCatga();
builder.Services.AddNatsDistributed("nats://localhost:4222");
builder.Services.AddRequestHandler<TRequest, TResponse, THandler>();
```

### **场景 3: 消息可靠性（Outbox/Inbox）**
```csharp
builder.Services.AddCatga();
builder.Services.AddNatsDistributed("nats://localhost:4222");
builder.Services.AddNatsJetStreamStores(); // Outbox + Inbox + Idempotency
builder.Services.AddRequestHandler<TRequest, TResponse, THandler>();
```

### **场景 4: Redis 分布式存储**
```csharp
builder.Services.AddCatga();
builder.Services.AddRedis("localhost:6379");
builder.Services.AddRedisStores(); // Outbox + Inbox + Idempotency
builder.Services.AddRequestHandler<TRequest, TResponse, THandler>();
```

### **场景 5: 高性能批处理**
```csharp
// 批量发送
var commands = new[] { command1, command2, command3 };
var results = await _mediator.SendBatchAsync(commands);

// 流式处理
await foreach (var result in _mediator.SendStreamAsync(largeDataStream))
{
    // 处理每个结果
}
```

---

## 🔧 Pipeline Behaviors

### **启用 Pipeline 功能**
```csharp
builder.Services.AddCatgaBuilder(builder => builder
    .WithLogging()          // 日志
    .WithValidation()       // 验证
    .WithRetry()           // 重试
    .WithCircuitBreaker()  // 熔断
    .WithTracing()         // 追踪
    .WithOutbox()          // Outbox模式
    .WithInbox()           // Inbox模式
);
```

### **自定义 Behavior**
```csharp
public class CustomBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 前置处理
        Console.WriteLine($"处理请求: {typeof(TRequest).Name}");

        // 执行下一个 Behavior
        var result = await next();

        // 后置处理
        Console.WriteLine($"请求完成: {result.IsSuccess}");

        return result;
    }
}

// 注册
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CustomBehavior<,>));
```

---

## 📊 分布式事务（Saga）

```csharp
// 定义事务
public class OrderSaga : ICatGaTransaction<OrderSagaData>
{
    public async Task<ICatGaResult> ExecuteAsync(OrderSagaData data)
    {
        // 步骤 1: 创建订单
        await CreateOrder(data.OrderId);

        // 步骤 2: 扣减库存
        await ReduceInventory(data.ProductId, data.Quantity);

        // 步骤 3: 支付
        await ProcessPayment(data.Amount);

        return CatGaResult.Success();
    }

    public async Task CompensateAsync(OrderSagaData data)
    {
        // 补偿逻辑（回滚）
        await CancelOrder(data.OrderId);
        await RestoreInventory(data.ProductId, data.Quantity);
        await RefundPayment(data.Amount);
    }
}

// 使用
await _sagaCoordinator.ExecuteAsync(new OrderSaga(), sagaData);
```

---

## 🚀 NativeAOT 发布

### **配置项目文件**
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PublishAot>true</PublishAot>
  </PropertyGroup>
</Project>
```

### **发布命令**
```bash
# 发布 NativeAOT
dotnet publish -c Release

# 运行
./bin/Release/net9.0/publish/YourApp
```

### **注意事项**
- ✅ 使用手动注册 Handler（避免反射）
- ✅ 使用 MemoryPack 序列化器（AOT 优化）
- ⚠️ 不要使用 `AddCatgaDevelopment()`（包含反射扫描）

---

## 📚 下一步

### **深入学习**
- 📖 [完整文档](./DOCUMENTATION_INDEX.md)
- 📖 [架构设计](./ARCHITECTURE.md)
- 📖 [简化 API](./SIMPLIFIED_API.md)
- 📖 [性能优化](./docs/performance/)
- 📖 [分布式架构](./docs/distributed/)

### **参考资源**
- 🔍 [单元测试](./tests/Catga.Tests/) - 学习 API 使用
- 📊 [性能基准](./benchmarks/Catga.Benchmarks/) - 了解性能
- 🎯 [快速参考](./QUICK_REFERENCE.md) - API 速查

---

## ❓ 常见问题

### **Q: 如何选择序列化器？**
- **JSON**: 兼容性好，可读性强，适合跨语言
- **MemoryPack**: 性能最佳，体积小，适合 .NET 内部通信

### **Q: NATS 和 Redis 如何选择？**
- **NATS**: 轻量级，高性能，原生支持分布式消息
- **Redis**: 功能丰富，生态成熟，适合复杂场景

### **Q: 如何启用 Outbox/Inbox？**
```csharp
// NATS
services.AddNatsJetStreamStores();

// Redis
services.AddRedisStores();
```

### **Q: 如何调试？**
```csharp
// 启用日志
builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.SetMinimumLevel(LogLevel.Debug);
});

// 启用追踪
builder.Services.AddCatgaBuilder(b => b.WithTracing());
```

---

## 🎉 开始使用

现在你已经准备好使用 Catga 了！

**下一步**:
1. ✅ 参考上面的示例创建你的第一个 Handler
2. ✅ 运行你的应用
3. ✅ 查看[完整文档](./DOCUMENTATION_INDEX.md)了解更多

**需要帮助？**
- 📖 查看文档
- 🐛 提交 Issue
- 💬 参与讨论

---

**祝你使用 Catga 愉快！** 🚀

