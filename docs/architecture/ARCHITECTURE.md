# Catga 架构设计

## 🎯 设计目标

Catga 是一个**简洁、高性能、可插拔**的 .NET CQRS 框架，专注于核心功能，拒绝过度设计。

### 核心原则

1. **Simple > Perfect** - 简单优于完美
2. **Focused > Comprehensive** - 专注优于全面
3. **Fast > Feature-Rich** - 性能优于功能丰富

---

## 📁 项目结构

### 精简后的文件夹组织

从 **14 个文件夹精简至 6 个**，减少 57% 的导航复杂度：

```
src/Catga/
├── Abstractions/       (15 files) - 所有接口定义
├── Core/              (22 files) - 核心实现
├── DependencyInjection/ (3 files) - DI 扩展
├── Observability/      (4 files) - 监控集成
├── Pipeline/           (8 files) - 管道系统
├── Polyfills/          (2 files) - .NET 6 兼容
├── CatgaMediator.cs    - Mediator 实现
└── Serialization.cs    - 序列化基类
```

### Abstractions - 接口层

**职责**: 定义所有抽象接口

```
Abstractions/
├── ICatgaMediator.cs           # Mediator 接口
├── IRequest<T>, IEvent         # 消息契约 (MessageContracts.cs)
├── IRequestHandler<,>          # Handler 契约 (HandlerContracts.cs)
├── IMessageTransport.cs        # 传输层抽象
├── IEventStore.cs              # 事件存储抽象
├── IOutboxStore.cs             # Outbox 抽象
├── IInboxStore.cs              # Inbox 抽象
├── IIdempotencyStore.cs        # 幂等性存储抽象
├── IMessageSerializer.cs       # 序列化抽象
├── IPipelineBehavior<,>        # 管道行为抽象
└── IDistributedIdGenerator.cs  # ID 生成器抽象
```

**设计决策**:
- ✅ 所有接口集中管理
- ✅ 便于查找和理解依赖
- ✅ 支持多种实现插拔

### Core - 核心实现

**职责**: 框架核心逻辑和工具类

```
Core/
├── CatgaResult<T>.cs           # 结果类型（零分配）
├── ErrorCodes.cs               # 10 个核心错误码
├── SnowflakeIdGenerator.cs     # Snowflake ID 生成
├── HandlerCache.cs             # Handler 解析（直接委托 DI）
├── MemoryPoolManager.cs        # 内存池管理
├── PooledBufferWriter<T>.cs    # 池化缓冲区
├── ValidationHelper.cs         # 验证工具
├── BatchOperationHelper.cs     # 批量操作工具
├── MessageHelper.cs            # 消息辅助
├── MessageExtensions.cs        # 消息扩展方法
├── FastPath.cs                 # 快速路径优化
├── BaseBehavior.cs             # Behavior 基类
└── ...
```

**设计决策**:
- ✅ 删除过度缓存（HandlerCache 直接委托 DI）
- ✅ 简化错误处理（10 个核心错误码）
- ✅ 内存池优化（MemoryPool.Shared + ArrayPool.Shared）

### Pipeline - 管道系统

**职责**: 请求处理管道和行为

```
Pipeline/
├── PipelineExecutor.cs         # 管道执行器
└── Behaviors/
    ├── LoggingBehavior.cs      # 日志记录
    ├── ValidationBehavior.cs   # 参数验证
    ├── OutboxBehavior.cs       # Outbox 模式
    ├── InboxBehavior.cs        # Inbox 模式
    ├── IdempotencyBehavior.cs  # 幂等性处理
    ├── DistributedTracingBehavior.cs # 分布式追踪
    └── RetryBehavior.cs        # 重试逻辑
```

**执行顺序**:
1. DistributedTracingBehavior (追踪)
2. LoggingBehavior (日志)
3. ValidationBehavior (验证)
4. IdempotencyBehavior (幂等)
5. InboxBehavior (去重)
6. RetryBehavior (重试)
7. OutboxBehavior (可靠发送)
8. **Handler** (业务逻辑)

---

## 🏗️ 核心组件

### 1. CatgaMediator - 协调器

**职责**: 协调 Command/Query/Event 的分发和执行

```csharp
public sealed class CatgaMediator : ICatgaMediator
{
    // Command/Query - 返回结果
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        // 1. 解析 Handler
        var handler = ResolveHandler<TRequest, TResponse>();
        
        // 2. 解析 Behaviors
        var behaviors = ResolveBehaviors<TRequest, TResponse>();
        
        // 3. 构建管道
        var pipeline = BuildPipeline(handler, behaviors);
        
        // 4. 执行
        return await pipeline(request, cancellationToken);
    }

    // Event - 无返回值
    public async Task PublishAsync<TEvent>(
        TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // 1. 解析所有 EventHandlers
        var handlers = ResolveEventHandlers<TEvent>();
        
        // 2. 并行执行
        await Task.WhenAll(handlers.Select(h => h.HandleAsync(@event, cancellationToken)));
    }
}
```

**设计决策**:
- ✅ 使用 `ValueTask<T>` 减少分配
- ✅ Handler 直接从 DI 解析（不缓存）
- ✅ Event 并行广播
- ✅ 支持 FastPath 优化（无 Behavior 时直接调用）

### 2. CatgaResult<T> - 结果类型

**职责**: 统一的返回类型，避免异常

```csharp
public readonly record struct CatgaResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public string ErrorCode { get; init; }
    
    public static CatgaResult<T> Success(T value) 
        => new() { IsSuccess = true, Value = value };
    
    public static CatgaResult<T> Failure(string error, string errorCode = ErrorCodes.Unknown)
        => new() { IsSuccess = false, Error = error, ErrorCode = errorCode };
    
    public static CatgaResult<T> Failure(ErrorInfo errorInfo)
        => new() { IsSuccess = false, Error = errorInfo.Message, ErrorCode = errorInfo.Code };
}
```

**设计决策**:
- ✅ 使用 `readonly record struct` 零分配
- ✅ 删除 `ResultMetadata`（过度设计）
- ✅ 简化错误处理（只保留必要信息）

### 3. ErrorCodes - 错误语义

**从 50+ 精简至 10 个**:

```csharp
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string HandlerFailed = "HANDLER_FAILED";
    public const string PipelineFailed = "PIPELINE_FAILED";
    public const string PersistenceFailed = "PERSISTENCE_FAILED";
    public const string TransportFailed = "TRANSPORT_FAILED";
    public const string SerializationFailed = "SERIALIZATION_FAILED";
    public const string LockFailed = "LOCK_FAILED";
    public const string Timeout = "TIMEOUT";
    public const string Cancelled = "CANCELLED";
    public const string Unknown = "UNKNOWN";
}
```

**设计决策**:
- ✅ 覆盖 95% 的业务场景
- ✅ 明确的语义
- ✅ 易于扩展（应用层可自定义）

### 4. SnowflakeIdGenerator - 分布式 ID

**职责**: 生成分布式唯一 ID

```csharp
// 位布局 (64 bits)
// | 1 bit (unused) | 41 bits (timestamp) | 10 bits (workerId) | 12 bits (sequence) |

public sealed class SnowflakeIdGenerator : IDistributedIdGenerator
{
    public long NextId() // 零分配，~45 ns
    {
        // Pure CAS loop (lock-free)
        while (true)
        {
            var current = Volatile.Read(ref _lastState);
            var next = GenerateNext(current);
            if (Interlocked.CompareExchange(ref _lastState, next, current) == current)
                return next;
        }
    }
}
```

**特性**:
- ✅ Lock-Free（纯 CAS 循环）
- ✅ 高性能（~45 ns）
- ✅ 全局唯一（支持 1024 个 Worker）
- ✅ 时间递增（可排序）

---

## 🔌 可插拔架构

### 传输层 (IMessageTransport)

```
Catga.Transport.InMemory    - 进程内（开发/测试）
Catga.Transport.Redis       - Redis Pub/Sub & Streams
Catga.Transport.Nats        - NATS JetStream
```

**设计**:
- QoS 0 (At-Most-Once): Pub/Sub
- QoS 1 (At-Least-Once): Stream/JetStream

### 持久化层 (IEventStore, IOutboxStore, IInboxStore)

```
Catga.Persistence.InMemory  - 内存存储（开发/测试）
Catga.Persistence.Redis     - Redis 持久化
Catga.Persistence.Nats      - NATS KeyValue Store
```

### 序列化层 (IMessageSerializer)

```
Catga.Serialization.Json        - System.Text.Json (AOT 优化)
Catga.Serialization.MemoryPack  - 高性能二进制
```

---

## 🚫 删除的过度设计

为保持简洁，我们删除了以下未使用或过度设计的组件：

### 删除的抽象 (8个)

1. **IRpcClient / IRpcServer**
   - 理由: 未使用，CQRS 不需要 RPC
   
2. **IDistributedCache / ICacheable / CachingBehavior**
   - 理由: 过度设计，应用层可自行集成 Redis/FusionCache
   
3. **IDistributedLock / ILockHandle**
   - 理由: 过度设计，应用层可使用 Redlock.net
   
4. **IHealthCheck**
   - 理由: .NET 已有 `IHealthCheck` 接口
   
5. **AggregateRoot / ProjectionBase / CatgaTransactionBase**
   - 理由: 强制 DDD 架构，违反"非侵入"原则
   
6. **SafeRequestHandler**
   - 理由: 不必要的抽象，使用 `CatgaResult` 即可
   
7. **ResultMetadata**
   - 理由: 复杂度过高，`ErrorCode` 足够
   
8. **TracingBehavior**
   - 理由: 与 `DistributedTracingBehavior` 重复

### 简化的组件

1. **HandlerCache**
   - Before: 3 层缓存（ThreadStatic + ConcurrentDictionary + Statistics）
   - After: 直接委托给 DI 容器
   
2. **ErrorCodes**
   - Before: 50+ 错误码
   - After: 10 个核心错误码
   
3. **CatgaResult**
   - Before: 包含 Metadata, TraceId 等
   - After: 只保留 Value, Error, ErrorCode

---

## 🎯 设计权衡

### ✅ 保留的功能

| 功能 | 理由 |
|------|------|
| CQRS | 核心模式 |
| Outbox/Inbox | 分布式可靠性必需 |
| 幂等性 | 生产环境必需 |
| 分布式追踪 | 监控必需 |
| 管道行为 | 扩展性必需 |
| 可插拔传输/持久化 | 灵活性必需 |

### ❌ 删除的功能

| 功能 | 删除理由 |
|------|---------|
| RPC | 未使用，CQRS 不需要 |
| 分布式缓存 | 应用层自行集成 |
| 分布式锁 | 应用层自行集成 |
| DDD 基类 | 强制架构，违反原则 |
| SafeRequestHandler | 不必要的抽象 |
| ResultMetadata | 过度设计 |

---

## 📊 性能优化

### 内存优化

1. **使用 Span<T>**
   ```csharp
   // 零拷贝序列化
   public void Serialize<T>(T message, IBufferWriter<byte> writer);
   ```

2. **内存池化**
   ```csharp
   using var buffer = MemoryPoolManager.RentArray(256);
   using var bufferWriter = new PooledBufferWriter<byte>(128);
   ```

3. **零分配结果**
   ```csharp
   public readonly record struct CatgaResult<T> { }
   ```

### 热路径优化

1. **FastPath** - 无 Behavior 时直接调用
2. **ValueTask** - 避免 Task 分配
3. **AggressiveInlining** - 内联小方法
4. **Lock-Free** - SnowflakeIdGenerator 使用 CAS

---

## 🔄 演进策略

### 向后兼容

- 使用 `[Obsolete]` 标记过时 API
- 提供迁移指南
- 保留核心接口稳定

### 未来方向

1. **Source Generator 增强** - 编译时生成更多代码
2. **性能持续优化** - 目标 < 500 ns
3. **更多传输层** - Kafka, RabbitMQ
4. **云原生集成** - Dapr, YARP

---

## 📚 参考

- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [Snowflake ID](https://github.com/twitter-archive/snowflake/tree/snowflake-2010)
- [OpenTelemetry](https://opentelemetry.io/)

---

<div align="center">

**Philosophy: Simple > Perfect, Focused > Comprehensive, Fast > Feature-Rich**

</div>
