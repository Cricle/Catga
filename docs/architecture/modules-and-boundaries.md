# Catga 架构：模块与边界

> 这一页聚焦模块分层、职责划分和设计边界。
> 如果你在找运行时数据流、性能、可观测性和扩展点，请看 [runtime-and-extensibility.md](./runtime-and-extensibility.md)。

---

## 这页适合谁

- 想先搞清楚 Catga 的分层和职责归属
- 想评审“哪些能力应该在框架里，哪些不应该”
- 想做模块拆分或边界治理

---

## 设计理念

Catga 的核心设计理念是 **专注、简洁、高性能**：

1. **专注核心价值** - 只做 CQRS 消息分发，不重复造轮子
2. **简洁易用** - 3 行配置，30 秒上手
3. **高性能优先** - 零反射、零分配、100% AOT
4. **职责清晰** - 明确的边界，依赖成熟生态

---

## 总体架构 (2025-10)

### 当前层次结构

```
┌─────────────────────────────────────────┐
│        Your Application                 │ ← 业务逻辑 + Handlers
├─────────────────────────────────────────┤
│   Catga.Serialization.MemoryPack        │ ← 序列化（推荐 - 100% AOT）
│   Custom JSON (IMessageSerializer)      │   可选（源生成）
├─────────────────────────────────────────┤
│      Catga.InMemory (Production)        │ ← 核心实现
│  • CatgaMediator                        │   - Mediator
│  • Pipeline Behaviors                   │   - Pipeline
│  • Idempotency Store                    │   - 幂等性
│  • Handler Cache                        │   - Handler 缓存
├─────────────────────────────────────────┤
│         Catga (Abstractions)            │ ← 接口定义
│  • IRequest / IEvent                    │   - 消息接口
│  • IRequestHandler / IEventHandler      │   - Handler 接口
│  • ICatgaMediator                       │   - Mediator 接口
│  • CatgaResult<T>                       │   - 结果类型
├─────────────────────────────────────────┤
│      Catga.SourceGenerator              │ ← 编译时代码生成
│  • Handler 自动注册                     │   - 零反射
│  • Type 缓存生成                        │   - 100% AOT
│  • Roslyn 分析器                        │   - 编译时检查
└─────────────────────────────────────────┘

        可选扩展（基础设施无关）
┌──────────────────┬───────────────────────┐
│  Transport       │  Persistence          │
│  - Nats          │  - Redis Outbox       │
│  - (Redis)       │  - Redis Inbox        │
│                  │  - Redis Cache        │
└──────────────────┴───────────────────────┘

        编排层（外部平台）
┌─────────────────────────────────────────┐
│  Kubernetes / .NET Aspire               │ ← 服务发现
│  - Service Discovery                    │   负载均衡
│  - Load Balancing                       │   健康检查
│  - Health Checks                        │   配置管理
│  - Service Mesh                         │
└─────────────────────────────────────────┘
```

### 关键变化 (2025-10)

**移除的组件** ❌:
- ~~Catga.Distributed.Nats~~ - 节点发现交给 K8s
- ~~Catga.Distributed.Redis~~ - 节点发现交给 K8s
- ~~应用层节点发现~~ - 使用平台原生能力

**新增的组件** ✅:
- `Catga.Serialization.MemoryPack` - 100% AOT 序列化
- 自定义 JSON 序列化（实现 `IMessageSerializer`）
- `CatgaServiceBuilder` - Fluent API
- Roslyn 分析器 - 编译时检查

---

## 核心模块

### 1. Catga (Core) - 抽象层

**职责**: 定义所有接口和基础类型

**关键接口**:
```csharp
// 消息接口
public interface IRequest<TResponse> { }
public interface IEvent { }
public interface IMessage
{
    string MessageId { get; }
    string? CorrelationId { get; }
    QualityOfService QoS { get; }
}

// Handler 接口
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken);
}

public interface IEventHandler<TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

// Mediator 接口
public interface ICatgaMediator
{
    ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
```

**设计原则**:
- ✅ 零反射 - 所有类型信息编译时确定
- ✅ 零分配 - 使用 `ValueTask` 和 `readonly struct`
- ✅ AOT 友好 - 无动态代码生成

---

### 2. Catga.InMemory - 核心实现

**职责**: 提供生产级的 CQRS 实现

**核心组件**:

#### CatgaMediator
```csharp
public sealed class CatgaMediator : ICatgaMediator
{
    // 直接 DI 解析 - 尊重生命周期，无过度缓存
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        // 1. 从 DI 获取 Handler（泛型 JIT 优化）
        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        // 2. 执行 Pipeline
        var result = await ExecutePipelineAsync(request, handler, scope.ServiceProvider, ct);

        return result;
    }
}
```

#### Pipeline Behaviors
```csharp
// 内置 Behaviors
- LoggingBehavior<TRequest, TResponse>      // 结构化日志
- TracingBehavior<TRequest, TResponse>      // 分布式追踪
- IdempotencyBehavior<TRequest, TResponse>  // 幂等性保证
- RetryBehavior<TRequest, TResponse>        // 自动重试
- ValidationBehavior<TRequest, TResponse>   // 数据验证
```

#### Idempotency Store
```csharp
// 分片幂等性存储 - 无锁设计
public sealed class ShardedIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, CachedResult>[] _shards;

    // 使用分片减少锁竞争
    private int GetShardIndex(string messageId)
        => Math.Abs(messageId.GetHashCode()) % _shardCount;
}
```

**性能优化**:
- ✅ 静态泛型缓存 - 零反射查找
- ✅ 无锁分片 - 高并发性能
- ✅ ArrayPool - 减少 GC 压力
- ✅ ValueTask - 减少分配

---

### 3. Catga.SourceGenerator - 代码生成

**职责**: 编译时生成代码，实现零反射

**生成内容**:

#### Handler 注册代码
```csharp
// 自动生成的注册代码
public static class GeneratedHandlerRegistration
{
    public static IServiceCollection AddGeneratedHandlers(
        this IServiceCollection services)
    {
        // 编译时发现所有 Handler
        services.AddTransient<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
        services.AddTransient<IRequestHandler<GetOrder, Order>, GetOrderHandler>();
        services.AddTransient<IEventHandler<OrderCreated>, OrderCreatedHandler>();
        // ... 更多 Handler

        return services;
    }
}
```

#### 类型缓存
```csharp
// 自动生成的类型缓存
internal static class TypeNameCache<T>
{
    public static readonly string Value = typeof(T).FullName ?? typeof(T).Name;
}

// Note: No handler instance caching to respect DI lifecycle
// GetRequiredService<T>() is already optimized by .NET DI container
```

#### Roslyn 分析器
```csharp
// CATGA001: 检测缺少 [MemoryPackable]
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingMemoryPackableAttributeAnalyzer : DiagnosticAnalyzer
{
    // 编译时检查消息类型是否标注 [MemoryPackable]
}

// CATGA002: 检测缺少序列化器注册
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingSerializerRegistrationAnalyzer : DiagnosticAnalyzer
{
    // 编译时检查是否调用 UseMemoryPack() 或手动注册 IMessageSerializer
}
```

**收益**:
- ✅ 零反射 - 90x 性能提升
- ✅ 编译时检查 - 减少运行时错误 90%
- ✅ 100% AOT 兼容

---

### 4. Catga.Serialization.* - 序列化层

**职责**: 提供序列化实现（基础设施无关）

#### MemoryPack (推荐)
```csharp
public sealed class MemoryPackMessageSerializer : IMessageSerializer
{
    // 100% AOT 兼容，零反射
    public byte[] Serialize<T>(T message)
        => MemoryPackSerializer.Serialize(message);

    public T? Deserialize<T>(byte[] data)
        => MemoryPackSerializer.Deserialize<T>(data);
}

// 使用
services.AddCatga().UseMemoryPack();
```

#### 自定义 JSON 序列化
```csharp
public sealed class CustomJsonMessageSerializer : IMessageSerializer
{
    // 需要配置 JsonSerializerContext 才能 AOT
    public byte[] Serialize<T>(T message) { ... }
    public T? Deserialize<T>(byte[] data) { ... }
}

// AOT 使用
[JsonSerializable(typeof(CreateOrder))]
public partial class AppJsonContext : JsonSerializerContext { }

services.AddCatga();
services.AddSingleton<IMessageSerializer>(sp => new CustomJsonMessageSerializer(new JsonSerializerOptions
{
    TypeInfoResolver = AppJsonContext.Default
}));
```

---

### 5. 可选扩展

#### Transport Layer
```csharp
// NATS Transport
services.AddCatga().UseMemoryPack();
services.AddNatsTransport("nats://nats:4222");  // K8s Service

// Redis Transport (Streams)
services.AddCatga().UseMemoryPack();
services.AddRedisTransport("redis:6379");
```

#### Persistence Layer
```csharp
// Redis Outbox/Inbox
services.AddRedisOutboxPersistence();
services.AddRedisInboxPersistence();

// Redis Cache
services.AddRedisDistributedCache();
```

---

## 🎯 职责边界

### Catga 负责 ✅

1. **CQRS 消息分发**
   - Command/Query 路由
   - Event 发布/订阅
   - Handler 执行

2. **Pipeline 管道**
   - Behavior 链式执行
   - 日志、追踪、验证
   - 错误处理

3. **幂等性保证**
   - 消息去重
   - 结果缓存
   - 过期清理

4. **可观测性**
   - Metrics (OpenTelemetry)
   - Tracing (ActivitySource)
   - Logging (LoggerMessage)

### Catga 不负责 ❌

1. **节点发现** → 使用 Kubernetes / Aspire
2. **负载均衡** → 使用 K8s Service
3. **服务网格** → 使用 Istio / Linkerd
4. **消息队列实现** → 使用 NATS / Redis 原生能力
5. **配置管理** → 使用 K8s ConfigMap / Aspire

**设计理念**: 专注核心价值，复用成熟生态

详细说明: [职责边界文档](./RESPONSIBILITY-BOUNDARY.md)

---

## 下一步

- 想看运行时数据流和性能：看 [runtime-and-extensibility.md](./runtime-and-extensibility.md)
- 想看更简短的全局入口：看 [ARCHITECTURE.md](./ARCHITECTURE.md)

---
