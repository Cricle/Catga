# 🔧 DRY 原则改进计划

## 📊 当前状态分析

### ✅ 已有的 Base 类

| Base 类 | 用途 | 使用者 | DRY 效果 |
|---------|------|--------|----------|
| **BaseMemoryStore<TMessage>** | InMemory 通用存储基类 | OutboxStore, InboxStore | ✅ 优秀 |
| **NatsJSStoreBase** | NATS JetStream 初始化 | 4个 Store | ✅ 优秀 |
| **ExpirationHelper** | 过期清理辅助 | InMemory Stores | ✅ 良好 |

### ⚠️ 发现的重复模式

#### 1. **Redis Stores - 缺少 Base 类**

所有 Redis Store 都有相同的模式：

```csharp
// 重复的构造函数模式
private readonly IConnectionMultiplexer _redis;
private readonly IMessageSerializer _serializer;
private readonly string _keyPrefix;

public RedisXxxStore(
    IConnectionMultiplexer redis,
    IMessageSerializer serializer,
    RedisXxxOptions? options = null)
{
    _redis = redis;
    _serializer = serializer;
    _keyPrefix = options?.KeyPrefix ?? "default:";
}

// 重复的 GetDatabase() 调用
var db = _redis.GetDatabase();
```

**文件**:
- `RedisIdempotencyStore.cs`
- `RedisDeadLetterQueue.cs`
- `OptimizedRedisOutboxStore.cs`
- `RedisEventStore.cs` (placeholder)

---

#### 2. **InMemory - DeadLetterQueue 和 EventStore 未使用 Base**

**InMemoryDeadLetterQueue**:
- 使用 `ConcurrentQueue`
- 独立实现，未继承 `BaseMemoryStore`

**InMemoryEventStore**:
- 使用 `ConcurrentDictionary`
- 独立实现，未继承 `BaseMemoryStore`

---

#### 3. **NATS - EventStore 独立实现**

**NatsJSEventStore**:
- 有自己的 CAS 初始化代码（与 `NatsJSStoreBase` 重复）
- 未继承 `NatsJSStoreBase`

---

#### 4. **序列化辅助代码重复**

所有 Store 都重复以下模式：

```csharp
// 序列化
var data = _serializer.Serialize(message);
var json = Encoding.UTF8.GetString(data);

// 反序列化
var data = Encoding.UTF8.GetBytes(json);
var message = _serializer.Deserialize<T>(data);
```

---

## 🎯 改进计划

### Phase 1: 创建 RedisStoreBase ✅ **高优先级**

**目标**: 为所有 Redis Store 创建统一基类

**新增文件**: `src/Catga.Persistence.Redis/RedisStoreBase.cs`

**设计**:

```csharp
/// <summary>
/// Base class for Redis-based stores with common patterns
/// </summary>
public abstract class RedisStoreBase
{
    protected readonly IConnectionMultiplexer Redis;
    protected readonly IMessageSerializer Serializer;
    protected readonly string KeyPrefix;

    protected RedisStoreBase(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string keyPrefix)
    {
        Redis = redis ?? throw new ArgumentNullException(nameof(redis));
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
    }

    /// <summary>
    /// Get Redis database (inline for performance)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected IDatabase GetDatabase() => Redis.GetDatabase();

    /// <summary>
    /// Build key with prefix
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string BuildKey(string suffix) => $"{KeyPrefix}{suffix}";

    /// <summary>
    /// Build key with prefix and ID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string BuildKey(long id) => $"{KeyPrefix}{id}";
}
```

**受益的 Store**:
- ✅ `RedisIdempotencyStore` - 减少 ~10 行
- ✅ `RedisDeadLetterQueue` - 减少 ~10 行
- ✅ `OptimizedRedisOutboxStore` - 减少 ~10 行
- ✅ `RedisInboxPersistence` - 减少 ~10 行
- ✅ `RedisEventStore` - 减少 ~10 行

**预估减少代码**: ~50 行重复代码

---

### Phase 2: NatsJSEventStore 继承 NatsJSStoreBase ✅ **高优先级**

**目标**: 让 `NatsJSEventStore` 继承 `NatsJSStoreBase`，消除重复的初始化代码

**当前问题**:
- `NatsJSEventStore` 有自己的 CAS 初始化代码（~50 行）
- 与 `NatsJSStoreBase` 的初始化逻辑完全相同

**修改**:

```csharp
// Before
public sealed class NatsJSEventStore : IEventStore
{
    private readonly INatsConnection _connection;
    private readonly INatsJSContext _jetStream;
    private volatile int _initializationState;
    private volatile bool _streamCreated;

    // ... 重复的初始化代码 ~50 行
}

// After
public sealed class NatsJSEventStore : NatsJSStoreBase, IEventStore
{
    private readonly IMessageSerializer _serializer;

    public NatsJSEventStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string streamName = "CATGA_EVENTS",
        NatsJSStoreOptions? options = null)
        : base(connection, streamName, options)
    {
        _serializer = serializer;
    }

    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    // 直接使用 base.EnsureInitializedAsync()
}
```

**预估减少代码**: ~50 行重复代码

---

### Phase 3: 创建 SerializationHelper 静态类 ⚠️ **中优先级**

**目标**: 消除序列化/反序列化的重复代码

**新增文件**: `src/Catga/Core/SerializationHelper.cs`

**设计**:

```csharp
/// <summary>
/// Serialization helper utilities (DRY for common patterns)
/// </summary>
public static class SerializationHelper
{
    /// <summary>
    /// Serialize to UTF-8 JSON string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string SerializeToJson<T>(this IMessageSerializer serializer, T value)
    {
        var bytes = serializer.Serialize(value);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Deserialize from UTF-8 JSON string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? DeserializeFromJson<T>(this IMessageSerializer serializer, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return serializer.Deserialize<T>(bytes);
    }

    /// <summary>
    /// Try deserialize with exception handling
    /// </summary>
    public static bool TryDeserialize<T>(
        this IMessageSerializer serializer,
        byte[] data,
        [NotNullWhen(true)] out T? result)
    {
        try
        {
            result = serializer.Deserialize<T>(data);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}
```

**受益的 Store**: 所有使用序列化的 Store（~15 个文件）

**预估减少代码**: ~30 行重复代码

---

### Phase 4: InMemory Store 统一化 ⚠️ **低优先级**

**分析**:
- `InMemoryEventStore` 使用 `ConcurrentDictionary<string, List<StoredEvent>>`
- `InMemoryDeadLetterQueue` 使用 `ConcurrentQueue<DeadLetterMessage>`

**结论**:
- ❌ **不建议统一**
- 原因: 数据结构差异太大（Dictionary vs Queue），强行统一会增加复杂性
- 当前独立实现是合理的

---

### Phase 5: 统一 Options 模式 ⚠️ **低优先级**

**当前状态**:
- Redis: `RedisIdempotencyOptions`, `RedisOutboxOptions`, `RedisInboxOptions`
- NATS: `NatsJSStoreOptions` (统一)

**建议**:
- Redis 可以创建 `RedisStoreOptions` 基类
- 但当前分散的 Options 更灵活
- ❌ **不建议改动**（收益小，风险大）

---

## 📋 实施优先级

### ✅ **立即执行（高价值，低风险）**

#### 1️⃣ Phase 1: 创建 RedisStoreBase
- **价值**: 减少 ~50 行重复代码
- **风险**: 低（纯新增基类）
- **工作量**: 2-3 小时
- **影响文件**: 5 个

#### 2️⃣ Phase 2: NatsJSEventStore 继承 NatsJSStoreBase
- **价值**: 减少 ~50 行重复代码，架构统一
- **风险**: 低（已有基类，只需继承）
- **工作量**: 1 小时
- **影响文件**: 1 个

---

### ⚠️ **可选执行（中等价值）**

#### 3️⃣ Phase 3: 创建 SerializationHelper
- **价值**: 减少 ~30 行重复代码
- **风险**: 低（扩展方法）
- **工作量**: 1-2 小时
- **影响文件**: ~15 个

---

### ❌ **不建议执行**

#### Phase 4: InMemory Store 统一化
- **原因**: 数据结构差异大，强行统一增加复杂性

#### Phase 5: 统一 Options 模式
- **原因**: 收益小，当前设计合理

---

## 📊 预期效果

### 代码减少统计

| Phase | 减少代码行 | 影响文件数 | DRY 提升 |
|-------|-----------|-----------|----------|
| Phase 1: RedisStoreBase | ~50 行 | 5 | ⭐⭐⭐⭐⭐ |
| Phase 2: NatsJSEventStore | ~50 行 | 1 | ⭐⭐⭐⭐⭐ |
| Phase 3: SerializationHelper | ~30 行 | 15 | ⭐⭐⭐ |
| **总计** | **~130 行** | **21** | **优秀** |

---

### 架构改进

**Before**:
```
InMemory: ✅ BaseMemoryStore (统一)
Redis:    ❌ 无 Base 类（分散）
NATS:     ⚠️ 部分统一（EventStore 独立）
```

**After**:
```
InMemory: ✅ BaseMemoryStore (统一)
Redis:    ✅ RedisStoreBase (统一)
NATS:     ✅ NatsJSStoreBase (完全统一)
```

---

## 🎯 推荐执行顺序

### Step 1: Phase 1 - RedisStoreBase ✅
**理由**: 影响最大，Redis 完全缺少 Base 类

### Step 2: Phase 2 - NatsJSEventStore ✅
**理由**: 快速消除重复，架构统一

### Step 3: Phase 3 - SerializationHelper ⚠️
**理由**: 可选，锦上添花

---

## ✅ 验收标准

### Phase 1 完成标准
- [ ] `RedisStoreBase.cs` 文件创建
- [ ] 5 个 Redis Store 继承 `RedisStoreBase`
- [ ] 所有测试通过
- [ ] 减少 ~50 行代码

### Phase 2 完成标准
- [ ] `NatsJSEventStore` 继承 `NatsJSStoreBase`
- [ ] 移除重复的初始化代码
- [ ] 所有测试通过
- [ ] 减少 ~50 行代码

### Phase 3 完成标准
- [ ] `SerializationHelper.cs` 文件创建
- [ ] ~15 个文件使用新 Helper
- [ ] 所有测试通过
- [ ] 减少 ~30 行代码

---

## 🚀 总结

### 关键改进点
1. ✅ **Redis 需要 Base 类**（最高优先级）
2. ✅ **NATS EventStore 需要统一**（高优先级）
3. ⚠️ **序列化可以抽取** Helper（中优先级）
4. ❌ **InMemory 不需要改动**（当前设计合理）

### 预期收益
- 📉 减少 ~130 行重复代码
- 🏗️ 架构更统一
- 📖 代码更易维护
- ✅ DRY 原则贯彻到位

---

<div align="center">

**准备开始实施！建议从 Phase 1 开始。**

</div>

