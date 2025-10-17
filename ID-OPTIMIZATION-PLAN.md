# ID 优化计划 - 统一标识符类型

## 🔍 当前问题分析

### 问题 1: ID 类型混乱
```csharp
// ❌ 混乱的类型
public interface IMessage
{
    public string MessageId => Guid.NewGuid().ToString();  // string
    public string? CorrelationId => null;                   // string?
}

public sealed record MessageId : IEquatable<MessageId>      // 强类型
{
    private readonly long _value;  // 基于 long
}

public sealed record CorrelationId : IEquatable<CorrelationId>  // 强类型
{
    private readonly long _value;  // 基于 long
}
```

**问题：**
- ❌ `IMessage` 接口用 `string`
- ❌ `MessageId` / `CorrelationId` record 用 `long`
- ❌ 实际代码中还用 `Guid.NewGuid().ToString()`
- ❌ 到处都是字符串转换和解析
- ❌ 性能损失（字符串分配）
- ❌ 类型不安全

### 问题 2: Guid.NewGuid().ToString() 到处都是
**发现的文件：**
1. `Catga.AspNetCore\Middleware\CorrelationIdMiddleware.cs`
2. `Catga\Rpc\RpcClient.cs`
3. `Catga.InMemory\InMemoryMessageTransport.cs`
4. `Catga\Core\CatgaTransactionBase.cs`
5. `Catga.Transport.Nats\NatsMessageTransport.cs`
6. `Catga\Messages\MessageContracts.cs` (IMessage 接口默认实现)
7. `Catga.Persistence.Redis\RedisDistributedLock.cs`

**问题：**
- ❌ 每次生成 GUID 都分配字符串
- ❌ GUID 转字符串消耗 CPU
- ❌ 不统一（有的用 MessageId record，有的用 Guid string）

### 问题 3: 字符串到处传递
```csharp
// ❌ 大量的方法签名
public async ValueTask<bool> TryLockMessageAsync(
    string messageId,      // 应该是 MessageId
    TimeSpan lockDuration,
    CancellationToken cancellationToken = default)

public async ValueTask MarkAsProcessedAsync(
    string messageId,      // 应该是 MessageId
    DateTime processedAt,
    CancellationToken cancellationToken = default)
```

---

## 🎯 优化目标

### 1. 统一 ID 类型 ✅
- **所有地方**使用强类型 `MessageId` / `CorrelationId`
- 移除 `IMessage` 接口中的 `string` 类型
- 统一为 `long` 基础类型（Snowflake ID）

### 2. 零分配 ID 生成 ✅
- 使用 `IDistributedIdGenerator` 生成 ID
- ID 内部是 `long`，不分配字符串
- 只在需要序列化时才转字符串

### 3. 类型安全 ✅
- 编译时检查 ID 类型
- 不能错误地传递 MessageId 到 CorrelationId
- IDE 智能提示更准确

---

## 📋 执行计划

### Phase 1: 修复 IMessage 接口 🔴 P0
**当前：**
```csharp
public interface IMessage
{
    public string MessageId => Guid.NewGuid().ToString();
    public string? CorrelationId => null;
}
```

**目标：**
```csharp
public interface IMessage
{
    // 移除默认实现 - 强制用户提供
    public string MessageId { get; }
    public string? CorrelationId { get; }
}
```

**理由：**
- ✅ 不再生成默认 ID（Fail Fast）
- ✅ 用户必须显式提供 ID
- ✅ 避免隐藏的 Guid.NewGuid() 分配

---

### Phase 2: 统一方法签名 🟡 P1
**修改所有接口和实现：**

**Before:**
```csharp
Task<bool> TryLockMessageAsync(string messageId, ...);
Task MarkAsProcessedAsync(string messageId, ...);
Task<string?> GetProcessedResultAsync(string messageId, ...);
```

**After:**
```csharp
Task<bool> TryLockMessageAsync(MessageId messageId, ...);
Task MarkAsProcessedAsync(MessageId messageId, ...);
Task<string?> GetProcessedResultAsync(MessageId messageId, ...);
```

**影响范围：**
- `IInboxStore` / `IOutboxStore`
- `IIdempotencyStore`
- `IDeadLetterQueue`
- 所有实现类（MemoryXxx, RedisXxx, NatsXxx）

---

### Phase 3: 移除 Guid.NewGuid().ToString() 🟡 P1
**替换策略：**

```csharp
// ❌ Before
var id = Guid.NewGuid().ToString();
var corrId = Guid.NewGuid().ToString("N");

// ✅ After
var id = MessageId.NewId(_idGenerator).ToString();
var corrId = CorrelationId.NewId(_idGenerator).ToString();

// ✅ Even Better (if possible, avoid ToString)
var id = MessageId.NewId(_idGenerator);
var corrId = CorrelationId.NewId(_idGenerator);
```

**修改文件：**
1. `CorrelationIdMiddleware.cs`
2. `RpcClient.cs`
3. `InMemoryMessageTransport.cs`
4. `CatgaTransactionBase.cs`
5. `NatsMessageTransport.cs`
6. `RedisDistributedLock.cs`

---

### Phase 4: 添加隐式转换优化 🟢 P2
**当前 `MessageId` 已有：**
```csharp
public static implicit operator string(MessageId id) => id.ToString();
public static implicit operator long(MessageId id) => id._value;
```

**但缺少反向转换：**
```csharp
public static implicit operator MessageId(string value) => Parse(value);
```

**考虑：**
- ⚠️ 隐式 string → MessageId 可能隐藏解析错误
- ✅ 显式转换更安全：`MessageId.Parse(str)`

---

## 🚀 执行策略

### 策略 A: 激进重构（推荐）⚡
**步骤：**
1. 修改 `IMessage` 接口 - 移除默认实现
2. 修改所有接口签名 - `string` → `MessageId`
3. 修改所有实现 - 更新方法签名
4. 替换所有 `Guid.NewGuid().ToString()`
5. 编译修复所有错误

**优点：**
- ✅ 一次性解决所有问题
- ✅ 类型安全
- ✅ 性能最优

**缺点：**
- ⚠️ Breaking Change（但内部 API 可接受）
- ⚠️ 需要修改大量代码

---

### 策略 B: 渐进式重构（保守）🐌
**步骤：**
1. 保留现有 `string` 签名
2. 添加新的 `MessageId` 重载
3. 标记旧方法为 `[Obsolete]`
4. 逐步迁移

**优点：**
- ✅ 兼容性好

**缺点：**
- ❌ 代码重复
- ❌ 迁移周期长
- ❌ 性能改进延迟

---

## 📊 预期收益

### 性能提升
```
Before (Guid.NewGuid().ToString()):
  - Guid 生成: ~16 bytes
  - ToString: ~36 bytes 字符串分配
  - 总分配: ~52 bytes per ID

After (MessageId.NewId):
  - long 生成: 8 bytes (栈上)
  - 无字符串分配（除非需要序列化）
  - 总分配: 0 bytes (直到序列化)

性能提升: ~50+ bytes per message 零分配
```

### 类型安全
```csharp
// ❌ Before - 可能传错
void Process(string messageId, string correlationId)
{
    // 很容易传反参数！
}
Process(corrId, msgId);  // ❌ 编译通过，运行时错误

// ✅ After - 编译时检查
void Process(MessageId messageId, CorrelationId correlationId)
{
    // ...
}
Process(corrId, msgId);  // ✅ 编译错误！类型不匹配
```

### 代码清晰度
```csharp
// ❌ Before
public async Task<string?> GetResultAsync(string id);  // 什么 ID？

// ✅ After
public async Task<string?> GetResultAsync(MessageId messageId);  // 清晰！
```

---

## ⚠️ 风险评估

### 风险 1: Breaking Change
**影响：** 用户代码如果直接使用了接口，需要更新
**缓解：**
- 这是内部 API，大多数用户通过 ICatgaMediator
- 提供迁移指南
- 版本号主版本升级

### 风险 2: 序列化兼容性
**影响：** Redis/数据库中存储的 ID 格式
**缓解：**
- `MessageId.ToString()` 保持兼容
- 持久化层继续使用字符串
- 只在内存中用强类型

---

## ✅ 执行决策

### 推荐：策略 A（激进重构）

**理由：**
1. ✅ Catga 还在早期阶段，Breaking Change 可接受
2. ✅ 长期收益远大于短期痛苦
3. ✅ 类型安全和性能是核心目标
4. ✅ 现在重构比以后更容易

**执行顺序：**
1. Phase 1: 修复 `IMessage` 接口（移除默认实现）
2. Phase 3: 移除所有 `Guid.NewGuid().ToString()`
3. Phase 2: 统一方法签名（可选，如果不影响序列化）
4. 编译、测试、验证

---

## 📝 兼容性策略

### 序列化层保持字符串
```csharp
// ✅ 内存中使用强类型
MessageId msgId = MessageId.NewId(generator);

// ✅ 序列化时转字符串
string serialized = msgId.ToString();  // "123456789012345"

// ✅ 反序列化时解析
MessageId parsed = MessageId.Parse(serialized);
```

### 接口层使用强类型
```csharp
// ✅ 所有方法签名
public interface IInboxStore
{
    Task<bool> TryLockMessageAsync(MessageId messageId, ...);
    Task MarkAsProcessedAsync(MessageId messageId, ...);
}
```

---

**决定：立即执行 Phase 1 和 Phase 3，优化 ID 生成和接口！**

