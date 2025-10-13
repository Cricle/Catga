# Catga 架构设计

> 深入了解 Catga 的架构设计和实现原理

[返回主文档](../../README.md)

---

## 📐 总体架构

### 层次结构

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
│              (Your Business Logic & Handlers)               │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                 Integration Layer (Optional)                │
│                     Catga.AspNetCore                        │
│        • HTTP Endpoints  • Health Checks  • Swagger         │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                   Orchestration Layer                       │
│                      Catga.InMemory                         │
│    • CatgaMediator  • Pipeline  • FastPath  • Behaviors     │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                     Core Abstractions                       │
│                        Catga (Core)                         │
│   • Interfaces  • Message Types  • Result Types  • Common   │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                    Code Generation Layer                    │
│                   Catga.SourceGenerator                     │
│         • Handler Registration  • Type Caching  • AOT       │
└─────────────────────────────────────────────────────────────┘

              Distributed Extensions (Optional)
┌──────────────────┬────────────────────┬───────────────────┐
│   Discovery      │     Transport      │   Persistence     │
├──────────────────┼────────────────────┼───────────────────┤
│ • Nats           │ • Nats             │ • Redis Outbox    │
│ • Redis          │ • Redis Streams    │ • Redis Inbox     │
│ • Node Registry  │ • InMemory         │ • Redis Cache     │
│ • Heartbeat      │ • RPC              │ • Idempotency     │
└──────────────────┴────────────────────┴───────────────────┘
```

---

## 🎯 核心模块

### 1. Catga (Core)

核心抽象层，定义所有接口和基础类型。

**职责：**
- 定义消息接口 (`IRequest`, `IEvent`, `IMessage`)
- 定义 Handler 接口 (`IRequestHandler`, `IEventHandler`)
- 定义 Pipeline 接口 (`IPipelineBehavior`)
- 定义传输接口 (`IMessageTransport`)
- 定义结果类型 (`CatgaResult<T>`)
- 提供公共工具 (`ArrayPoolHelper`, `TypeNameCache`)

**关键设计：**
- 零反射：所有泛型静态缓存
- 零分配：使用 `readonly struct` 和 `ArrayPool`
- AOT 友好：无动态代码生成

```csharp
// Message abstractions
public interface IRequest<TResponse> { }
public interface IEvent { }
public interface IMessage
{
    string MessageId { get; }
    string? CorrelationId { get; }
    QualityOfService QoS { get; }
}

// Handler abstractions
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

// Result type (zero-allocation struct)
public readonly struct CatgaResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }
    public ResultMetadata? Metadata { get; init; }
}
```

---

### 2. Catga.InMemory

生产级实现，提供高性能的 Mediator 和消息处理。

**职责：**
- `CatgaMediator`: 核心调度器
- `InMemoryMessageTransport`: 进程内消息传输
- Pipeline Behaviors: 日志、追踪、验证、幂等性
- Stores: Outbox, Inbox, Idempotency

**关键特性：**
- **Fast Path**: 无 Behavior 时直接调用 Handler，零开销
- **Lock-Free**: 使用 `ConcurrentDictionary` 和 `ImmutableList`
- **Zero-Allocation**: 关键路径使用 `ArrayPool` 和 `Span<T>`
- **Observability**: 内置 ActivitySource 和 Metrics

```csharp
public class CatgaMediator : ICatgaMediator
{
    // Fast path for requests without behaviors
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(_serviceProvider);
        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorsList = behaviors as IList<...> ?? behaviors.ToList();

        // Fast path: no behaviors
        if (FastPath.CanUseFastPath(behaviorsList.Count))
            return await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken);

        // Pipeline execution
        return await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);
    }
}
```

---

### 3. Catga.SourceGenerator

编译时代码生成，消除反射。

**职责：**
- Handler 注册代码生成
- 类型缓存生成
- AOT 元数据生成

**生成的代码：**

```csharp
// Auto-generated by Catga.SourceGenerator
public static class CatgaHandlerRegistration
{
    public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)
    {
        // Command handlers
        services.AddTransient<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
        services.AddTransient<IRequestHandler<GetOrder, Order>, GetOrderHandler>();

        // Event handlers
        services.AddTransient<IEventHandler<OrderCreated>, OrderCreatedHandler>();
        services.AddTransient<IEventHandler<OrderCreated>, SendEmailHandler>();

        return services;
    }
}
```

**优势：**
- 90x 性能提升（45ms → 0.5ms）
- 100% AOT 兼容
- 编译时错误检查

---

### 4. Catga.AspNetCore

ASP.NET Core 集成层。

**职责：**
- HTTP 端点自动生成
- 错误状态码映射
- Swagger/OpenAPI 集成
- 健康检查

**端点生成：**

```csharp
app.MapCatgaEndpoints();

// Generates:
// POST /catga/command/CreateOrder
// POST /catga/query/GetOrder
// POST /catga/event/OrderCreated
// GET  /catga/health
// GET  /catga/nodes
```

---

## 🔄 消息流转

### Request (Command/Query) 流程

```
┌──────────────┐
│   Client     │
│  SendAsync   │
└──────┬───────┘
       │
       ↓
┌──────────────────────────────────────────────────────────┐
│                    CatgaMediator                         │
│  1. Get handler from cache (zero reflection)             │
│  2. Get behaviors from DI                                │
│  3. Check Fast Path eligibility                          │
└──────┬───────────────────────────────────┬───────────────┘
       │                                   │
       ↓ (No Behaviors)                    ↓ (Has Behaviors)
┌──────────────┐                    ┌─────────────────────┐
│  Fast Path   │                    │  Pipeline Executor  │
│  Direct Call │                    │  • LoggingBehavior  │
└──────┬───────┘                    │  • TracingBehavior  │
       │                            │  • Idempotency      │
       │                            │  • Validation       │
       │                            └─────────┬───────────┘
       │                                      │
       └──────────────┬───────────────────────┘
                      ↓
              ┌───────────────┐
              │    Handler    │
              │  HandleAsync  │
              └───────┬───────┘
                      │
                      ↓
              ┌───────────────┐
              │ CatgaResult<T>│
              └───────────────┘
```

### Event 流程

```
┌──────────────┐
│   Client     │
│ PublishAsync │
└──────┬───────┘
       │
       ↓
┌──────────────────────────────────────┐
│         CatgaMediator                │
│  1. Get all event handlers           │
│  2. Fire to all handlers in parallel │
└──────┬───────────────────────────────┘
       │
       ↓
┌─────────────────────────────────┐
│  Parallel Handler Execution     │
│  ┌─────────┐  ┌─────────┐       │
│  │Handler 1│  │Handler 2│  ...  │
│  └─────────┘  └─────────┘       │
└─────────────────────────────────┘
```

---

## 🚀 性能优化

### 1. 零反射设计

**问题：** 反射慢且不兼容 AOT

**解决方案：**

```csharp
// ❌ Before: Reflection (slow)
var typeName = typeof(TMessage).Name;
var handlers = GetHandlers(typeof(TMessage));

// ✅ After: Static generic cache (fast)
var typeName = TypeNameCache<TMessage>.Name;
var handlers = TypedSubscribers<TMessage>.Handlers;
```

**优势：**
- 类型名访问：25ns → 1ns (25x)
- Handler 查找：50ns → 5ns (10x)
- AOT 友好

### 2. 零分配设计

**技术：**
- `readonly struct` for data transfer objects
- `ArrayPool<T>` for temporary arrays
- `Span<T>` and `Memory<T>` for buffers
- `ValueTask` for async operations

```csharp
// ❌ Before: Heap allocation
var tasks = new Task[handlers.Count];
for (int i = 0; i < handlers.Count; i++)
    tasks[i] = handlers[i].HandleAsync(...);
await Task.WhenAll(tasks);

// ✅ After: ArrayPool (zero allocation)
using var rented = ArrayPoolHelper.RentOrAllocate<Task>(handlers.Count);
for (int i = 0; i < handlers.Count; i++)
    rented.Array[i] = handlers[i].HandleAsync(...);
await Task.WhenAll(rented.AsSpan().ToArray());
```

### 3. Fast Path 优化

**场景：** 无 Behavior 时，直接调用 Handler

```csharp
public static class FastPath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanUseFastPath(int behaviorCount) => behaviorCount == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteRequestDirectAsync<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        return await handler.HandleAsync(request, cancellationToken);
    }
}
```

**优势：**
- 零开销调用
- Inlining 优化
- 热路径最优

### 4. 锁自由设计

**数据结构：**
- `ConcurrentDictionary<TKey, TValue>` - 线程安全字典
- `ImmutableList<T>` - 不可变列表
- `Interlocked` 操作 - 原子操作

```csharp
// Lock-free sharded idempotency store
public class ShardedIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (DateTime, Type?, string?)>[] _shards;

    private ConcurrentDictionary<string, (DateTime, Type?, string?)> GetShard(string messageId)
        => _shards[messageId.GetHashCode() & (_shardCount - 1)];

    public Task<bool> HasBeenProcessedAsync(string messageId, ...)
    {
        var shard = GetShard(messageId);
        return Task.FromResult(shard.ContainsKey(messageId));
    }
}
```

---

## 🌐 分布式架构

### 节点拓扑

```
                  NATS/Redis Cluster
                         │
        ┌────────────────┼────────────────┐
        │                │                │
   ┌────▼────┐      ┌────▼────┐     ┌────▼────┐
   │ Node 1  │      │ Node 2  │     │ Node 3  │
   │ (Order) │      │ (User)  │     │ (Pay)   │
   └─────────┘      └─────────┘     └─────────┘
        │                │                │
        └────────────────┼────────────────┘
                         │
              Node Discovery & Heartbeat
```

### 消息路由策略

**1. Broadcast (广播)**

所有节点都接收消息

```csharp
services.AddCatga()
    .UseDistributedMediator(options =>
    {
        options.RoutingStrategy = RoutingStrategy.Broadcast;
    });
```

**2. Hash (哈希)**

根据消息 ID 哈希到特定节点

```csharp
options.RoutingStrategy = RoutingStrategy.Hash;
options.HashSelector = message => message.MessageId;
```

**3. RoundRobin (轮询)**

依次分配给各节点

```csharp
options.RoutingStrategy = RoutingStrategy.RoundRobin;
```

**4. Priority (优先级)**

根据节点优先级选择

```csharp
options.RoutingStrategy = RoutingStrategy.Priority;
options.PrioritySelector = node => node.Metadata["priority"];
```

---

## 🔒 可靠性保证

### 1. QoS (Quality of Service)

| Level | Guarantee | Use Case |
|-------|-----------|----------|
| `AtMostOnce` | Fire-and-forget | Logging, Analytics |
| `AtLeastOnce` | Retry until success | Orders, Payments |
| `ExactlyOnce` | Idempotent, only once | Critical transactions |

```csharp
public record CreateOrder(...) : IRequest<OrderResult>, IMessage
{
    public QualityOfService QoS { get; init; } = QualityOfService.AtLeastOnce;
}
```

### 2. Idempotency

**实现方式：**
- `MessageId` 作为幂等键
- 成功结果缓存
- 失败结果不缓存（允许重试）

```csharp
┌─────────────────────────────────────────────┐
│         Idempotency Flow                    │
├─────────────────────────────────────────────┤
│ 1. Check if MessageId processed             │
│    ├─ Yes → Return cached result            │
│    └─ No → Continue                         │
│                                             │
│ 2. Execute handler                          │
│                                             │
│ 3. If success → Cache result                │
│    If failure → Don't cache (allow retry)   │
└─────────────────────────────────────────────┘
```

### 3. Outbox Pattern

确保消息和数据库事务的一致性

```csharp
// 1. Save to database + Outbox in transaction
using var transaction = await dbContext.Database.BeginTransactionAsync();
await dbContext.Orders.AddAsync(order);
await outboxStore.AddAsync(new OrderCreated(order.Id));
await transaction.CommitAsync();

// 2. Background publisher sends from Outbox
// OutboxPublisher polls and publishes pending messages
```

### 4. Inbox Pattern

防止重复消息处理

```csharp
// 1. Check Inbox
if (await inboxStore.HasBeenProcessedAsync(messageId))
    return cached_result;

// 2. Lock message
if (!await inboxStore.TryAcquireLockAsync(messageId))
    return; // Another instance is processing

// 3. Process
var result = await handler.HandleAsync(message);

// 4. Mark as processed
await inboxStore.MarkAsProcessedAsync(messageId, result);
```

---

## 📊 可观测性

### Distributed Tracing

```csharp
using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Command.Execute");
activity?.SetTag("command_type", "CreateOrder");
activity?.SetTag("message_id", messageId);

try
{
    var result = await handler.HandleAsync(request);
    activity?.SetTag("success", result.IsSuccess);
    return result;
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    activity?.AddTag("exception.type", ex.GetType().Name);
    throw;
}
```

### Metrics

```csharp
// Counters
CatgaDiagnostics.CommandsExecuted.Add(1, new("command_type", "CreateOrder"));
CatgaDiagnostics.MessagesFailed.Add(1, new("message_type", "OrderCreated"));

// Histograms
CatgaDiagnostics.CommandDuration.Record(durationMs, new("command_type", "CreateOrder"));
CatgaDiagnostics.MessageSize.Record(sizeBytes, new("message_type", "OrderCreated"));

// Gauges
CatgaDiagnostics.IncrementActiveMessages();
CatgaDiagnostics.DecrementActiveMessages();
```

### Structured Logging

```csharp
// Zero-allocation logging with LoggerMessage source generation
[LoggerMessage(EventId = 1000, Level = LogLevel.Information,
    Message = "Command {CommandType} executing [MessageId={MessageId}]")]
public static partial void CommandExecuting(ILogger logger, string commandType, string? messageId);

// Usage
CatgaLog.CommandExecuting(logger, "CreateOrder", messageId);
```

---

## 🛡️ 设计原则

### 1. SOLID Principles

- **Single Responsibility**: 每个类只做一件事
- **Open/Closed**: 通过 Behavior 扩展，不修改核心
- **Liskov Substitution**: 接口定义清晰的契约
- **Interface Segregation**: 小而专注的接口
- **Dependency Inversion**: 依赖抽象，不依赖实现

### 2. Performance First

- 热路径零反射
- 关键路径零分配
- Fast Path 优化
- 锁自由设计

### 3. AOT Friendly

- 无动态代码生成
- 源码生成器
- 明确的泛型约束
- `DynamicallyAccessedMembers` 标注

### 4. DRY (Don't Repeat Yourself)

- 提取公共逻辑
- Helper 类统一实现
- 代码复用

---

## 📚 更多资源

- [CQRS 模式](./cqrs.md)
- [API 文档](../api/README.md)
- [性能基准](../../benchmarks/Catga.Benchmarks/)
- [源码生成器](../guides/source-generator-usage.md)

---

<div align="center">

[返回主文档](../../README.md) · [快速开始](../../QUICK-REFERENCE.md) · [示例](../../examples/)

**理解架构，用好 Catga！** 🚀

</div>
