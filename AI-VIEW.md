# 🤖 Catga - AI 使用指南

**面向 AI 的完整参考文档**

> 本文档专为 AI 助手设计，提供 Catga 框架的全面理解，包括使用方式、全特性列表和注意事项。

---

## 📖 目录

1. [核心概念](#核心概念)
2. [完整特性清单](#完整特性清单)
3. [使用方式](#使用方式)
4. [架构设计](#架构设计)
5. [性能优化](#性能优化)
6. [注意事项](#注意事项)
7. [最佳实践](#最佳实践)
8. [常见问题](#常见问题)

---

## 核心概念

### 什么是 Catga？

Catga 是一个**高性能、零反射、AOT 兼容**的 .NET CQRS/Event Sourcing 框架。

**核心特点**:
- **纳秒级延迟**: 462ns/op (2.2M QPS)
- **零反射**: 完全使用源生成器
- **Native AOT**: 100% 支持 AOT 编译
- **零分配优化**: ArrayPool + MemoryPool + Span
- **生产就绪**: 弹性机制完整

### CQRS 模式

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
   ┌───▼───┐
   │Request│ (Command/Query)
   └───┬───┘
       │
┌──────▼────────┐
│ CatgaMediator │ ← 核心调度器
└──────┬────────┘
       │
   ┌───▼────┐
   │Handler │ ← 业务逻辑
   └───┬────┘
       │
   ┌───▼────┐
   │ Result │
   └────────┘
```

### 三种消息类型

1. **Command (命令)** - 写操作，修改状态
   - `IRequest<TResponse>` 或 `ICommand<TResponse>`
   - 有且仅有一个 Handler
   - 返回 `CatgaResult<TResponse>`

2. **Query (查询)** - 读操作，不修改状态
   - `IRequest<TResponse>` 或 `IQuery<TResponse>`
   - 有且仅有一个 Handler
   - 返回 `CatgaResult<TResponse>`

3. **Event (事件)** - 已发生的事实
   - `IEvent`
   - 可以有多个 Handler
   - Fire-and-forget (不返回值)

---

## 完整特性清单

### 🎯 核心功能

#### 1. 消息处理
- ✅ 命令处理 (Command)
- ✅ 查询处理 (Query)
- ✅ 事件发布 (Event)
- ✅ 批量处理 (`SendBatchAsync`, `PublishBatchAsync`)
- ✅ 流式处理 (`SendStreamAsync`)
- ✅ 取消令牌支持 (`CancellationToken`)
- ✅ 参数验证 (`ArgumentNullException.ThrowIfNull`)

#### 2. Pipeline Behaviors (管道行为)
- ✅ **LoggingBehavior** - 结构化日志
- ✅ **IdempotencyBehavior** - 消息去重
- ✅ **RetryBehavior** - 自动重试
- ✅ **ValidationBehavior** - 请求验证
- ✅ **TracingBehavior** - 分布式追踪

#### 3. 弹性机制 (Resilience)
- ✅ **CircuitBreaker** - 熔断器
  - 三种状态: Closed, Open, HalfOpen
  - 失败阈值配置
  - 自动恢复
- ✅ **ConcurrencyLimiter** - 并发限制
  - SemaphoreSlim 实现
  - 背压控制
  - 防止线程池饥饿
- ✅ **RetryPolicy** - 重试策略
  - 指数退避
  - 可配置重试次数
  - 可重试异常过滤

#### 4. 持久化 (Persistence)
- ✅ **InMemory** - 内存存储 (开发/测试)
- ✅ **Redis** - Redis 持久化
  - RedisOutboxStore
  - RedisInboxStore
  - RedisIdempotencyStore
  - RedisDeadLetterQueue
- ✅ **NATS JetStream** - NATS 持久化
  - NatsJSOutboxStore
  - NatsJSInboxStore
  - NatsJSEventStore
  - NatsJSIdempotencyStore
  - NatsJSDeadLetterQueue

#### 5. 传输层 (Transport)
- ✅ **InMemory** - 内存传输 (单体应用)
- ✅ **NATS** - NATS 消息传输
- ✅ **Redis** - Redis Pub/Sub

#### 6. 序列化 (Serialization)
- ✅ **JSON** - System.Text.Json
- ✅ **MemoryPack** - 高性能二进制序列化

#### 7. 可观测性 (Observability)
- ✅ **分布式追踪** - OpenTelemetry ActivitySource
- ✅ **结构化日志** - Microsoft.Extensions.Logging
- ✅ **指标收集** - Metrics API
- ✅ **CorrelationId** - 端到端追踪
- ✅ **死信队列 (DLQ)** - 失败消息记录

#### 8. 消息生成
- ✅ **Snowflake ID** - 分布式唯一 ID
  - 自动生成 MessageId
  - 时间戳 + WorkerId + 序列号
  - 单调递增
- ✅ **CorrelationId** - 关联 ID
  - 跨服务追踪
  - 自动传播

#### 9. 源生成器 (Source Generator)
- ✅ **自动生成 MessageId** - 为所有消息添加 ID
- ✅ **自动注册 Handler** - 编译时发现和注册

#### 10. ASP.NET Core 集成
- ✅ **端点注册** - `MapCatgaEndpoints()`
- ✅ **诊断端点** - `/catga/health`, `/catga/metrics`
- ✅ **依赖注入** - 完整 DI 支持

#### 11. 测试支持
- ✅ **CatgaTestFixture** - 测试夹具
- ✅ **Mock 支持** - 与 Moq/NSubstitute 兼容
- ✅ **FluentAssertions** - 断言扩展

---

## 使用方式

### 1. 基础设置

#### 1.1 安装包

```bash
# 核心包 (必需)
dotnet add package Catga

# 传输层 (选择一个)
dotnet add package Catga.Transport.InMemory  # 推荐: 单体应用
dotnet add package Catga.Transport.Nats      # 分布式应用
dotnet add package Catga.Transport.Redis     # Redis Pub/Sub

# 持久化 (可选)
dotnet add package Catga.Persistence.InMemory
dotnet add package Catga.Persistence.Redis
dotnet add package Catga.Persistence.Nats

# 序列化 (可选)
dotnet add package Catga.Serialization.Json        # 默认
dotnet add package Catga.Serialization.MemoryPack  # 高性能

# ASP.NET Core (可选)
dotnet add package Catga.AspNetCore

# 测试 (可选)
dotnet add package Catga.Testing
```

#### 1.2 注册服务

```csharp
// Program.cs
using Catga;

var builder = WebApplication.CreateBuilder(args);

// ⭐ 方式1: 默认配置 (推荐)
builder.Services.AddCatga();

// ⭐ 方式2: 自定义配置
builder.Services.AddCatga(options =>
{
    options.MaxConcurrentRequests = 5000;
    options.EnableCircuitBreaker = true;
    options.CircuitBreakerFailureThreshold = 10;
    options.EnableRetry = true;
    options.MaxRetryAttempts = 3;
});

// ⭐ 方式3: 配置预设
builder.Services.AddCatga(opt => opt.WithHighPerformance());
// 或
builder.Services.AddCatga(opt => opt.WithResilience());
// 或
builder.Services.AddCatga(opt => opt.ForDevelopment());

// 传输层
builder.Services.AddInMemoryTransport(); // 内存传输

// 持久化 (可选)
builder.Services.AddInMemoryPersistence();

var app = builder.Build();
app.Run();
```

### 2. 定义消息

#### 2.1 Command (命令)

```csharp
using Catga.Abstractions;

// ✅ 推荐: 使用 record
public record CreateOrderCommand(
    string ProductId,
    int Quantity,
    string CustomerEmail
) : IRequest<Order>;

// MessageId 会自动生成 (源生成器)
// 你也可以手动设置:
// public long MessageId { get; init; } = MessageExtensions.NewMessageId();
```

#### 2.2 Query (查询)

```csharp
public record GetOrderQuery(string OrderId) : IRequest<Order?>;

// 或使用更语义化的接口
public record GetOrderQuery(string OrderId) : IQuery<Order?>;
```

#### 2.3 Event (事件)

```csharp
public record OrderCreatedEvent(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal TotalAmount
) : IEvent;

// 可以有多个 Handler 订阅此事件
```

### 3. 实现 Handler

#### 3.1 Command Handler

```csharp
using Catga.Abstractions;
using Catga.Core;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Order>
{
    private readonly IOrderRepository _repository;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository repository,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<Order>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // 1️⃣ 验证
        if (string.IsNullOrWhiteSpace(request.ProductId))
            return CatgaResult<Order>.Failure("ProductId is required");

        if (request.Quantity <= 0)
            return CatgaResult<Order>.Failure("Quantity must be positive");

        // 2️⃣ 业务逻辑
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            CustomerEmail = request.CustomerEmail,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // 3️⃣ 保存
        await _repository.SaveAsync(order, cancellationToken);

        // 4️⃣ 发布事件
        await _mediator.PublishAsync(new OrderCreatedEvent(
            order.Id,
            order.ProductId,
            order.Quantity,
            order.TotalAmount
        ), cancellationToken);

        // 5️⃣ 返回结果
        return CatgaResult<Order>.Success(order);
    }
}
```

#### 3.2 Query Handler

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
        CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        return CatgaResult<Order?>.Success(order);
    }
}
```

#### 3.3 Event Handler

```csharp
// ✅ 可以有多个 Handler 订阅同一个事件
public class OrderCreatedEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public OrderCreatedEmailHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _emailService.SendOrderConfirmationAsync(
            @event.OrderId,
            cancellationToken
        );
    }
}

public class OrderCreatedAnalyticsHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IAnalyticsService _analytics;

    public OrderCreatedAnalyticsHandler(IAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _analytics.TrackOrderCreatedAsync(@event, cancellationToken);
    }
}
```

### 4. 使用 Mediator

#### 4.1 发送命令/查询

```csharp
public class OrderController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrderController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        // 发送命令
        var command = new CreateOrderCommand(
            request.ProductId,
            request.Quantity,
            request.CustomerEmail
        );

        var result = await _mediator.SendAsync(command);

        if (result.IsSuccess)
            return Ok(result.Value);
        else
            return BadRequest(new { error = result.Error });
    }

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> GetOrder(string orderId)
    {
        // 发送查询
        var query = new GetOrderQuery(orderId);
        var result = await _mediator.SendAsync(query);

        if (result.IsSuccess && result.Value != null)
            return Ok(result.Value);
        else
            return NotFound();
    }
}
```

#### 4.2 发布事件

```csharp
// 发布单个事件
await _mediator.PublishAsync(new OrderCreatedEvent(...));

// 批量发布事件
var events = new List<OrderCreatedEvent>
{
    new OrderCreatedEvent("order-1", ...),
    new OrderCreatedEvent("order-2", ...),
    new OrderCreatedEvent("order-3", ...)
};
await _mediator.PublishBatchAsync(events);
```

#### 4.3 批量处理

```csharp
// 批量发送命令
var commands = new List<CreateOrderCommand>
{
    new CreateOrderCommand("PROD-1", 10, "user1@example.com"),
    new CreateOrderCommand("PROD-2", 5, "user2@example.com"),
    new CreateOrderCommand("PROD-3", 3, "user3@example.com")
};

var results = await _mediator.SendBatchAsync<CreateOrderCommand, Order>(commands);

foreach (var result in results)
{
    if (result.IsSuccess)
        Console.WriteLine($"Order created: {result.Value.Id}");
    else
        Console.WriteLine($"Failed: {result.Error}");
}
```

#### 4.4 流式处理

```csharp
// 适用于大量数据的流式处理
async IAsyncEnumerable<CreateOrderCommand> GenerateOrders()
{
    for (int i = 0; i < 10000; i++)
    {
        yield return new CreateOrderCommand($"PROD-{i}", 1, $"user{i}@example.com");
    }
}

await foreach (var result in _mediator.SendStreamAsync<CreateOrderCommand, Order>(GenerateOrders()))
{
    if (result.IsSuccess)
        Console.WriteLine($"Processed: {result.Value.Id}");
}
```

### 5. Pipeline Behaviors

#### 5.1 自定义 Behavior

```csharp
public class PerformanceMonitoringBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceMonitoringBehavior<TRequest, TResponse>> _logger;

    public PerformanceMonitoringBehavior(ILogger<PerformanceMonitoringBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await next();
            sw.Stop();

            if (sw.ElapsedMilliseconds > 1000) // 超过1秒记录警告
            {
                _logger.LogWarning(
                    "Slow request detected: {RequestType} took {ElapsedMs}ms",
                    typeof(TRequest).Name,
                    sw.ElapsedMilliseconds
                );
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Request failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }
}

// 注册
services.AddPipelineBehavior(typeof(PerformanceMonitoringBehavior<,>));
```

### 6. 错误处理

#### 6.1 使用 CatgaResult

```csharp
// ✅ 成功
return CatgaResult<Order>.Success(order);

// ❌ 失败（业务错误）
return CatgaResult<Order>.Failure("Order not found");

// ❌ 失败（带异常）
return CatgaResult<Order>.Failure("Payment failed", exception);

// 检查结果
var result = await _mediator.SendAsync(command);
if (result.IsSuccess)
{
    var order = result.Value;
    // 处理成功
}
else
{
    var error = result.Error;
    var exception = result.Exception;
    // 处理失败
}
```

#### 6.2 异常处理

```csharp
public async Task<CatgaResult<Order>> HandleAsync(
    CreateOrderCommand request,
    CancellationToken cancellationToken)
{
    try
    {
        // 业务逻辑
        var order = await _repository.SaveAsync(request);
        return CatgaResult<Order>.Success(order);
    }
    catch (ValidationException ex)
    {
        // 业务异常 - 返回 Failure
        return CatgaResult<Order>.Failure(ex.Message);
    }
    catch (Exception ex)
    {
        // 系统异常 - 会被 Pipeline 捕获和记录
        throw;
    }
}
```

### 7. 分布式部署

#### 7.1 使用 NATS

```csharp
// 配置 NATS
builder.Services.AddNatsTransport("nats://localhost:4222", options =>
{
    options.ConnectionName = "OrderService";
    options.MaxReconnectAttempts = 5;
});

// NATS 持久化（使用已注册的 INatsConnection）
builder.Services.AddNatsPersistence(options =>
{
    options.EventStreamName = "ORDERS";
    options.OutboxStreamName = "CATGA_OUTBOX";
    options.InboxStreamName = "CATGA_INBOX";
});
```

#### 7.2 使用 Redis

```csharp
// 配置 Redis
builder.Services.AddRedisTransport("localhost:6379", options =>
{
    options.ChannelPrefix = "catga:";
});

// Redis 持久化（Outbox + Inbox），Idempotency 可单独调用 AddRedisIdempotencyStore
builder.Services.AddRedisPersistence(
    outbox =>
    {
        outbox.KeyPrefix = "outbox:";
        outbox.Database = 0;
    },
    inbox =>
    {
        inbox.KeyPrefix = "inbox:";
        inbox.Database = 0;
    });
```

---

## 架构设计

### 核心组件

```
┌─────────────────────────────────────────────────────────┐
│                     CatgaMediator                       │
│  (核心调度器，协调所有组件)                              │
└─────────────┬───────────────────────────────────────────┘
              │
    ┌─────────┼─────────┐
    │         │         │
    ▼         ▼         ▼
┌────────┐ ┌────────┐ ┌──────────┐
│Pipeline│ │Handler │ │Transport │
│Behaviors│ │ Cache  │ │  Layer   │
└────────┘ └────────┘ └──────────┘
    │         │         │
    │         │         │
    ▼         ▼         ▼
┌──────────────────────────────┐
│      Persistence Layer       │
│ (Outbox/Inbox/Idempotency)  │
└──────────────────────────────┘
```

### 数据流

```
1. 客户端发送请求
   ↓
2. CatgaMediator 接收
   ↓
3. Pipeline Behaviors 处理
   - Logging
   - Idempotency Check
   - Validation
   - Tracing
   ↓
4. Handler Cache 查找 Handler
   ↓
5. Handler 执行业务逻辑
   ↓
6. 返回 CatgaResult
   ↓
7. Pipeline Behaviors 后处理
   - Retry (如果失败)
   - Logging
   ↓
8. 返回给客户端
```

### 性能优化架构

1. **零反射设计**
   - 源生成器在编译时生成所有代码
   - 运行时完全无反射调用

2. **零分配优化**
   - 使用 `ArrayPool<T>` 和 `MemoryPool<T>`
   - 尽可能使用 `Span<T>` 和 `Memory<T>`
   - 避免闭包和装箱

3. **热路径优化**
   - `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
   - 栈分配代替堆分配
   - 最小化异步状态机开销

4. **并发优化**
   - 无锁数据结构 (`ConcurrentDictionary`)
   - 原子操作 (`Interlocked`)
   - 分片存储减少锁竞争

---

## 性能优化

### 1. 配置优化

```csharp
builder.Services.AddCatga(options =>
{
    // 🚀 并发优化
    options.MaxConcurrentRequests = 5000; // 增加并发限制

    // 🚀 幂等性优化
    options.IdempotencyShardCount = 64; // 分片减少锁竞争
    options.IdempotencyRetentionHours = 24; // 24小时后清理

    // 🚀 禁用不需要的功能
    options.EnableTracing = false; // 如果不需要追踪
    options.EnableMetrics = false; // 如果不需要指标

    // 🚀 批处理优化
    options.BatchSize = 1000; // 批处理大小
});
```

### 2. Handler 优化

```csharp
public class OptimizedHandler : IRequestHandler<MyCommand, MyResponse>
{
    // ✅ 使用 ValueTask 代替 Task (适合同步操作)
    public ValueTask<CatgaResult<MyResponse>> HandleAsync(
        MyCommand request,
        CancellationToken cancellationToken)
    {
        // 同步操作直接返回
        var response = ProcessSync(request);
        return new ValueTask<CatgaResult<MyResponse>>(
            CatgaResult<MyResponse>.Success(response)
        );
    }

    // ✅ 避免不必要的异步
    private MyResponse ProcessSync(MyCommand request)
    {
        // 纯计算，无 I/O
        return new MyResponse { ... };
    }
}
```

### 3. 批量处理优化

```csharp
// ✅ 使用批量API
var results = await _mediator.SendBatchAsync<MyCommand, MyResponse>(commands);

// ❌ 避免循环调用
// foreach (var cmd in commands)
// {
//     await _mediator.SendAsync(cmd); // 低效
// }
```

### 4. 内存优化

```csharp
// ✅ 使用 ArrayPool
private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

public async Task ProcessAsync()
{
    var buffer = _pool.Rent(4096);
    try
    {
        // 使用 buffer
    }
    finally
    {
        _pool.Return(buffer);
    }
}
```

---

## 注意事项

### ⚠️ 关键注意事项

#### 1. **Handler 注册规则**

```csharp
// ✅ 正确: 一个 Command/Query 只能有一个 Handler
public record MyCommand : IRequest<MyResponse>;
public class MyCommandHandler : IRequestHandler<MyCommand, MyResponse> { }

// ❌ 错误: 不能有多个 Handler 处理同一个 Command
public class AnotherMyCommandHandler : IRequestHandler<MyCommand, MyResponse> { }
// 编译时会报错: CAT2003

// ✅ 正确: Event 可以有多个 Handler
public record MyEvent : IEvent;
public class MyEventHandler1 : IEventHandler<MyEvent> { }
public class MyEventHandler2 : IEventHandler<MyEvent> { } // OK
```

#### 2. **异步陷阱**

```csharp
// ❌ 错误: 不要在 Handler 中使用 .Result 或 .Wait()
public Task<CatgaResult<MyResponse>> HandleAsync(...)
{
    var result = SomeAsyncMethod().Result; // 可能死锁
    return Task.FromResult(CatgaResult<MyResponse>.Success(result));
}

// ✅ 正确: 使用 await
public async Task<CatgaResult<MyResponse>> HandleAsync(...)
{
    var result = await SomeAsyncMethod();
    return CatgaResult<MyResponse>.Success(result);
}
```

#### 3. **取消令牌**

```csharp
// ✅ 正确: 始终传递 CancellationToken
public async Task<CatgaResult<MyResponse>> HandleAsync(
    MyCommand request,
    CancellationToken cancellationToken) // 必需参数
{
    // 传递给所有异步调用
    var data = await _repository.GetAsync(request.Id, cancellationToken);
    await _service.ProcessAsync(data, cancellationToken);

    // 长时间操作前检查
    cancellationToken.ThrowIfCancellationRequested();

    return CatgaResult<MyResponse>.Success(response);
}
```

#### 4. **事件处理失败**

```csharp
// ⚠️ 注意: Event Handler 的异常不会传播
public class MyEventHandler : IEventHandler<MyEvent>
{
    public async Task HandleAsync(MyEvent @event, CancellationToken ct)
    {
        try
        {
            // 可能失败的操作
            await _service.ProcessAsync(@event);
        }
        catch (Exception ex)
        {
            // ✅ 记录日志
            _logger.LogError(ex, "Event processing failed");

            // ✅ 可选: 发送到死信队列
            await _dlq.AddAsync(@event, ex);

            // ❌ 不要 throw - 会阻止其他 Handler 执行
            // throw;
        }
    }
}
```

#### 5. **幂等性**

```csharp
// ✅ Catga 自动处理幂等性（基于 MessageId）
// 相同的 MessageId 只会处理一次

var command = new MyCommand { ... };
// MessageId 会自动生成

await _mediator.SendAsync(command); // 处理
await _mediator.SendAsync(command); // 跳过（相同 MessageId）

// ⚠️ 如果需要重新处理，使用新的 Command 实例
var newCommand = new MyCommand { ... }; // 新的 MessageId
await _mediator.SendAsync(newCommand); // 重新处理
```

#### 6. **批处理取消**

```csharp
// ⚠️ 注意: 批处理不会立即取消
var cts = new CancellationTokenSource();
var commands = GenerateCommands(1000);

var task = _mediator.SendBatchAsync<MyCommand, MyResponse>(commands, cts.Token);

cts.Cancel(); // 取消

// 批处理会完成已启动的任务
var results = await task; // 不会立即抛出 OperationCanceledException
```

#### 7. **CircuitBreaker 状态**

```csharp
// ✅ 理解 CircuitBreaker 的三种状态
// 1. Closed (正常): 所有请求通过
// 2. Open (打开): 所有请求拒绝（快速失败）
// 3. HalfOpen (半开): 测试性请求，判断是否恢复

// ⚠️ HalfOpen 状态下，任何失败都会重新打开熔断器
var circuitBreaker = new CircuitBreaker(failureThreshold: 5);

// 5次失败后 -> Open
// 等待 30 秒 -> HalfOpen
// 如果成功 -> Closed
// 如果失败 -> Open (重新等待)
```

#### 8. **序列化限制**

```csharp
// ⚠️ MemoryPack 需要特殊标记
[MemoryPackable]
public partial record MyCommand : IRequest<MyResponse>
{
    // 属性必须是可序列化的
}

// ✅ JSON 序列化更灵活但性能较低
// ✅ 开发环境推荐 JSON，生产环境推荐 MemoryPack
```

#### 9. **依赖注入生命周期**

```csharp
// ✅ Handler 默认是 Scoped
services.AddCatga(); // Handler 自动注册为 Scoped

// ⚠️ 注意: Handler 中不要注入 Singleton 的有状态服务
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    // ✅ 可以注入 Scoped 或 Transient
    private readonly IRepository _repository; // Scoped - OK

    // ⚠️ 小心注入 Singleton
    private readonly ICacheService _cache; // Singleton - 确保线程安全
}
```

#### 10. **性能测试**

```csharp
// ⚠️ 注意: 使用 Release 配置进行性能测试
// dotnet run -c Release

// ✅ 使用 BenchmarkDotNet
[MemoryDiagnoser]
public class MyBenchmark
{
    [Benchmark]
    public async Task SendCommand()
    {
        await _mediator.SendAsync(new MyCommand());
    }
}
```

---

## 最佳实践

### 1. **消息设计**

```csharp
// ✅ 使用 record 类型（不可变）
public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<Order>;

// ✅ 使用描述性名称
public record PlaceOrderCommand(...) : IRequest<Order>;

// ✅ 包含所有必需数据
public record UpdateOrderCommand(
    string OrderId,    // 必需
    string? Status,    // 可选
    DateTime? ShipDate // 可选
) : IRequest<Order>;

// ❌ 避免过于通用的名称
// public record ProcessCommand(...) : IRequest<Result>; // 不清晰
```

### 2. **Handler 设计**

```csharp
// ✅ 单一职责
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Order>
{
    // 只处理订单创建逻辑
    // 不要包含支付、发货等其他逻辑
}

// ✅ 使用构造函数注入
public class CreateOrderHandler
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository repository,
        ILogger<CreateOrderHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }
}

// ✅ 结构化处理
public async Task<CatgaResult<Order>> HandleAsync(
    CreateOrderCommand request,
    CancellationToken cancellationToken)
{
    // 1. 验证
    if (!IsValid(request))
        return CatgaResult<Order>.Failure("Invalid request");

    // 2. 业务逻辑
    var order = await CreateOrder(request, cancellationToken);

    // 3. 持久化
    await _repository.SaveAsync(order, cancellationToken);

    // 4. 发布事件（可选）
    await PublishEventsAsync(order, cancellationToken);

    // 5. 返回结果
    return CatgaResult<Order>.Success(order);
}
```

### 3. **错误处理**

```csharp
// ✅ 区分业务错误和系统错误
public async Task<CatgaResult<Order>> HandleAsync(...)
{
    try
    {
        // 业务验证
        if (!IsValid(request))
            return CatgaResult<Order>.Failure("Invalid request"); // 业务错误

        // 业务逻辑
        var order = await ProcessOrder(request);
        return CatgaResult<Order>.Success(order);
    }
    catch (BusinessException ex)
    {
        // 预期的业务异常
        return CatgaResult<Order>.Failure(ex.Message);
    }
    // 系统异常让它传播，Pipeline 会处理
}
```

### 4. **测试**

```csharp
// ✅ 使用 Catga.Testing
[Fact]
public async Task CreateOrder_WithValidData_ShouldSucceed()
{
    // Arrange
    var fixture = new CatgaTestFixture();
    fixture.RegisterRequestHandler<CreateOrderCommand, Order, CreateOrderHandler>();

    var command = new CreateOrderCommand("PROD-001", 5);

    // Act
    var result = await fixture.Mediator.SendAsync(command);

    // Assert
    result.Should().BeSuccessful();
    result.Value.ProductId.Should().Be("PROD-001");
    result.Value.Quantity.Should().Be(5);
}
```

### 5. **日志记录**

```csharp
// ✅ 使用结构化日志
_logger.LogInformation(
    "Order created: {OrderId}, Product: {ProductId}, Quantity: {Quantity}",
    order.Id, order.ProductId, order.Quantity
);

// ❌ 避免字符串拼接
// _logger.LogInformation("Order created: " + order.Id); // 不推荐
```

### 6. **配置管理**

```csharp
// ✅ 使用配置文件
// appsettings.json
{
  "Catga": {
    "MaxConcurrentRequests": 5000,
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 10
  }
}

// Program.cs
builder.Services.AddCatga(options =>
{
    builder.Configuration.GetSection("Catga").Bind(options);
});
```

---

## 常见问题

### Q1: Handler 没有被调用？

**A**: 检查以下几点：
1. Handler 是否实现了正确的接口？
2. Handler 是否在 Startup 之前定义？（源生成器需要）
3. 是否调用了 `services.AddCatga()`？
4. 消息类型是否匹配？

```csharp
// ✅ 确保类型匹配
await _mediator.SendAsync<CreateOrderCommand, Order>(command);
// 而不是
// await _mediator.SendAsync<CreateOrderCommand, string>(command); // 错误类型
```

### Q2: 如何处理长时间运行的任务？

**A**: 使用后台服务：

```csharp
public class OrderProcessingService : BackgroundService
{
    private readonly ICatgaMediator _mediator;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var order in GetPendingOrders(stoppingToken))
        {
            await _mediator.SendAsync(new ProcessOrderCommand(order.Id), stoppingToken);
        }
    }
}
```

### Q3: 如何实现事务？

**A**: 使用 Outbox 模式：

```csharp
public async Task<CatgaResult<Order>> HandleAsync(...)
{
    using var transaction = await _dbContext.BeginTransactionAsync();
    try
    {
        // 1. 保存实体
        await _repository.SaveAsync(order);

        // 2. 保存事件到 Outbox
        await _outbox.AddAsync(new OrderCreatedEvent(...));

        // 3. 提交事务
        await transaction.CommitAsync();

        return CatgaResult<Order>.Success(order);
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

// 后台服务发布 Outbox 中的事件
```

### Q4: 如何监控性能？

**A**: 使用内置的可观测性功能：

```csharp
// 1. 启用 OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddCatgaInstrumentation() // Catga 追踪
        .AddJaegerExporter());

// 2. 访问指标端点
// GET /catga/metrics

// 3. 检查死信队列
var failed = await _dlq.GetFailedMessagesAsync(100);
```

### Q5: 如何升级到新版本？

**A**:
1. 检查 CHANGELOG.md
2. 运行测试
3. 关注 BREAKING CHANGES
4. 逐步迁移

---

## 📚 参考资源

- **文档**: [docs/](./docs/)
- **示例**: [examples/OrderSystem.Api/](./examples/OrderSystem.Api/)
- **测试**: [tests/Catga.Tests/](./tests/Catga.Tests/)
- **性能测试**: [benchmarks/](./benchmarks/)
- **架构文档**: [docs/architecture/ARCHITECTURE.md](./docs/architecture/ARCHITECTURE.md)

---

## 🎯 总结

### 核心要点

1. **简单**: 2 行配置，自动注册 Handler
2. **快速**: 纳秒级延迟，百万 QPS
3. **可靠**: 完整的弹性机制
4. **可观测**: 内置追踪和监控
5. **AOT 友好**: 零反射，完全 AOT 兼容

### 立即开始

```bash
dotnet new webapi -n MyApp
cd MyApp
dotnet add package Catga
```

```csharp
// Program.cs
builder.Services.AddCatga();
```

就这么简单！🎉

---

**版本**: 0.1.0
**最后更新**: 2025-10-26
**许可证**: MIT

