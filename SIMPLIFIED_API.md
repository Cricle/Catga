# 🚀 Catga 简化 API 使用指南

Catga 提供了多种使用方式，从极简的一行配置到完全自定义，满足不同场景需求。

---

## ⚡ 极简模式（推荐新手）

### 开发环境
```csharp
var builder = WebApplication.CreateBuilder(args);

// 一行搞定！自动扫描 + 完整功能
builder.Services.AddCatgaDevelopment();

var app = builder.Build();
```

**自动启用：**
- ✅ 自动扫描当前程序集的所有 Handlers
- ✅ 日志记录
- ✅ 分布式追踪
- ✅ 请求验证
- ✅ 幂等处理
- ✅ 重试机制
- ✅ 死信队列

### 生产环境
```csharp
var builder = WebApplication.CreateBuilder(args);

// 生产优化配置
builder.Services.AddCatgaProduction();

var app = builder.Build();
```

**自动优化：**
- ⚡ 性能优化（32 分片，关闭详细日志）
- 🛡️ 可靠性（熔断器、重试、死信队列）
- 🔍 自动扫描 Handlers

---

## 🔧 链式配置（推荐进阶用户）

```csharp
builder.Services.AddCatgaBuilder(catga => catga
    .ScanCurrentAssembly()           // 自动扫描
    .WithOutbox(opt => {             // Outbox 模式
        opt.PollingInterval = TimeSpan.FromSeconds(5);
        opt.BatchSize = 100;
    })
    .WithInbox(opt => {              // Inbox 模式
        opt.LockDuration = TimeSpan.FromMinutes(5);
    })
    .WithReliability()               // 可靠性特性
    .WithPerformanceOptimization()   // 性能优化
    .Configure(opt => {              // 自定义配置
        opt.MaxConcurrentRequests = 1000;
    })
);
```

### 可选的构建器方法

| 方法 | 说明 |
|------|------|
| `ScanCurrentAssembly()` | 扫描当前程序集 |
| `ScanHandlers(Assembly)` | 扫描指定程序集 |
| `WithOutbox()` | 启用 Outbox 模式 |
| `WithInbox()` | 启用 Inbox 模式 |
| `WithReliability()` | 启用熔断/重试/死信队列 |
| `WithPerformanceOptimization()` | 性能优化配置 |
| `Configure()` | 自定义配置 |

---

## 📋 手动注册（完全控制）

```csharp
// 1. 注册核心服务
builder.Services.AddCatga(opt =>
{
    opt.EnableLogging = true;
    opt.EnableCircuitBreaker = true;
    opt.MaxConcurrentRequests = 500;
});

// 2. 手动注册每个 Handler（AOT 友好）
builder.Services.AddRequestHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>();
builder.Services.AddRequestHandler<UpdateOrderCommand, OrderResult, UpdateOrderHandler>();
builder.Services.AddEventHandler<OrderCreatedEvent, SendEmailHandler>();
builder.Services.AddEventHandler<OrderCreatedEvent, UpdateInventoryHandler>();

// 3. 添加 Outbox/Inbox
builder.Services.AddOutbox(opt => opt.BatchSize = 50);
builder.Services.AddInbox();
```

---

## 🌐 分布式场景

```csharp
// NATS + Redis 完整配置
builder.Services.AddCatgaBuilder(catga => catga
    .ScanCurrentAssembly()
    .WithOutbox()
    .WithInbox()
    .WithReliability()
);

// NATS 分布式消息
builder.Services.AddNatsCatga(opt =>
{
    opt.Url = "nats://localhost:4222";
    opt.ClusterName = "my-cluster";
});

// Redis 状态存储
builder.Services.AddRedisCatga("localhost:6379");
```

---

## 📊 对比表

| 方式 | 代码量 | 灵活性 | AOT 友好 | 适用场景 |
|------|--------|--------|----------|---------|
| `AddCatgaDevelopment()` | ⭐⭐⭐⭐⭐ 1行 | ⭐⭐ | ⚠️ 部分 | 快速开发 |
| `AddCatgaProduction()` | ⭐⭐⭐⭐⭐ 1行 | ⭐⭐ | ⚠️ 部分 | 快速部署 |
| 链式配置 | ⭐⭐⭐⭐ 5-10行 | ⭐⭐⭐⭐ | ⚠️ 部分 | 平衡选择 |
| 手动注册 | ⭐⭐ 20+行 | ⭐⭐⭐⭐⭐ | ✅ 完全 | 完全控制 |

> 💡 **建议**：开发时用极简模式，生产时根据需求选择链式配置或手动注册。

---

## ✨ 完整示例

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 极简配置
builder.Services.AddCatgaProduction();

var app = builder.Build();
app.MapPost("/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

app.Run();

// 命令定义
public record CreateOrderCommand(string CustomerId, decimal Amount) 
    : IRequest<OrderResult>;

public record OrderResult(string OrderId, OrderStatus Status);

// 处理器实现
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 业务逻辑
        var orderId = Guid.NewGuid().ToString();
        return CatgaResult<OrderResult>.Success(
            new OrderResult(orderId, OrderStatus.Created));
    }
}
```

就这么简单！🎉

