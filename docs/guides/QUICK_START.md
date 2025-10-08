# 🚀 Catga 快速开始指南

> **从零到生产级微服务，只需 3 步**

---

## 🎯 第 1 步：核心层（30 分钟）

### 安装

```bash
dotnet new webapi -n MyService
cd MyService
dotnet add package Catga
```

### 配置

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga();  // 就这一行！

var app = builder.Build();
```

### 使用

```csharp
// 1. 定义命令
public record CreateOrderCommand(string OrderId, decimal Amount) : ICommand;

// 2. 定义处理器
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    public async Task<Result> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        // 你的业务逻辑
        Console.WriteLine($"创建订单: {command.OrderId}");
        return Result.Success();
    }
}

// 3. 使用
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});
```

✅ **你已经掌握了 CQRS 核心！**

---

## 🌐 第 2 步：分布式层（1-2 小时）

### 安装

```bash
dotnet add package Catga.Nats
dotnet add package Catga.Redis
```

### 配置

```csharp
builder.Services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")      // NATS 消息传输
    .AddRedisCatgaStore("localhost:6379");      // Redis 状态存储
```

### 跨服务调用

```csharp
// OrderService 发送命令
var result = await mediator.SendAsync(new ProcessPaymentCommand(...));
// ↓ 自动通过 NATS 路由
// PaymentService 接收并处理
```

### 运行依赖

```bash
# 启动 NATS
docker run -d -p 4222:4222 nats:latest

# 启动 Redis
docker run -d -p 6379:6379 redis:latest
```

✅ **你已经拥有了分布式微服务架构！**

---

## 🔄 第 3 步：可靠性层（2-3 小时）

### 配置

```csharp
builder.Services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379")
    .AddRedisOutbox()                           // 可靠消息发送
    .AddRedisInbox()                            // 幂等消息处理
    .AddPipelineBehavior<CircuitBreakerBehavior>()  // 熔断器
    .AddPipelineBehavior<RetryBehavior>();          // 重试机制
```

### Saga 分布式事务

```csharp
var saga = new OrderSaga();

saga.AddStep<CreateOrderCommand, OrderCreatedEvent>()
    .Compensate<CancelOrderCommand>()           // 如果失败，自动补偿
    .WithRetry(3);                              // 重试 3 次

saga.AddStep<ProcessPaymentCommand, PaymentProcessedEvent>()
    .Compensate<RefundPaymentCommand>();        // 退款补偿

await saga.ExecuteAsync(new CreateOrderCommand(...));
```

✅ **你已经拥有了生产级可靠性！**

---

## 🎊 完整示例

### OrderService

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379")
    .AddRedisOutbox()
    .AddRedisInbox()
    .AddPipelineBehavior<LoggingBehavior<,>>()
    .AddPipelineBehavior<CircuitBreakerBehavior>()
    .AddPipelineBehavior<RetryBehavior>();

var app = builder.Build();

// 创建订单 API
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// 查询订单 API
app.MapGet("/orders/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync(new GetOrderQuery(id));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
});

app.Run();
```

### 命令和处理器

```csharp
// Commands/CreateOrderCommand.cs
public record CreateOrderCommand(
    string OrderId,
    string CustomerId,
    decimal Amount) : ICommand<Order>;

// Handlers/CreateOrderHandler.cs
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Order>
{
    private readonly IOrderRepository _repository;
    private readonly ICatgaMediator _mediator;

    public CreateOrderHandler(IOrderRepository repository, ICatgaMediator mediator)
    {
        _repository = repository;
        _mediator = mediator;
    }

    public async Task<Result<Order>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        // 1. 创建订单
        var order = new Order
        {
            Id = command.OrderId,
            CustomerId = command.CustomerId,
            Amount = command.Amount,
            Status = OrderStatus.Created
        };

        // 2. 保存订单
        await _repository.SaveAsync(order);

        // 3. 发布事件（通过 Outbox 可靠发送）
        await _mediator.PublishAsync(new OrderCreatedEvent(
            order.Id,
            order.CustomerId,
            order.Amount));

        // 4. 调用支付服务（自动路由到 PaymentService）
        var paymentResult = await _mediator.SendAsync(new ProcessPaymentCommand(
            order.Id,
            order.Amount));

        if (!paymentResult.IsSuccess)
        {
            order.Status = OrderStatus.PaymentFailed;
            await _repository.SaveAsync(order);
            return Result<Order>.Failure(paymentResult.Error!);
        }

        order.Status = OrderStatus.Confirmed;
        await _repository.SaveAsync(order);

        return Result<Order>.Success(order);
    }
}
```

### PaymentService

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379")
    .AddRedisInbox();  // 确保幂等性

var app = builder.Build();
app.Run();

// Handlers/ProcessPaymentHandler.cs
public class ProcessPaymentHandler : ICommandHandler<ProcessPaymentCommand, PaymentResult>
{
    private readonly IPaymentGateway _gateway;

    public async Task<Result<PaymentResult>> HandleAsync(
        ProcessPaymentCommand command,
        CancellationToken cancellationToken)
    {
        // 调用第三方支付网关
        var result = await _gateway.ChargeAsync(command.Amount);

        if (result.Success)
        {
            return Result<PaymentResult>.Success(new PaymentResult
            {
                TransactionId = result.TransactionId,
                Status = PaymentStatus.Completed
            });
        }

        return Result<PaymentResult>.Failure(new Error(
            "PAYMENT_FAILED",
            $"支付失败: {result.ErrorMessage}"));
    }
}
```

---

## 🎯 运行你的微服务

### 1. 启动依赖

```bash
# Docker Compose
docker-compose up -d

# 或手动启动
docker run -d -p 4222:4222 nats:latest
docker run -d -p 6379:6379 redis:latest
```

### 2. 启动服务

```bash
# Terminal 1: OrderService
cd OrderService
dotnet run --urls "http://localhost:5001"

# Terminal 2: PaymentService
cd PaymentService
dotnet run --urls "http://localhost:5002"
```

### 3. 测试

```bash
# 创建订单
curl -X POST http://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "order-001",
    "customerId": "customer-123",
    "amount": 99.99
  }'

# 查询订单
curl http://localhost:5001/orders/order-001
```

---

## 🔍 高级功能（可选）

### 服务发现

```csharp
// Kubernetes 环境
builder.Services.AddKubernetesServiceDiscovery();

// Consul 环境
builder.Services.AddConsulServiceDiscovery("http://consul:8500");
```

### 流处理

```csharp
var pipeline = StreamProcessor.From(eventStream)
    .Where(e => e.Type == "OrderCreated")
    .Select(e => Transform(e))
    .Batch(100)
    .Do(batch => await ProcessBatchAsync(batch));

await pipeline.RunAsync();
```

---

## 📚 学习路径

| 阶段 | 时间 | 内容 | 文档 |
|-----|------|------|------|
| **第1天** | 30分钟 | CQRS 核心 | [README.md](README.md) |
| **第2-3天** | 1-2小时 | 分布式消息 | [ARCHITECTURE.md](ARCHITECTURE.md) |
| **第4-5天** | 2-3小时 | 可靠性层 | [QUICK_REFERENCE.md](QUICK_REFERENCE.md) |
| **第2周** | 按需 | 高级功能 | [服务发现](docs/service-discovery/README.md) |

---

## 🎊 总结

### 你已经学会了

✅ **CQRS 模式** - 命令、查询、事件分离
✅ **分布式消息** - NATS 跨服务通信
✅ **可靠消息** - Outbox/Inbox 模式
✅ **分布式事务** - Saga 编排
✅ **弹性设计** - 熔断、重试、限流

### 下一步

- 📖 查看 [ARCHITECTURE.md](ARCHITECTURE.md) 了解完整架构
- 🎯 查看 [示例项目](examples/) 学习更多
- 💡 查看 [QUICK_REFERENCE.md](QUICK_REFERENCE.md) 速查 API

---

**开始构建你的分布式系统吧！** 🚀

