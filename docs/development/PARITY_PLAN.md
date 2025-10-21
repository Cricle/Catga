# 🎯 InMemory / Redis / NATS 功能对等性实施计划

## 📊 当前状态分析

### Transport 层 - ✅ 已对等

| 实现 | 文件数 | 核心文件 |
|------|--------|---------|
| InMemory | 2 | InMemoryMessageTransport, InMemoryIdempotencyStore (内部) |
| Redis | 2 | RedisMessageTransport, RedisTransportOptions |
| NATS | 3 | NatsMessageTransport, NatsTransportOptions, NatsRecoverableTransport |

### Persistence 层 - ⚠️ 不对等

| 功能 | InMemory | Redis | NATS |
|------|----------|-------|------|
| EventStore | ✅ | ✅ | ✅ (刚移动) |
| OutboxStore | ✅ | ✅ | ✅ |
| InboxStore | ✅ | ✅ | ✅ |
| **DeadLetterQueue** | ✅ | ❌ | ❌ |
| **IdempotencyStore** | ⚠️ 简单版 | ✅ | ❌ |

---

## 🎯 对等性目标

### 所有三个实现必须提供:

**Persistence 层**:
1. ✅ IEventStore
2. ✅ IOutboxStore
3. ✅ IInboxStore
4. ⏳ IIdempotencyStore (完整实现)
5. ⏳ IDeadLetterQueue

---

## 📋 执行计划

### Phase 1: 修复命名空间和结构 ✅

#### 1.1 移动 NatsEventStore ✅ 已完成
```
From: src/Catga.Transport.Nats/NatsEventStore.cs
To:   src/Catga.Persistence.Nats/Stores/NatsJSEventStore.cs
```

#### 1.2 更新命名空间
```csharp
// Before
namespace Catga.Transport.Nats;

// After
namespace Catga.Persistence.Nats;
```

---

### Phase 2: 补充缺失的 Store 实现

#### 2.1 创建 InMemoryIdempotencyStore (完整版)
**文件**: `src/Catga.Persistence.InMemory/Stores/MemoryIdempotencyStore.cs`

**注意**: `Abstractions/IIdempotencyStore.cs` 中已有 `MemoryIdempotencyStore`，但使用了锁！需要：
- 选项1: 移动到 Persistence.InMemory 并改为无锁（ConcurrentDictionary）
- 选项2: 保持在 Abstractions 作为简单实现，创建优化版本

**推荐**: 选项1 - 移动并优化为无锁

#### 2.2 创建 RedisDeadLetterQueue
**文件**: `src/Catga.Persistence.Redis/Stores/RedisDeadLetterQueue.cs`

**实现策略**:
```csharp
public class RedisDeadLetterQueue : IDeadLetterQueue
{
    // 使用 Redis List + Hash
    // Key pattern: "dlq:messages" (List) + "dlq:details:{id}" (Hash)
    // 无锁: Redis 单线程
    // AOT: 使用 IMessageSerializer
}
```

#### 2.3 创建 NatsJSDeadLetterQueue
**文件**: `src/Catga.Persistence.Nats/Stores/NatsJSDeadLetterQueue.cs`

**实现策略**:
```csharp
public class NatsJSDeadLetterQueue : NatsJSStoreBase, IDeadLetterQueue
{
    // 使用 NATS JetStream
    // Stream: "CATGA_DLQ"
    // 无锁: NATS 内部处理
    // AOT: 使用 IMessageSerializer
}
```

#### 2.4 创建 NatsKVIdempotencyStore
**文件**: `src/Catga.Persistence.Nats/Stores/NatsKVIdempotencyStore.cs`

**实现策略**:
```csharp
public class NatsKVIdempotencyStore : NatsJSStoreBase, IIdempotencyStore
{
    // 使用 NATS KeyValue Store
    // Bucket: "CATGA_IDEMPOTENCY"
    // TTL: 24小时
    // 无锁: NATS KV 线程安全
    // AOT: 使用 IMessageSerializer
}
```

---

### Phase 3: 统一文件夹结构

#### 3.1 创建 Redis Stores/ 文件夹
```
src/Catga.Persistence.Redis/
├── Stores/                           🆕 创建
│   ├── RedisEventStore.cs           🆕 移动
│   ├── RedisOutboxStore.cs          🆕 移动 (OptimizedRedisOutboxStore)
│   ├── RedisInboxStore.cs           🆕 创建 (从 Persistence/ 重构)
│   ├── RedisIdempotencyStore.cs     🆕 移动
│   └── RedisDeadLetterQueue.cs      🆕 创建
├── RedisBatchOperations.cs          ✅ 保留 (辅助类)
├── RedisReadWriteCache.cs           ✅ 保留 (辅助类)
└── Options classes                   ✅ 保留
```

---

### Phase 4: DI 扩展补全

确保所有实现都可以通过 DI 注册：

```csharp
// InMemory
services.AddInMemoryPersistence()
    .AddInMemoryEventStore()
    .AddInMemoryOutbox()
    .AddInMemoryInbox()
    .AddInMemoryIdempotency()      // 🆕
    .AddInMemoryDeadLetterQueue(); // ✅

// Redis
services.AddRedisPersistence(...)
    .AddRedisEventStore()
    .AddRedisOutbox()
    .AddRedisInbox()
    .AddRedisIdempotency()         // ✅
    .AddRedisDeadLetterQueue();    // 🆕

// NATS
services.AddNatsPersistence(...)
    .AddNatsEventStore()
    .AddNatsOutbox()
    .AddNatsInbox()
    .AddNatsIdempotency()          // 🆕
    .AddNatsDeadLetterQueue();     // 🆕
```

---

## ⚠️ 关键约束

### 无锁设计
- ✅ InMemory: ConcurrentDictionary / ImmutableList + CAS
- ✅ Redis: 单线程模型，天然无锁
- ✅ NATS: 内部处理并发

### AOT 兼容
- ✅ 使用 IMessageSerializer 接口
- ✅ 不直接调用 JsonSerializer
- ✅ DynamicallyAccessedMembers 标记
- ✅ 避免反射

### 代码复用
- ✅ InMemory: 继承 BaseMemoryStore
- ✅ NATS: 继承 NatsJSStoreBase
- ⚠️ Redis: 考虑创建 RedisStoreBase

---

## 📝 执行顺序

### ✅ Step 1: 移动 NatsEventStore (已完成)
```
✅ src/Catga.Persistence.Nats/Stores/NatsJSEventStore.cs
```

### ⏳ Step 2: 更新 NatsJSEventStore 命名空间
```
namespace Catga.Transport.Nats; → namespace Catga.Persistence.Nats;
```

### ⏳ Step 3: 优化 MemoryIdempotencyStore (无锁)
```
From: src/Catga/Abstractions/IIdempotencyStore.cs (使用 SemaphoreSlim)
To:   src/Catga.Persistence.InMemory/Stores/MemoryIdempotencyStore.cs (使用 ConcurrentDictionary)
```

### ⏳ Step 4: 创建 RedisDeadLetterQueue
```
src/Catga.Persistence.Redis/Stores/RedisDeadLetterQueue.cs
```

### ⏳ Step 5: 创建 NatsJSDeadLetterQueue
```
src/Catga.Persistence.Nats/Stores/NatsJSDeadLetterQueue.cs
```

### ⏳ Step 6: 创建 NatsKVIdempotencyStore
```
src/Catga.Persistence.Nats/Stores/NatsKVIdempotencyStore.cs
```

### ⏳ Step 7: 重组 Redis 文件结构
```
创建 Stores/ 并移动文件
```

### ⏳ Step 8: 补全 DI 扩展
```
更新所有 ServiceCollectionExtensions
```

### ⏳ Step 9: 测试验证
```
补充单元测试和集成测试
```

---

## 🎯 验收标准

### 功能对等
- [ ] InMemory: 5/5 Stores (Event, Outbox, Inbox, Idempotency, DeadLetter)
- [ ] Redis: 5/5 Stores (Event, Outbox, Inbox, Idempotency, DeadLetter)
- [ ] NATS: 5/5 Stores (Event, Outbox, Inbox, Idempotency, DeadLetter)

### 结构对等
- [ ] 所有 Persistence 都使用 Stores/ 文件夹
- [ ] 所有实现都有 Options 类（合理的）
- [ ] 所有实现都有 DI 扩展

### 质量标准
- [ ] 所有实现都是无锁的
- [ ] 所有实现都是 AOT 兼容
- [ ] 所有实现都有测试
- [ ] 编译 0 错误

---

<div align="center">

**准备开始实施！**

</div>

