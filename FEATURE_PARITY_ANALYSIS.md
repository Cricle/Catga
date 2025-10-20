# 📊 InMemory / Redis / NATS 功能对等性分析

## 🎯 目标：三个实现功能完全对等

---

## 📦 传输层 (Transport)

### 功能对比表

| 功能 | InMemory | Redis | NATS | 状态 |
|------|----------|-------|------|------|
| **IMessageTransport** | ✅ | ✅ | ✅ | ✅ 对等 |
| **PublishAsync** | ✅ | ✅ | ✅ | ✅ 对等 |
| **SendAsync** | ✅ | ✅ | ✅ | ✅ 对等 |
| **SubscribeAsync** | ✅ | ✅ | ✅ | ✅ 对等 |
| **PublishBatchAsync** | ✅ | ✅ | ✅ | ✅ 对等 |
| **SendBatchAsync** | ✅ | ✅ | ✅ | ✅ 对等 |
| **QoS Support** | ✅ | ✅ | ✅ | ✅ 对等 |
| **Idempotency Store** | ✅ | ❌ | ❌ | ⚠️ **不对等** |

### 文件结构对比

```
InMemory:
├── InMemoryMessageTransport.cs       ✅
├── InMemoryIdempotencyStore.cs       ✅
└── DependencyInjection/
    └── InMemoryTransportServiceCollectionExtensions.cs ✅

Redis:
├── RedisMessageTransport.cs          ✅
├── RedisTransportOptions.cs          ✅
└── DependencyInjection/
    └── RedisTransportServiceCollectionExtensions.cs ✅

NATS:
├── NatsMessageTransport.cs           ✅
├── NatsTransportOptions.cs           ✅
├── NatsEventStore.cs                 ⚠️ 应该在 Persistence
├── NatsRecoverableTransport.cs       ✅
└── DependencyInjection/
    └── NatsTransportServiceCollectionExtensions.cs ✅
```

### ⚠️ 发现的问题

1. **InMemory 有 IdempotencyStore，Redis/NATS 没有**
   - InMemory: `InMemoryIdempotencyStore.cs` ✅
   - Redis: ❌ 缺失（在 Persistence.Redis 中）
   - NATS: ❌ 缺失

2. **NATS 有 EventStore 在 Transport 中**
   - `NatsEventStore.cs` 应该在 `Catga.Persistence.Nats`

---

## 📦 持久化层 (Persistence)

### 功能对比表

| 功能 | InMemory | Redis | NATS | 状态 |
|------|----------|-------|------|------|
| **IEventStore** | ✅ | ✅ | ✅ | ✅ 对等 |
| **IOutboxStore** | ✅ | ✅ | ✅ | ✅ 对等 |
| **IInboxStore** | ✅ | ✅ | ✅ | ✅ 对等 |
| **IDeadLetterQueue** | ✅ | ❌ | ❌ | ⚠️ **不对等** |
| **IIdempotencyStore** | ❌ | ✅ | ❌ | ⚠️ **不对等** |
| **BaseStore 抽象** | ✅ | ❌ | ✅ | ⚠️ 部分对等 |

### 文件结构对比

```
InMemory:
├── BaseMemoryStore.cs                ✅ (抽象基类)
└── Stores/
    ├── InMemoryEventStore.cs         ✅
    ├── MemoryOutboxStore.cs          ✅
    ├── MemoryInboxStore.cs           ✅
    └── InMemoryDeadLetterQueue.cs    ✅

Redis:
├── RedisEventStore.cs                ✅
├── OptimizedRedisOutboxStore.cs      ✅
├── RedisIdempotencyStore.cs          ✅
├── RedisIdempotencyOptions.cs        ✅
├── RedisInboxOptions.cs              ✅
├── RedisOutboxOptions.cs             ✅
├── RedisReadWriteCache.cs            ✅ (辅助类)
├── RedisBatchOperations.cs           ✅ (辅助类)
└── Persistence/
    ├── RedisInboxPersistence.cs      ✅
    └── RedisOutboxPersistence.cs     ✅

NATS:
├── NatsJSStoreBase.cs                ✅ (抽象基类)
├── NatsJSStoreOptions.cs             ✅
├── NatsKVEventStore.cs               ✅
└── Stores/
    ├── NatsJSInboxStore.cs           ✅
    └── NatsJSOutboxStore.cs          ✅
```

### ⚠️ 发现的问题

1. **Redis 缺少 IDeadLetterQueue 实现**
   - InMemory: ✅ `InMemoryDeadLetterQueue.cs`
   - Redis: ❌ 缺失
   - NATS: ❌ 缺失

2. **InMemory 缺少 IIdempotencyStore 实现**
   - InMemory: ❌ 缺失（在 Transport.InMemory 中）
   - Redis: ✅ `RedisIdempotencyStore.cs`
   - NATS: ❌ 缺失

3. **结构不一致**
   - InMemory: 使用 `Stores/` 子文件夹
   - Redis: 根目录 + `Persistence/` 子文件夹
   - NATS: 根目录 + `Stores/` 子文件夹

---

## 🔧 需要补充的功能

### 高优先级：核心功能缺失

#### 1. Redis.Persistence - IDeadLetterQueue ❌
**需要创建**: `src/Catga.Persistence.Redis/Stores/RedisDeadLetterQueue.cs`

```csharp
public class RedisDeadLetterQueue : IDeadLetterQueue
{
    // 使用 Redis List 或 Stream 存储失败消息
    Task AddAsync(...)
    Task<IReadOnlyList<DeadLetterMessage>> GetAsync(...)
    Task RetryAsync(...)
    Task DeleteAsync(...)
}
```

#### 2. Nats.Persistence - IDeadLetterQueue ❌
**需要创建**: `src/Catga.Persistence.Nats/Stores/NatsJSDeadLetterQueue.cs`

```csharp
public class NatsJSDeadLetterQueue : IDeadLetterQueue
{
    // 使用 NATS JetStream 或 KeyValue 存储失败消息
    Task AddAsync(...)
    Task<IReadOnlyList<DeadLetterMessage>> GetAsync(...)
    Task RetryAsync(...)
    Task DeleteAsync(...)
}
```

#### 3. InMemory.Persistence - IIdempotencyStore ❌
**需要创建**: `src/Catga.Persistence.InMemory/Stores/InMemoryIdempotencyStore.cs`

```csharp
public class InMemoryIdempotencyStore : BaseMemoryStore, IIdempotencyStore
{
    // 从 Transport.InMemory.InMemoryIdempotencyStore 移动过来
    // 或创建新的实现
    Task<bool> ContainsAsync(...)
    Task AddAsync(...)
    Task<T?> GetResultAsync<T>(...)
    Task SetResultAsync<T>(...)
}
```

#### 4. Nats.Persistence - IIdempotencyStore ❌
**需要创建**: `src/Catga.Persistence.Nats/Stores/NatsKVIdempotencyStore.cs`

```csharp
public class NatsKVIdempotencyStore : NatsJSStoreBase, IIdempotencyStore
{
    // 使用 NATS KeyValue Store
    Task<bool> ContainsAsync(...)
    Task AddAsync(...)
    Task<T?> GetResultAsync<T>(...)
    Task SetResultAsync<T>(...)
}
```

---

## 📋 完整功能矩阵

### Transport 层

| 接口/功能 | InMemory | Redis | NATS |
|-----------|----------|-------|------|
| IMessageTransport | ✅ | ✅ | ✅ |
| QoS 0 (At-Most-Once) | ✅ | ✅ Pub/Sub | ✅ Core |
| QoS 1 (At-Least-Once) | ✅ | ✅ Streams | ✅ JetStream |
| QoS 2 (Exactly-Once) | ✅ | ✅ | ✅ |
| Batch Operations | ✅ | ✅ | ✅ |
| Options | - | ✅ | ✅ |
| Recoverable | - | - | ✅ |

### Persistence 层

| 接口/功能 | InMemory | Redis | NATS |
|-----------|----------|-------|------|
| IEventStore | ✅ | ✅ | ✅ |
| IOutboxStore | ✅ | ✅ | ✅ |
| IInboxStore | ✅ | ✅ | ✅ |
| **IDeadLetterQueue** | ✅ | ❌ | ❌ |
| **IIdempotencyStore** | ❌ | ✅ | ❌ |
| Base抽象 | ✅ | - | ✅ |
| Options | - | ✅ | ✅ |
| Batch优化 | - | ✅ | - |

---

## 🎯 对等性改进计划

### Phase 1: 移动错位的实现

1. **移动 NatsEventStore.cs**
   - From: `Catga.Transport.Nats/NatsEventStore.cs`
   - To: `Catga.Persistence.Nats/NatsJSEventStore.cs`
   - 理由: EventStore 是持久化功能，不是传输功能

2. **移动 InMemoryIdempotencyStore.cs**
   - From: `Catga.Transport.InMemory/InMemoryIdempotencyStore.cs`
   - To: `Catga.Persistence.InMemory/Stores/InMemoryIdempotencyStore.cs`
   - 理由: Idempotency 是持久化功能

### Phase 2: 补充缺失的实现

3. **创建 RedisDeadLetterQueue.cs**
   - Path: `src/Catga.Persistence.Redis/Stores/RedisDeadLetterQueue.cs`
   - 实现: 使用 Redis List + Hash

4. **创建 NatsJSDeadLetterQueue.cs**
   - Path: `src/Catga.Persistence.Nats/Stores/NatsJSDeadLetterQueue.cs`
   - 实现: 使用 NATS JetStream

5. **创建 NatsKVIdempotencyStore.cs**
   - Path: `src/Catga.Persistence.Nats/Stores/NatsKVIdempotencyStore.cs`
   - 实现: 使用 NATS KeyValue Store

### Phase 3: 统一文件夹结构

6. **统一使用 Stores/ 子文件夹**
   - InMemory: ✅ 已使用
   - Redis: ❌ 需要创建 `Stores/` 并移动文件
   - NATS: ✅ 已使用

7. **移动 Redis 文件到 Stores/**
   - `RedisEventStore.cs` → `Stores/RedisEventStore.cs`
   - `OptimizedRedisOutboxStore.cs` → `Stores/RedisOutboxStore.cs`
   - `RedisIdempotencyStore.cs` → `Stores/RedisIdempotencyStore.cs`
   - 保留辅助类在根目录

### Phase 4: 完善 DI 扩展

8. **确保所有 Store 都有 DI 扩展方法**
   - InMemory: 检查并补全
   - Redis: 检查并补全
   - NATS: 检查并补全

---

## 📊 预期最终结构

### Catga.Transport.InMemory
```
├── InMemoryMessageTransport.cs       ✅
└── DependencyInjection/
    └── InMemoryTransportServiceCollectionExtensions.cs ✅
```

### Catga.Transport.Redis
```
├── RedisMessageTransport.cs          ✅
├── RedisTransportOptions.cs          ✅
└── DependencyInjection/
    └── RedisTransportServiceCollectionExtensions.cs ✅
```

### Catga.Transport.Nats
```
├── NatsMessageTransport.cs           ✅
├── NatsTransportOptions.cs           ✅
├── NatsRecoverableTransport.cs       ✅
└── DependencyInjection/
    └── NatsTransportServiceCollectionExtensions.cs ✅
```

---

### Catga.Persistence.InMemory
```
├── BaseMemoryStore.cs                ✅
└── Stores/
    ├── InMemoryEventStore.cs         ✅
    ├── MemoryOutboxStore.cs          ✅
    ├── MemoryInboxStore.cs           ✅
    ├── InMemoryDeadLetterQueue.cs    ✅
    └── InMemoryIdempotencyStore.cs   🆕 需要移动
```

### Catga.Persistence.Redis
```
├── RedisBatchOperations.cs           ✅ (辅助类)
├── RedisReadWriteCache.cs            ✅ (辅助类)
├── RedisEventStoreOptions.cs         🆕 需要创建
├── RedisOutboxOptions.cs             ✅
├── RedisInboxOptions.cs              ✅
├── RedisIdempotencyOptions.cs        ✅
└── Stores/
    ├── RedisEventStore.cs            🆕 需要移动
    ├── RedisOutboxStore.cs           🆕 需要移动
    ├── RedisInboxStore.cs            🆕 需要创建 (从 Persistence/ 移动)
    ├── RedisIdempotencyStore.cs      🆕 需要移动
    └── RedisDeadLetterQueue.cs       🆕 需要创建
```

### Catga.Persistence.Nats
```
├── NatsJSStoreBase.cs                ✅
├── NatsJSStoreOptions.cs             ✅
└── Stores/
    ├── NatsJSEventStore.cs           🆕 需要从 Transport 移动
    ├── NatsJSOutboxStore.cs          ✅
    ├── NatsJSInboxStore.cs           ✅
    ├── NatsKVIdempotencyStore.cs     🆕 需要创建
    └── NatsJSDeadLetterQueue.cs      🆕 需要创建
```

---

## ✅ 执行计划

### Phase 1: 文件移动和重组 (无锁 + AOT)

#### 1.1 移动 InMemoryIdempotencyStore
```
From: src/Catga.Transport.InMemory/InMemoryIdempotencyStore.cs
To:   src/Catga.Persistence.InMemory/Stores/InMemoryIdempotencyStore.cs
```

#### 1.2 移动 NatsEventStore
```
From: src/Catga.Transport.Nats/NatsEventStore.cs
To:   src/Catga.Persistence.Nats/Stores/NatsJSEventStore.cs
```

#### 1.3 创建 Redis Stores/ 文件夹并移动文件
```
移动:
- RedisEventStore.cs → Stores/RedisEventStore.cs
- OptimizedRedisOutboxStore.cs → Stores/RedisOutboxStore.cs
- RedisIdempotencyStore.cs → Stores/RedisIdempotencyStore.cs

From: src/Catga.Persistence.Redis/Persistence/RedisInboxPersistence.cs
To:   src/Catga.Persistence.Redis/Stores/RedisInboxStore.cs
```

---

### Phase 2: 补充缺失的实现 (无锁 + AOT)

#### 2.1 创建 RedisDeadLetterQueue
**实现策略**: 
- 使用 Redis List (`LPUSH` + `LRANGE`)
- 使用 Redis Hash 存储消息详情
- 无锁（Redis 本身是单线程）
- AOT 兼容（使用 IMessageSerializer）

#### 2.2 创建 NatsJSDeadLetterQueue
**实现策略**:
- 使用 NATS JetStream
- Stream name: `CATGA_DLQ`
- 无锁（NATS 本身处理）
- AOT 兼容

#### 2.3 创建 NatsKVIdempotencyStore
**实现策略**:
- 使用 NATS KeyValue Store
- Bucket name: `CATGA_IDEMPOTENCY`
- TTL: 可配置
- 无锁（NATS KV 是线程安全的）
- AOT 兼容

---

### Phase 3: 统一 DI 扩展

#### 3.1 确保所有 Store 都可注册
```csharp
// InMemory
builder.Services
    .AddInMemoryTransport()
    .AddInMemoryPersistence()
        .AddInMemoryEventStore()
        .AddInMemoryOutbox()
        .AddInMemoryInbox()
        .AddInMemoryIdempotency()      // ✅ 需要添加
        .AddInMemoryDeadLetterQueue(); // ✅ 已有

// Redis
builder.Services
    .AddRedisTransport(...)
    .AddRedisPersistence(...)
        .AddRedisEventStore()
        .AddRedisOutbox()
        .AddRedisInbox()
        .AddRedisIdempotency()         // ✅ 已有
        .AddRedisDeadLetterQueue();    // ✅ 需要添加

// NATS
builder.Services
    .AddNatsTransport(...)
    .AddNatsPersistence(...)
        .AddNatsEventStore()
        .AddNatsOutbox()
        .AddNatsInbox()
        .AddNatsIdempotency()          // ✅ 需要添加
        .AddNatsDeadLetterQueue();     // ✅ 需要添加
```

---

## 🎯 实施原则

### 无锁设计
1. ✅ InMemory 使用 ConcurrentDictionary / ImmutableList
2. ✅ Redis 单线程模型，无需额外锁
3. ✅ NATS 内部处理并发，无需额外锁

### AOT 兼容
1. ✅ 使用 IMessageSerializer 接口（不直接调用 JSON）
2. ✅ 避免反射
3. ✅ DynamicallyAccessedMembers 标记

### 代码复用
1. ✅ InMemory 使用 BaseMemoryStore
2. ✅ NATS 使用 NatsJSStoreBase
3. ⚠️ Redis 可以考虑创建 RedisStoreBase

---

## 📊 工作量估算

| 任务 | 工作量 | 优先级 | 风险 |
|------|--------|--------|------|
| 移动文件 | 1-2h | 高 | 低 |
| RedisDeadLetterQueue | 2-3h | 高 | 低 |
| NatsJSDeadLetterQueue | 2-3h | 高 | 低 |
| NatsKVIdempotencyStore | 2-3h | 高 | 低 |
| InMemoryIdempotencyStore移动 | 30min | 中 | 低 |
| DI 扩展补全 | 1h | 中 | 低 |
| 测试补充 | 3-4h | 高 | 低 |
| 文档更新 | 1h | 低 | 无 |

**总计**: ~12-16 小时

---

## ✅ 验收标准

### 功能对等
- [ ] 所有三个实现都有 IEventStore
- [ ] 所有三个实现都有 IOutboxStore
- [ ] 所有三个实现都有 IInboxStore
- [ ] 所有三个实现都有 IIdempotencyStore
- [ ] 所有三个实现都有 IDeadLetterQueue

### 结构对等
- [ ] 所有 Persistence 都使用 Stores/ 文件夹
- [ ] 所有实现都有 Options 类
- [ ] 所有实现都有 DI 扩展
- [ ] 文件组织一致

### 质量标准
- [ ] 所有实现都是无锁的
- [ ] 所有实现都是 AOT 兼容的
- [ ] 所有实现都有单元测试
- [ ] 所有实现都有文档

---

<div align="center">

**目标: 三个实现功能 100% 对等！**

</div>

