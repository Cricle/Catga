# 🏗️ Catga 架构重构计划

## 🎯 目标

1. **拆分 InMemory**: 将 `Catga.InMemory` 拆分为通讯和存储两个独立库
2. **提升共用代码**: 将通用组件移到 `Catga` 核心库
3. **统一实现模式**: NATS、Redis、InMemory 应该对齐一致
4. **降低实现门槛**: 简化新库实现的复杂度

---

## 📊 当前问题分析

### Problem 1: Catga.InMemory 职责混乱
```
Catga.InMemory (❌ 混合了多种职责)
├── CatgaMediator (消息调度 - 应在核心)
├── InMemoryMessageTransport (通讯)
├── InMemoryEventStore (存储)
├── HandlerCache (处理器缓存 - 应在核心)
├── Pipeline/Behaviors (管道行为 - 应在核心)
└── DI Extensions (混合了太多东西)
```

### Problem 2: 实现库不一致
```
Catga.InMemory:  ✅ Transport + ✅ EventStore + ✅ Mediator + ✅ Behaviors
Catga.Transport.Nats: ✅ Transport + ✅ EventStore + ❌ 无 Mediator
Catga.Persistence.Redis: ❌ 无 Transport + ❌ 无 EventStore + ✅ Cache/Outbox
```

### Problem 3: 核心功能在 InMemory 中
- `CatgaMediator` - 应该在核心
- `HandlerCache` - 应该在核心
- `Pipeline.Behaviors` - 应该在核心
- `SerializationHelper` - 应该在核心

---

## 🎯 目标架构

### 层次结构
```
┌─────────────────────────────────────────────────────────┐
│                  Application Layer                       │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│          Transport Layer (通讯层 - 可选)                  │
│  Catga.Transport.InMemory  (开发/测试)                   │
│  Catga.Transport.Nats      (生产)                        │
│  Catga.Transport.RabbitMQ  (未来)                        │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│         Persistence Layer (持久化层 - 可选)                │
│  Catga.Persistence.InMemory  (开发/测试)                 │
│  Catga.Persistence.Redis     (生产)                      │
│  Catga.Persistence.Postgres  (未来)                      │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│        Serialization Layer (序列化层 - 可选)               │
│  Catga.Serialization.Json                                │
│  Catga.Serialization.MemoryPack                          │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│              Core Library (核心库 - 必需)                  │
│                    Catga                                 │
│  - CatgaMediator (消息调度)                               │
│  - HandlerCache (处理器缓存)                              │
│  - Pipeline.Behaviors (管道行为)                          │
│  - IMessageTransport (通讯抽象)                           │
│  - IEventStore (存储抽象)                                 │
│  - Performance Tools (性能工具)                           │
└─────────────────────────────────────────────────────────┘
```

---

## 📋 重构步骤

### Phase 1: 提升核心组件到 Catga ✅

#### 1.1 移动 CatgaMediator
```bash
src/Catga.InMemory/CatgaMediator.cs
  → src/Catga/Mediator/CatgaMediator.cs
```

#### 1.2 移动 HandlerCache
```bash
src/Catga.InMemory/HandlerCache.cs
  → src/Catga/Handlers/HandlerCache.cs
```

#### 1.3 移动 Pipeline.Behaviors
```bash
src/Catga.InMemory/Pipeline/Behaviors/
  → src/Catga/Pipeline/Behaviors/

移动：
- LoggingBehavior.cs
- TracingBehavior.cs (已有 DistributedTracingBehavior，需合并)
- ValidationBehavior.cs
- RetryBehavior.cs
- IdempotencyBehavior.cs
- CachingBehavior.cs
- InboxBehavior.cs
- OutboxBehavior.cs
```

#### 1.4 移动 PipelineExecutor
```bash
src/Catga.InMemory/Pipeline/PipelineExecutor.cs
  → src/Catga/Pipeline/PipelineExecutor.cs
```

#### 1.5 移动 SerializationHelper
```bash
src/Catga.InMemory/SerializationHelper.cs
  → src/Catga/Serialization/SerializationHelper.cs
```

#### 1.6 移动 TypedSubscribers
```bash
src/Catga.InMemory/TypedSubscribers.cs
  → src/Catga/Handlers/TypedSubscribers.cs
```

---

### Phase 2: 创建统一的 Transport 接口 ✅

在 `Catga` 核心库中：

```csharp
// src/Catga/Transport/IMessageTransport.cs
public interface IMessageTransport
{
    ValueTask PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class, IMessage;

    ValueTask SubscribeAsync<TMessage>(Func<TMessage, CancellationToken, ValueTask> handler, CancellationToken ct = default)
        where TMessage : class, IMessage;
}

// src/Catga/Transport/IRecoverableTransport.cs
public interface IRecoverableTransport : IMessageTransport, IRecoverableComponent
{
    // 支持优雅恢复的传输层
}
```

---

### Phase 3: 拆分 Catga.InMemory ✅

#### 3.1 创建 Catga.Transport.InMemory
```
src/Catga.Transport.InMemory/
├── Catga.Transport.InMemory.csproj
├── InMemoryMessageTransport.cs (from Catga.InMemory)
├── DependencyInjection/
│   └── InMemoryTransportExtensions.cs
└── README.md
```

**职责**: 仅负责内存中的消息传输

#### 3.2 创建 Catga.Persistence.InMemory
```
src/Catga.Persistence.InMemory/
├── Catga.Persistence.InMemory.csproj
├── Stores/
│   ├── InMemoryEventStore.cs (from Catga.InMemory)
│   ├── InMemoryIdempotencyStore.cs (from Catga.InMemory)
│   ├── InMemoryDeadLetterQueue.cs (from Catga.InMemory)
│   ├── MemoryInboxStore.cs (from Catga.InMemory)
│   └── MemoryOutboxStore.cs (from Catga.InMemory)
├── DependencyInjection/
│   └── InMemoryPersistenceExtensions.cs
└── README.md
```

**职责**: 仅负责内存中的数据持久化

#### 3.3 保留 Catga.InMemory (Facade)
```
src/Catga.InMemory/
├── Catga.InMemory.csproj (依赖 Transport.InMemory + Persistence.InMemory)
├── DependencyInjection/
│   └── InMemoryServiceCollectionExtensions.cs (Facade)
└── README.md (说明这是一个 Facade 包)
```

**职责**: 方便快速开发的 Facade 包，聚合 Transport + Persistence

---

### Phase 4: 统一所有实现库 ✅

#### 4.1 Catga.Transport.InMemory (开发/测试)
```csharp
// 统一接口
public class InMemoryMessageTransport : IRecoverableTransport
{
    public ValueTask PublishAsync<TMessage>(...)
    public ValueTask SubscribeAsync<TMessage>(...)
    public ValueTask<bool> HealthCheckAsync()
    public ValueTask RecoverAsync()
}

// DI 扩展
public static IServiceCollection AddInMemoryTransport(this IServiceCollection services)
public static CatgaServiceBuilder UseInMemoryTransport(this CatgaServiceBuilder builder)
```

#### 4.2 Catga.Transport.Nats (生产)
```csharp
// 统一接口
public class NatsMessageTransport : IRecoverableTransport
{
    public ValueTask PublishAsync<TMessage>(...)
    public ValueTask SubscribeAsync<TMessage>(...)
    public ValueTask<bool> HealthCheckAsync()
    public ValueTask RecoverAsync()
}

// DI 扩展
public static IServiceCollection AddNatsTransport(this IServiceCollection services, Action<NatsOptions>? configure = null)
public static CatgaServiceBuilder UseNatsTransport(this CatgaServiceBuilder builder, Action<NatsOptions>? configure = null)
```

#### 4.3 Catga.Persistence.InMemory (开发/测试)
```csharp
// DI 扩展
public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services)
public static CatgaServiceBuilder UseInMemoryPersistence(this CatgaServiceBuilder builder)
```

#### 4.4 Catga.Persistence.Redis (生产)
```csharp
// 新增 EventStore 实现
public class RedisEventStore : IEventStore
{
    public ValueTask AppendAsync(...)
    public ValueTask<IReadOnlyList<IEvent>> GetEventsAsync(...)
}

// 统一 DI 扩展
public static IServiceCollection AddRedisPersistence(this IServiceCollection services, Action<RedisOptions>? configure = null)
public static CatgaServiceBuilder UseRedisPersistence(this CatgaServiceBuilder builder, Action<RedisOptions>? configure = null)
```

---

### Phase 5: 统一 DI 扩展模式 ✅

#### 标准模式（所有库遵循）
```csharp
namespace Catga.DependencyInjection;

public static class {LibraryName}Extensions
{
    // 1. IServiceCollection 扩展（基础）
    public static IServiceCollection Add{Feature}(
        this IServiceCollection services,
        Action<{Options}>? configure = null)
    {
        // 注册服务
        services.TryAddSingleton<IService, Implementation>();

        // 配置选项
        if (configure != null)
        {
            services.Configure(configure);
        }

        return services;
    }

    // 2. CatgaServiceBuilder 扩展（Fluent）
    public static CatgaServiceBuilder Use{Feature}(
        this CatgaServiceBuilder builder,
        Action<{Options}>? configure = null)
    {
        builder.Services.Add{Feature}(configure);
        return builder;
    }
}
```

---

## 📦 新项目结构

### Transport Layer
```
src/Catga.Transport.InMemory/      (开发/测试)
src/Catga.Transport.Nats/          (生产)
src/Catga.Transport.RabbitMQ/      (未来)
```

### Persistence Layer
```
src/Catga.Persistence.InMemory/    (开发/测试)
src/Catga.Persistence.Redis/       (生产)
src/Catga.Persistence.Postgres/    (未来)
```

### Facade Package
```
src/Catga.InMemory/                (Facade: Transport.InMemory + Persistence.InMemory)
```

---

## 🎯 新库实现指南

### 实现一个新的 Transport 库

**步骤 1**: 创建项目
```bash
dotnet new classlib -n Catga.Transport.{Name}
```

**步骤 2**: 实现接口
```csharp
public class {Name}MessageTransport : IRecoverableTransport
{
    public ValueTask PublishAsync<TMessage>(TMessage message, CancellationToken ct)
    {
        // 实现发布逻辑
    }

    public ValueTask SubscribeAsync<TMessage>(Func<TMessage, CancellationToken, ValueTask> handler, CancellationToken ct)
    {
        // 实现订阅逻辑
    }

    public ValueTask<bool> HealthCheckAsync() => /* 健康检查 */;
    public ValueTask RecoverAsync() => /* 恢复逻辑 */;
}
```

**步骤 3**: 添加 DI 扩展
```csharp
public static class {Name}TransportExtensions
{
    public static IServiceCollection Add{Name}Transport(
        this IServiceCollection services,
        Action<{Name}Options>? configure = null)
    {
        services.TryAddSingleton<IMessageTransport, {Name}MessageTransport>();
        services.TryAddSingleton<IRecoverableTransport, {Name}MessageTransport>();
        // 配置选项...
        return services;
    }

    public static CatgaServiceBuilder Use{Name}Transport(
        this CatgaServiceBuilder builder,
        Action<{Name}Options>? configure = null)
    {
        builder.Services.Add{Name}Transport(configure);
        return builder;
    }
}
```

**完成！** 新库即可与现有系统无缝集成。

---

### 实现一个新的 Persistence 库

**步骤 1**: 创建项目
```bash
dotnet new classlib -n Catga.Persistence.{Name}
```

**步骤 2**: 实现接口
```csharp
public class {Name}EventStore : IEventStore
{
    public ValueTask AppendAsync(string streamId, IReadOnlyList<IEvent> events, long expectedVersion, CancellationToken ct)
    {
        // 实现事件追加逻辑
    }

    public ValueTask<IReadOnlyList<IEvent>> GetEventsAsync(string streamId, long fromVersion, CancellationToken ct)
    {
        // 实现事件读取逻辑
    }
}
```

**步骤 3**: 添加 DI 扩展
```csharp
public static class {Name}PersistenceExtensions
{
    public static IServiceCollection Add{Name}Persistence(
        this IServiceCollection services,
        Action<{Name}Options>? configure = null)
    {
        services.TryAddSingleton<IEventStore, {Name}EventStore>();
        // 其他持久化服务...
        return services;
    }

    public static CatgaServiceBuilder Use{Name}Persistence(
        this CatgaServiceBuilder builder,
        Action<{Name}Options>? configure = null)
    {
        builder.Services.Add{Name}Persistence(configure);
        return builder;
    }
}
```

**完成！** 新库即可与现有系统无缝集成。

---

## 📊 迁移影响分析

### Breaking Changes

#### 1. Catga.InMemory 拆分
```csharp
// 旧代码
services.AddCatga()
    .UseInMemoryTransport();

// 新代码 (选项 A: 使用 Facade)
services.AddCatga()
    .UseInMemory();  // Facade，自动添加 Transport + Persistence

// 新代码 (选项 B: 精确控制)
services.AddCatga()
    .UseInMemoryTransport()
    .UseInMemoryPersistence();
```

#### 2. 行为移动到核心库
```csharp
// 旧代码
using Catga.InMemory.Pipeline.Behaviors;

// 新代码
using Catga.Pipeline.Behaviors;  // ✅ 在核心库中
```

### 兼容性策略
1. **保留 Catga.InMemory Facade**: 向后兼容
2. **标记过时方法**: `[Obsolete("Use UseInMemory() instead")]`
3. **迁移指南**: 提供详细的迁移文档

---

## ⚡ 实施顺序

### Step 1: 提升核心组件 (2-3 小时)
- 移动 Mediator、HandlerCache、Behaviors 到 Catga
- 确保编译通过，测试通过

### Step 2: 创建新项目 (1 小时)
- 创建 Catga.Transport.InMemory
- 创建 Catga.Persistence.InMemory

### Step 3: 拆分代码 (2-3 小时)
- 将 Transport 代码移到 Catga.Transport.InMemory
- 将 Persistence 代码移到 Catga.Persistence.InMemory
- 更新 Catga.InMemory 为 Facade

### Step 4: 统一 DI 扩展 (1-2 小时)
- 标准化所有库的 DI 扩展
- 添加 CatgaServiceBuilder 扩展

### Step 5: 对齐 Redis 和 NATS (1-2 小时)
- Redis 添加 EventStore 实现
- 统一 DI 扩展模式

### Step 6: 更新示例和文档 (1-2 小时)
- 更新 OrderSystem 示例
- 编写迁移指南
- 更新架构文档

**总计**: 约 8-14 小时

---

## 🎉 预期收益

### 架构清晰度 ⬆️
- ✅ 单一职责原则
- ✅ 依赖层次清晰
- ✅ 易于理解和维护

### 灵活性 ⬆️
- ✅ 可独立选择 Transport
- ✅ 可独立选择 Persistence
- ✅ 易于扩展新实现

### 实现门槛 ⬇️
- ✅ 统一的接口
- ✅ 统一的 DI 模式
- ✅ 清晰的实现指南

### 一致性 ⬆️
- ✅ NATS、Redis、InMemory 完全对齐
- ✅ 新库实现遵循相同模式
- ✅ 用户体验一致

---

## 📝 决策点

请选择实施方案：
- **A**: 全部执行（推荐）✅ 完整重构，彻底解决架构问题
- **B**: 分阶段执行 📊 先执行 Step 1-3，后续再优化
- **C**: 仅核心提升 💡 只执行 Step 1，保留现有结构
- **D**: 制定更详细计划 📋 需要更多信息再决策

🎯 **推荐选择 A：一次性彻底重构，建立清晰的架构基础！**

