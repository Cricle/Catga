# 架构概览

Catga 是一个现代化的分布式应用框架，将 CQRS、事件驱动架构和分布式事务（Saga）集成在一个统一的编程模型中。

## 核心设计原则

### 1. 简单优先
- 最少配置，合理默认值
- 一致的 API 设计
- 清晰的命名约定

### 2. 高性能
- 无锁设计（原子操作 + ConcurrentDictionary）
- 零分配热路径
- 异步优先（全异步，零阻塞）
- 分片存储（减少锁竞争）

### 3. AOT 兼容
- 零反射
- 显式泛型注册
- 编译时类型检查
- 无 `object` 装箱

### 4. 可观测性
- 内置分布式追踪
- 结构化日志
- 死信队列
- 性能指标

## 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                        Application Layer                     │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │ Web API  │  │ gRPC     │  │ Console  │  │ Worker   │   │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘   │
└───────┼─────────────┼─────────────┼─────────────┼──────────┘
        │             │             │             │
        └─────────────┴─────────────┴─────────────┘
                          │
        ┌─────────────────▼─────────────────┐
        │      ITransitMediator             │
        │    (Central Message Router)       │
        └─────────────────┬─────────────────┘
                          │
        ┌─────────────────▼─────────────────┐
        │        Pipeline Behaviors         │
        │  ┌────────────────────────────┐   │
        │  │ Logging → Tracing →        │   │
        │  │ Validation → Idempotency → │   │
        │  │ Retry                      │   │
        │  └────────────────────────────┘   │
        └─────────────────┬─────────────────┘
                          │
        ┌─────────────────▼─────────────────┐
        │         Message Handlers          │
        │  ┌──────────┐  ┌──────────┐      │
        │  │ Command  │  │  Query   │      │
        │  │ Handlers │  │ Handlers │      │
        │  └──────────┘  └──────────┘      │
        │  ┌──────────┐  ┌──────────┐      │
        │  │  Event   │  │  CatGa   │      │
        │  │ Handlers │  │Executors │      │
        │  └──────────┘  └──────────┘      │
        └─────────────────┬─────────────────┘
                          │
        ┌─────────────────▼─────────────────┐
        │        Domain/Data Layer          │
        │  ┌──────────┐  ┌──────────┐      │
        │  │ Domain   │  │ Repos    │      │
        │  │ Services │  │          │      │
        │  └──────────┘  └──────────┘      │
        └───────────────────────────────────┘
```

## 核心组件

### 1. Mediator（中介者）

**职责**: 消息路由和协调

```csharp
public interface ITransitMediator
{
    // 发送请求并等待响应
    Task<TransitResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    // 发布事件到所有订阅者
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
```

**特点**:
- ✅ 单一入口
- ✅ 类型安全
- ✅ AOT 友好（显式泛型参数）
- ✅ 无锁实现

### 2. Messages（消息）

**消息类型层次**:

```
IMessage (标记接口)
│
├── IRequest<TResponse> (请求-响应)
│   │
│   ├── ICommand<TResult> (命令 - 修改状态)
│   │   └── 示例: CreateUserCommand, UpdateOrderCommand
│   │
│   └── IQuery<TResult> (查询 - 只读)
│       └── 示例: GetUserQuery, ListOrdersQuery
│
└── IEvent (事件 - 异步通知)
    └── 示例: UserCreatedEvent, OrderPlacedEvent
```

**设计原则**:
- 使用 `record` 类型（不可变）
- 清晰的命名（Query/Command/Event 后缀）
- 包含所有必要数据

### 3. Handlers（处理器）

**接口定义**:

```csharp
// 请求处理器（Command/Query）
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TransitResult<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}

// 事件处理器
public interface IEventHandler<TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken = default);
}
```

**特点**:
- 单一职责
- 独立测试
- 显式依赖注入

### 4. Pipeline Behaviors（管道行为）

**执行顺序**:

```
Request
  │
  ├─► LoggingBehavior (记录请求/响应)
  │     │
  │     ├─► TracingBehavior (分布式追踪)
  │     │     │
  │     │     ├─► ValidationBehavior (验证)
  │     │     │     │
  │     │     │     ├─► IdempotencyBehavior (去重)
  │     │     │     │     │
  │     │     │     │     ├─► RetryBehavior (重试)
  │     │     │     │     │     │
  │     │     │     │     │     ├─► Handler (实际处理)
  │     │     │     │     │     │
  │     │     │     │     │     └─► Response
  │     │     │     │     │
  │     │     │     │     └─► (缓存响应)
  │     │     │     │
  │     │     │     └─► (记录验证错误)
  │     │     │
  │     │     └─► (结束 Span)
  │     │
  │     └─► (记录日志)
  │
  └─► Response
```

**可扩展性**:

```csharp
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TransitResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<TransitResult<TResponse>>> next,
        CancellationToken cancellationToken = default);
}
```

### 5. CatGa（分布式事务）

**Saga 模式实现**:

```csharp
public interface ICatGaTransaction<TRequest, TResponse>
{
    // 前向操作
    Task<TResponse> ExecuteAsync(
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default);

    // 补偿操作
    Task CompensateAsync(
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default);
}
```

**流程**:

```
Step 1: Execute ──► Success ──► Step 2: Execute ──► Success ──► ...
   │                                │
   │ Failure                        │ Failure
   ▼                                ▼
Step 1: Compensate ◄───────── Step 2: Compensate
```

## 消息流程

### 命令流程

```
1. Client → ITransitMediator.SendAsync(CreateUserCommand)
              │
2. Pipeline → LoggingBehavior
              │
3. Pipeline → ValidationBehavior (验证 Name, Email)
              │
4. Pipeline → IdempotencyBehavior (检查重复)
              │
5. Handler  → CreateUserHandler
              │
              ├─► Save to DB
              ├─► Publish UserCreatedEvent
              │
6. Response ← TransitResult<UserId>
```

### 查询流程

```
1. Client → ITransitMediator.SendAsync(GetUserQuery)
              │
2. Pipeline → LoggingBehavior
              │
3. Pipeline → IdempotencyBehavior (缓存检查)
              │
4. Handler  → GetUserHandler
              │
              ├─► Load from DB/Cache
              │
5. Response ← TransitResult<User>
```

### 事件流程

```
1. Publisher → ITransitMediator.PublishAsync(UserCreatedEvent)
                │
                ├─► EventHandler1 (SendEmailHandler)
                ├─► EventHandler2 (UpdateStatisticsHandler)
                └─► EventHandler3 (NotifyAdminHandler)
                     │
                     └─► (并行执行)
```

## 弹性机制

### 1. 重试机制（Retry）

```csharp
options.EnableRetry = true;
options.MaxRetryAttempts = 3;
options.RetryDelayMs = 100; // 指数退避 + Jitter
```

### 2. 熔断器（Circuit Breaker）

```
┌─────────────┐
│   Closed    │ ─► 正常状态，允许所有请求
└──────┬──────┘
       │ 达到失败阈值
       ▼
┌─────────────┐
│    Open     │ ─► 快速失败，拒绝所有请求
└──────┬──────┘
       │ 超时后
       ▼
┌─────────────┐
│ Half-Open   │ ─► 允许少量请求测试
└─────────────┘
       │
       ├─► 成功 ──► Closed
       └─► 失败 ──► Open
```

### 3. 限流（Rate Limiting）

**令牌桶算法**:

```
Bucket (容量: N)
│
├─► 每秒补充 R 个令牌
├─► 请求消耗 1 个令牌
└─► 无令牌则拒绝
```

### 4. 死信队列（Dead Letter Queue）

```
Request → Handler
            │
            │ 重试失败
            ▼
     Dead Letter Queue
            │
            └─► 人工处理/重新发送
```

## 性能特性

### 无锁设计

```csharp
// 原子操作
private int _currentCount;
Interlocked.Increment(ref _currentCount);

// 并发字典（分段锁）
private readonly ConcurrentDictionary<K, V> _handlers;
```

### 分片存储

```csharp
// 幂等性存储（64 分片）
var shardIndex = messageId.GetHashCode() % 64;
var shard = _shards[shardIndex];
```

### 对象池

```csharp
// 复用 CancellationTokenSource
private static readonly ObjectPool<CTS> _ctsPool;
```

## 扩展点

### 1. 自定义 Behavior

```csharp
public class CachingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    // 实现缓存逻辑
}
```

### 2. 自定义传输

```csharp
public class RabbitMqTransport : ITransitMediator
{
    // 实现 RabbitMQ 消息发送
}
```

### 3. 自定义存储

```csharp
public class PostgresIdempotencyStore : IIdempotencyStore
{
    // 实现 PostgreSQL 存储
}
```

## 部署模式

### 1. 单体应用

```
┌────────────────────┐
│   Web Application  │
│  ┌──────────────┐  │
│  │  Mediator    │  │
│  │  (In-Memory) │  │
│  └──────────────┘  │
└────────────────────┘
```

### 2. 微服务

```
┌─────────┐      NATS       ┌─────────┐
│Service A│ ───────────────► │Service B│
│Mediator │                  │Mediator │
└─────────┘                  └─────────┘
     │                            │
     ▼                            ▼
  Database A                 Database B
```

### 3. 事件驱动

```
┌─────────┐
│Publisher│
└────┬────┘
     │
     ▼
┌────────┐     ┌─────────────┐
│  NATS  │────►│Subscriber 1 │
│  Pub   │     ├─────────────┤
│  Sub   │────►│Subscriber 2 │
└────────┘     ├─────────────┤
               │Subscriber N │
               └─────────────┘
```

## 下一步

- 📖 深入了解 [CQRS 模式](cqrs.md)
- 🔄 学习 [Pipeline 行为](pipeline-behaviors.md)
- 🌐 探索 [CatGa 分布式事务](catga-transactions.md)
- 🚀 查看 [性能优化指南](../guides/performance.md)

---

**Catga Architecture** - 简单、高性能、可扩展 ✨

