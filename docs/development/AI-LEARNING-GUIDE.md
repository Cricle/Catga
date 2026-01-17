# Catga AI 学习指南

> 本文档专为 AI 助手设计，提供 Catga 框架的核心概念、用法、注意事项和最佳实践。

## 📋 目录

- [框架概述](#overview)
- [核心概念](#core-concepts)
- [架构设计](#architecture)
- [使用示例](#examples)
- [重要注意事项](#important-notes)
- [最佳实践](#best-practices)
- [常见错误](#common-errors)
- [性能优化](#performance-optimization)
- [故障排查](#troubleshooting)

---

<a id="overview"></a>
## 框架概述 {#overview}

### 什么是 Catga？

Catga 是一个现代化、高性能的 .NET CQRS/Event Sourcing 框架，具有以下特点：

- **现代化设计**: 基于 .NET 6+ 和最新的 C# 特性
- **高性能**: 零反射、零分配、支持 Native AOT
- **生产就绪**: 完整的 Outbox/Inbox 模式实现
- **可扩展**: 支持 InMemory、Redis、NATS 多种传输和持久化方式
- **可观测**: 内置 OpenTelemetry 分布式追踪和指标
- **易用性**: Source Generator 自动生成代码

### 核心功能

```
┌─────────────────────────────────────────────────────────────┐
│                         Catga 框架                           │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   Commands   │  │   Queries    │  │    Events    │     │
│  │   (CQRS)     │  │   (CQRS)     │  │ (Event Bus)  │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   Outbox     │  │    Inbox     │  │ Event Store  │     │
│  │   (发送)     │  │   (接收)     │  │  (持久化)    │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │          Transport Layer (InMemory/Redis/NATS)        │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │       Persistence Layer (InMemory/Redis/NATS)         │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

<a id="core-concepts"></a>
## 核心概念 {#core-concepts}

### 1. CQRS (Command Query Responsibility Segregation)

**命令 (Command)**:
- 用于修改状态
- 通过 `SendAsync<TResponse>()` 发送
- 只有一个 Handler
- 可以返回结果

**查询 (Query)**:
- 用于读取数据
- 通过 `SendAsync<TResponse>()` 发送
- 只有一个 Handler
- 返回数据

**事件 (Event)**:
- 表示已发生的事实
- 通过 `PublishAsync()` 发布
- 可以有多个 Handler
- 异步处理

### 2. 接口定义

```csharp
// 命令/查询（有返回值）
public interface IRequest<out TResponse> : IBaseRequest { }

// 通知/事件（无返回值）
public interface INotification : IBaseRequest { }

// 命令/查询处理器
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

// 事件处理器
public interface IEventHandler<in TEvent> where TEvent : INotification
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
```

### 3. 传输层 (Transport)

负责消息的发送和接收：

```csharp
public interface IMessageTransport
{
    // 发布事件（一对多）
    Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default);

    // 发送命令/查询（一对一）
    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, TransportContext? context = null, CancellationToken cancellationToken = default);

    // 订阅事件
    Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, CancellationToken, Task> handler, CancellationToken cancellationToken = default);

    // 批量发布
    Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default);
}
```

**实现方式**:
- `Catga.Transport.InMemory`: 内存传输（开发/测试）
- `Catga.Transport.Redis`: Redis Pub/Sub (QoS 0) 和 Streams (QoS 1)
- `Catga.Transport.Nats`: NATS 消息传输

### 4. 持久化层 (Persistence)

负责事件存储和 Outbox/Inbox 模式：

```csharp
// 事件存储
public interface IEventStore
{
    Task SaveAsync(string streamId, IEnumerable<object> events, long expectedVersion, CancellationToken cancellationToken = default);
    Task<IEnumerable<object>> LoadAsync(string streamId, long fromVersion, CancellationToken cancellationToken = default);
}

// Outbox 存储（发送端）
public interface IOutboxStore
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);
    Task<IEnumerable<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);
    Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default);
}

// Inbox 存储（接收端）
public interface IInboxStore
{
    Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default);
    Task AddAsync(InboxMessage message, CancellationToken cancellationToken = default);
}
```

**实现方式**:
- `Catga.Persistence.InMemory`: 内存存储（开发/测试）
- `Catga.Persistence.Redis`: Redis Hash/Sorted Set
- `Catga.Persistence.Nats`: NATS JetStream

### 5. Outbox/Inbox 模式

**Outbox 模式** (保证消息至少发送一次):
```
1. 业务逻辑执行
2. 在同一个事务中保存业务数据 + Outbox 消息
3. 提交事务（原子操作）
4. 后台任务轮询 Outbox
5. 发送消息到消息队列
6. 标记为已发送
```

**Inbox 模式** (保证消息至多处理一次):
```
1. 接收消息
2. 检查 Inbox 是否已存在（幂等性检查）
3. 如果存在则跳过
4. 如果不存在则处理消息
5. 在同一个事务中保存业务数据 + Inbox 记录
6. 提交事务
```

---

<a id="architecture"></a>
## 架构设计 {#architecture}

### 项目结构

```
Catga/
├── src/
│   ├── Catga/                          # 核心库
│   │   ├── ICatgaMediator.cs          # 核心接口
│   │   ├── IRequest.cs                # 请求接口
│   │   ├── INotification.cs           # 通知接口
│   │   ├── Observability/             # 可观测性
│   │   └── Serialization/             # 序列化抽象
│   │
│   ├── Catga.Transport.InMemory/      # 内存传输
│   ├── Catga.Transport.Redis/         # Redis 传输
│   ├── Catga.Transport.Nats/          # NATS 传输
│   │
│   ├── Catga.Persistence.InMemory/    # 内存持久化
│   ├── Catga.Persistence.Redis/       # Redis 持久化
│   ├── Catga.Persistence.Nats/        # NATS 持久化
│   │
│   ├── Catga.SourceGenerator/         # 源代码生成器
│   └── Catga.Hosting.Aspire/          # .NET Aspire 集成
│
├── examples/                           # 示例项目
│   ├── MinimalApi/                    # 最小 API 示例
│   └── DistributedSystem/             # 分布式系统示例
│
└── docs/                               # 文档
```

### 依赖关系

```
应用层 (Your App)
    ↓
Catga.Transport.* / Catga.Persistence.*
    ↓
Catga (核心库)
```

### 库的对等关系

**重要**: InMemory、Redis、NATS 三种实现是对等的，没有继承关系：

```
Catga.Transport.InMemory  ←┐
Catga.Transport.Redis     ←┼→ 都实现 IMessageTransport
Catga.Transport.Nats      ←┘

Catga.Persistence.InMemory  ←┐
Catga.Persistence.Redis     ←┼→ 都实现 IEventStore/IOutboxStore/IInboxStore
Catga.Persistence.Nats      ←┘
```

---

<a id="examples"></a>
## 使用示例 {#examples}

### 1. 基本配置

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 注册 Catga + InMemory
builder.Services.AddCatga()
    .AddInMemoryTransport()      // 内存传输
    .AddInMemoryPersistence();   // 内存持久化

// 或者使用 Redis
builder.Services.AddCatga()
    .AddRedisTransport(options =>
    {
        options.Configuration = "localhost:6379";
        options.DefaultQoS = QoSLevel.QoS1; // QoS0: Pub/Sub, QoS1: Streams
    })
    .AddRedisPersistence(options =>
    {
        options.Configuration = "localhost:6379";
    });

// 或者使用 NATS
builder.Services.AddCatga()
    .AddNatsTransport(options =>
    {
        options.Url = "nats://localhost:4222";
    })
    .AddNatsPersistence(options =>
    {
        options.Url = "nats://localhost:4222";
        options.StreamName = "CATGA_EVENTS";
    });

var app = builder.Build();
app.Run();
```

### 2. 定义消息

```csharp
// 命令（修改状态）
public record CreateOrderCommand(Guid OrderId, string ProductName, decimal Amount)
    : IRequest<CreateOrderResponse>;

public record CreateOrderResponse(bool Success, string OrderNumber);

// 查询（读取数据）
public record GetOrderQuery(Guid OrderId)
    : IRequest<OrderDto>;

public record OrderDto(Guid Id, string ProductName, decimal Amount, string Status);

// 事件（已发生的事实）
public record OrderCreatedEvent(Guid OrderId, string ProductName, decimal Amount)
    : INotification;

public record OrderCancelledEvent(Guid OrderId, string Reason)
    : INotification;
```

### 3. 实现处理器

```csharp
// 命令处理器
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    private readonly ICatgaMediator _mediator;
    private readonly IOrderRepository _repository;

    public CreateOrderCommandHandler(ICatgaMediator mediator, IOrderRepository repository)
    {
        _mediator = mediator;
        _repository = repository;
    }

    public async Task<CreateOrderResponse> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // 1. 验证
        if (request.Amount <= 0)
            return new CreateOrderResponse(false, string.Empty);

        // 2. 创建订单
        var order = new Order
        {
            Id = request.OrderId,
            ProductName = request.ProductName,
            Amount = request.Amount,
            Status = "Created"
        };

        // 3. 保存到数据库
        await _repository.SaveAsync(order, cancellationToken);

        // 4. 发布事件（其他服务可以订阅）
        await _mediator.PublishAsync(
            new OrderCreatedEvent(order.Id, order.ProductName, order.Amount),
            cancellationToken);

        return new CreateOrderResponse(true, order.OrderNumber);
    }
}

// 查询处理器
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly IOrderRepository _repository;

    public GetOrderQueryHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<OrderDto> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);

        return new OrderDto(order.Id, order.ProductName, order.Amount, order.Status);
    }
}

// 事件处理器（可以有多个）
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public OrderCreatedEventHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // 发送邮件通知
        await _emailService.SendOrderConfirmationAsync(
            @event.OrderId,
            @event.ProductName,
            cancellationToken);
    }
}

// 另一个事件处理器
public class OrderCreatedInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryService _inventoryService;

    public OrderCreatedInventoryHandler(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // 更新库存
        await _inventoryService.ReserveStockAsync(
            @event.ProductName,
            cancellationToken);
    }
}
```

### 4. 使用 Mediator

```csharp
// 在 Controller/API 中使用
public class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrdersController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    // 发送命令
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            request.ProductName,
            request.Amount);

        var response = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(
            command,
            HttpContext.RequestAborted);

        return response.Success
            ? Ok(new { orderNumber = response.OrderNumber })
            : BadRequest();
    }

    // 发送查询
    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetOrder(Guid orderId)
    {
        var query = new GetOrderQuery(orderId);

        var result = await _mediator.SendAsync<GetOrderQuery, OrderDto>(
            query,
            HttpContext.RequestAborted);

        return result != null
            ? Ok(result)
            : NotFound();
    }

    // 发布事件
    [HttpPost("{orderId}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid orderId, [FromBody] string reason)
    {
        var @event = new OrderCancelledEvent(orderId, reason);

        await _mediator.PublishAsync(@event, HttpContext.RequestAborted);

        return Accepted();
    }
}
```

### 5. 使用 Source Generator 自动注册

```csharp
// 添加特性（可选）
[CatgaHandler(Lifetime = ServiceLifetime.Scoped)]
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    // ...
}

// 在 Program.cs 中自动注册
builder.Services.AddCatgaServices(); // Source Generator 自动生成的扩展方法
```

---

<a id="important-notes"></a>
## 重要注意事项 {#important-notes}

### ⚠️ 关键约束

1. **序列化抽象**:
   - ❌ **禁止**直接使用 `System.Text.Json.JsonSerializer`
   - ✅ **必须**使用 `IMessageSerializer` 接口
   - 原因: 支持 AOT，支持多种序列化器（JSON、MemoryPack 等）

   ```csharp
   // ❌ 错误
   var json = JsonSerializer.Serialize(message);

   // ✅ 正确
   private readonly IMessageSerializer _serializer;
   var bytes = await _serializer.SerializeAsync(message, cancellationToken);
   ```

2. **异步/等待**:
   - ❌ **禁止**不必要的 `async void`
   - ✅ **必须**使用 `async Task` 并正确 `await`
   - 例外: 只有在真的不需要等待时才省略 `await`

   ```csharp
   // ❌ 错误
   public async Task ProcessAsync()
   {
       Task.Run(() => DoWork()); // 没有 await，fire-and-forget
   }

   // ✅ 正确
   public async Task ProcessAsync()
   {
       await Task.Run(() => DoWork()); // 正确等待
   }
   ```

3. **库的对等性**:
   - InMemory、Redis、NATS 是**对等**的，不是继承关系
   - 每个库都是**独立**实现接口
   - 不能混用不同库的具体类型

4. **TransportContext.Metadata**:
   - 用于传递元数据（如 CorrelationId、TraceContext）
   - **不是** Headers（之前版本的命名）
   - 使用 `context.Metadata["key"]` 访问

5. **QoS 级别** (Redis):
   - QoS 0: Pub/Sub（至多一次，可能丢失）
   - QoS 1: Streams（至少一次，可能重复）
   - 根据业务需求选择

6. **NATS JetStream**:
   - 持久化使用 JetStream Streams（不是 KV Store）
   - KV Store 仅用于简单的键值存储
   - Streams 支持完整的事件溯源

### ⚠️ AOT 兼容性

所有代码必须支持 Native AOT：

1. **避免反射**:
   ```csharp
   // ❌ 错误
   var type = Type.GetType("MyNamespace.MyClass");

   // ✅ 正确 - 使用泛型
   var instance = GetService<MyClass>();
   ```

2. **使用 Source Generator**:
   - 编译时生成代码
   - 避免运行时反射

3. **序列化使用 JsonSerializerContext**:
   ```csharp
   [JsonSerializable(typeof(CreateOrderCommand))]
   public partial class AppJsonContext : JsonSerializerContext { }
   ```

### ⚠️ 内存优化

1. **使用 ArrayPool**:
   ```csharp
   // ✅ 正确
   var buffer = ArrayPool<byte>.Shared.Rent(size);
   try
   {
       // 使用 buffer
   }
   finally
   {
       ArrayPool<byte>.Shared.Return(buffer);
   }
   ```

2. **使用 Span<T>**:
   ```csharp
   // ✅ 零拷贝操作
   public void Process(ReadOnlySpan<byte> data)
   {
       // 处理数据
   }
   ```

3. **FusionCache 配置**:
   - InMemory 实现使用 FusionCache
   - **禁用** Fail-safe 机制（内存场景不需要）
   - 配置合理的过期时间

---

<a id="best-practices"></a>
## 最佳实践 {#best-practices}

### 1. 命名规范

```csharp
// 命令: 动词 + 名词 + Command
CreateOrderCommand
UpdateUserCommand
DeleteProductCommand

// 查询: Get/List/Find + 名词 + Query
GetOrderQuery
ListUsersQuery
FindProductsByNameQuery

// 事件: 名词 + 过去式 + Event
OrderCreatedEvent
UserUpdatedEvent
ProductDeletedEvent

// 响应: 名词 + Response
CreateOrderResponse
UpdateUserResponse
```

### 2. 错误处理

```csharp
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    public async Task<CreateOrderResponse> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 验证输入
            if (request.Amount <= 0)
                throw new ValidationException("Amount must be positive");

            // 2. 业务逻辑
            var order = await _repository.CreateAsync(request, cancellationToken);

            // 3. 发布事件
            await _mediator.PublishAsync(
                new OrderCreatedEvent(order.Id, order.ProductName, order.Amount),
                cancellationToken);

            return new CreateOrderResponse(true, order.OrderNumber);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for order creation");
            return new CreateOrderResponse(false, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order");
            throw; // 让上层处理
        }
    }
}
```

### 3. 幂等性设计

```csharp
// 使用 Inbox 模式确保幂等性
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInboxStore _inboxStore;

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // 检查是否已处理
        var messageId = $"OrderCreated_{@event.OrderId}";
        if (await _inboxStore.ExistsAsync(messageId, cancellationToken))
        {
            _logger.LogInformation("Message {MessageId} already processed", messageId);
            return; // 跳过重复消息
        }

        // 处理业务逻辑
        await ProcessOrderAsync(@event, cancellationToken);

        // 记录到 Inbox
        await _inboxStore.AddAsync(new InboxMessage
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow
        }, cancellationToken);
    }
}
```

### 4. 分布式追踪

```csharp
using Catga.Observability;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    public async Task<CreateOrderResponse> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // 创建 Activity（自动使用 CatgaActivitySource）
        using var activity = CatgaActivitySource.Source.StartActivity("CreateOrder");

        activity?.SetTag(CatgaActivitySource.Tags.AggregateId, request.OrderId);
        activity?.SetTag(CatgaActivitySource.Tags.AggregateType, "Order");

        try
        {
            var order = await _repository.CreateAsync(request, cancellationToken);

            // 标记成功
            activity?.SetSuccess(true, order.OrderNumber);

            return new CreateOrderResponse(true, order.OrderNumber);
        }
        catch (Exception ex)
        {
            // 记录错误
            activity?.SetError(ex);
            throw;
        }
    }
}
```

### 5. 使用 Pipeline Behaviors

```csharp
// 日志 Behavior
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Handling {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();

            _logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Failed handling {RequestName} after {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

// 注册
builder.Services.AddCatga(options =>
{
    options.AddBehavior(typeof(LoggingBehavior<,>));
});
```

---

<a id="common-errors"></a>
## 常见错误 {#common-errors}

### ❌ 错误 1: 直接使用 JsonSerializer

```csharp
// ❌ 错误
public class MyTransport : IMessageTransport
{
    public async Task PublishAsync<TMessage>(TMessage message, ...)
    {
        var json = JsonSerializer.Serialize(message); // 不支持 AOT
        await SendAsync(json);
    }
}

// ✅ 正确
public class MyTransport : IMessageTransport
{
    private readonly IMessageSerializer _serializer;

    public MyTransport(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    public async Task PublishAsync<TMessage>(TMessage message, ...)
    {
        var bytes = await _serializer.SerializeAsync(message, cancellationToken);
        await SendAsync(bytes);
    }
}
```

### ❌ 错误 2: 忘记 await

```csharp
// ❌ 错误
public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
{
    _mediator.PublishAsync(new InventoryReservedEvent(...)); // 没有 await
    // CS4014 warning
}

// ✅ 正确
public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
{
    await _mediator.PublishAsync(new InventoryReservedEvent(...), cancellationToken);
}
```

### ❌ 错误 3: 混用 Headers 和 Metadata

```csharp
// ❌ 错误（旧版本）
context.Headers["CorrelationId"] = correlationId;

// ✅ 正确（当前版本）
context.Metadata["CorrelationId"] = correlationId;
```

### ❌ 错误 4: 在事件处理器中返回值

```csharp
// ❌ 错误 - 事件处理器不应该返回值
public record OrderCreatedEvent : IRequest<bool> { } // 错误，应该是 INotification

// ✅ 正确
public record OrderCreatedEvent : INotification { }
```

### ❌ 错误 5: 多个命令处理器

```csharp
// ❌ 错误 - 一个命令只能有一个处理器
public class CreateOrderCommandHandler1 : IRequestHandler<CreateOrderCommand, CreateOrderResponse> { }
public class CreateOrderCommandHandler2 : IRequestHandler<CreateOrderCommand, CreateOrderResponse> { }
// 会抛出异常

// ✅ 正确 - 一个命令一个处理器
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse> { }

// ✅ 正确 - 事件可以有多个处理器
public class OrderCreatedEventHandler1 : IEventHandler<OrderCreatedEvent> { }
public class OrderCreatedEventHandler2 : IEventHandler<OrderCreatedEvent> { }
```

---

<a id="performance-optimization"></a>
## 性能优化 {#performance-optimization}

### 1. 批量发布

```csharp
// ❌ 低效 - 逐个发布
foreach (var @event in events)
{
    await _mediator.PublishAsync(@event, cancellationToken);
}

// ✅ 高效 - 批量发布
await _mediator.PublishBatchAsync(events, cancellationToken);
```

### 2. 使用 ValueTask

```csharp
// ✅ 避免分配
public ValueTask<OrderDto> GetFromCacheAsync(Guid orderId)
{
    if (_cache.TryGetValue(orderId, out var order))
    {
        return new ValueTask<OrderDto>(order); // 同步完成，无分配
    }

    return new ValueTask<OrderDto>(LoadFromDatabaseAsync(orderId)); // 异步
}
```

### 3. 配置 FusionCache

```csharp
builder.Services.AddCatga()
    .AddInMemoryPersistence(options =>
    {
        options.CacheOptions = new FusionCacheOptions
        {
            DefaultEntryOptions = new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(30), // 缓存 30 分钟
                Size = 1, // 每个条目的大小（用于 LRU）
                Priority = CacheItemPriority.Normal
            }
        };
    });
```

### 4. 使用连接池

```csharp
// Redis 连接池配置
builder.Services.AddRedisTransport(options =>
{
    options.Configuration = "localhost:6379";
    options.ConfigurationOptions = new ConfigurationOptions
    {
        ConnectRetry = 3,
        ConnectTimeout = 5000,
        SyncTimeout = 5000,
        AbortOnConnectFail = false,
        // 连接池配置
        KeepAlive = 60
    };
});
```

---

<a id="troubleshooting"></a>
## 故障排查 {#troubleshooting}

### 问题 1: 消息没有被处理

**可能原因**:
1. 处理器没有注册
2. 传输层没有正确配置
3. 消息类型不匹配

**解决方案**:
```csharp
// 1. 检查处理器注册
builder.Services.AddCatgaServices(); // Source Generator
// 或手动注册
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResponse>, CreateOrderCommandHandler>();

// 2. 检查传输层配置
builder.Services.AddInMemoryTransport(); // 确保已注册

// 3. 启用日志
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});
```

### 问题 2: AOT 编译失败

**可能原因**:
1. 使用了反射
2. 使用了动态代码生成
3. 序列化没有使用 JsonSerializerContext

**解决方案**:
```csharp
// 1. 定义 JsonSerializerContext
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(CreateOrderResponse))]
[JsonSerializable(typeof(OrderCreatedEvent))]
public partial class AppJsonContext : JsonSerializerContext { }

// 2. 配置序列化器
builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// 3. 发布时启用 AOT
dotnet publish -r win-x64 -c Release /p:PublishAot=true
```

### 问题 3: 内存泄漏

**可能原因**:
1. 事件处理器持有大对象
2. 缓存没有过期策略
3. 订阅没有取消

**解决方案**:
```csharp
// 1. 使用 IDisposable 清理资源
public class MyEventHandler : IEventHandler<OrderCreatedEvent>, IDisposable
{
    public void Dispose()
    {
        // 清理资源
    }
}

// 2. 配置缓存过期
options.CacheOptions = new FusionCacheOptions
{
    DefaultEntryOptions = new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(30) // 设置过期时间
    }
};

// 3. 正确取消订阅
var cts = new CancellationTokenSource();
await transport.SubscribeAsync<OrderCreatedEvent>(handler, cts.Token);
// 完成后
cts.Cancel();
```

### 问题 4: 分布式追踪不工作

**可能原因**:
1. OpenTelemetry 没有配置
2. ActivitySource 没有订阅
3. TraceContext 没有传播

**解决方案**:
```csharp
// 在应用层配置 OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Catga.Framework") // 订阅 Catga 的 ActivitySource
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:4317");
            });
    });
```

---

## 快速参考

### 安装包

```bash
# 核心库
dotnet add package Catga

# 传输层（选择一个或多个）
dotnet add package Catga.Transport.InMemory
dotnet add package Catga.Transport.Redis
dotnet add package Catga.Transport.Nats

# 持久化层（选择一个或多个）
dotnet add package Catga.Persistence.InMemory
dotnet add package Catga.Persistence.Redis
dotnet add package Catga.Persistence.Nats

# 可选包
dotnet add package Catga.SourceGenerator        # 源代码生成器
dotnet add package Catga.Hosting.Aspire         # .NET Aspire 集成
```

### 常用命令

```bash
# 运行示例
cd examples/MinimalApi
dotnet run

# 构建项目
dotnet build

# 运行测试
dotnet test

# 发布 AOT
dotnet publish -r win-x64 -c Release /p:PublishAot=true

# 生成文档
docfx docfx.json --serve
```

### 有用的链接

- GitHub: https://github.com/Cricle/Catga
- 文档: https://cricle.github.io/Catga/
- NuGet: https://www.nuget.org/packages/Catga
- 示例: https://github.com/Cricle/Catga/tree/master/examples

---

## 总结

### 核心原则

1. **CQRS**: 分离命令和查询
2. **事件驱动**: 使用事件解耦系统
3. **Outbox/Inbox**: 保证消息可靠性
4. **AOT 兼容**: 避免反射，支持 Native AOT
5. **高性能**: 零分配，使用 Span/ArrayPool
6. **可观测**: 分布式追踪和指标

### 记住

- ✅ 使用 `IMessageSerializer` 而不是 `JsonSerializer`
- ✅ 总是 `await` 异步方法
- ✅ 命令一对一，事件一对多
- ✅ 使用 Outbox/Inbox 保证可靠性
- ✅ 配置分布式追踪
- ✅ InMemory/Redis/NATS 是对等的

### 下一步

1. 阅读完整文档: `docs/`
2. 运行示例项目: `examples/`
3. 查看最佳实践: `README.md`
4. 配置 OpenTelemetry
5. 实现你的第一个 CQRS 应用！

---

**Happy Coding with Catga!** 🚀



