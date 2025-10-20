# DRY (Don't Repeat Yourself) 重构计划

**创建时间**: 2025-10-20  
**状态**: 📋 计划中  
**目标**: 消除重复代码，提高可维护性，减少bug风险

---

## 📊 代码重复分析

### 🔴 严重重复 (Priority 0)

#### 1. **序列化器基类缺失** ⚠️⚠️⚠️

**问题**: JsonMessageSerializer 和 MemoryPackMessageSerializer 有大量相同代码

**重复代码统计**:
```
SerializeToMemory<T>:           98% 相同 (仅调用不同)
Deserialize(ReadOnlySequence):  95% 相同
TrySerialize<T>:                 95% 相同
Serialize(Memory):               95% 相同
```

**具体重复**:

##### a) SerializeToMemory<T> (2个实现，几乎相同)
```csharp
// JsonMessageSerializer.cs (line 93-101)
public IMemoryOwner<byte> SerializeToMemory<T>(T value)
{
    using var writer = _poolManager.RentBufferWriter();
    Serialize(value, writer);  // ← 唯一差异
    
    var owner = _poolManager.RentMemory(writer.WrittenCount);
    writer.WrittenSpan.CopyTo(owner.Memory.Span);
    return owner;
}

// MemoryPackMessageSerializer.cs (line 81-90) - 完全相同！
public IMemoryOwner<byte> SerializeToMemory<T>(T value)
{
    using var writer = _poolManager.RentBufferWriter();
    MemoryPackSerializer.Serialize(writer, value);  // ← 唯一差异
    
    var owner = _poolManager.RentMemory(writer.WrittenCount);
    writer.WrittenSpan.CopyTo(owner.Memory.Span);
    return owner;
}
```

##### b) Deserialize(ReadOnlySequence) (2个实现，完全相同)
```csharp
// JsonMessageSerializer.cs (line 109-118)
public T? Deserialize<T>(ReadOnlySequence<byte> data)
{
    if (data.IsSingleSegment)
        return Deserialize<T>(data.FirstSpan);  // ← 调用虚方法

    // Multi-segment: rent buffer and copy
    using var owner = _poolManager.RentMemory((int)data.Length);
    data.CopyTo(owner.Memory.Span);
    return Deserialize<T>(owner.Memory.Span);  // ← 调用虚方法
}

// MemoryPackMessageSerializer.cs (line 98-107) - 完全相同！
```

##### c) TrySerialize<T> (2个实现，95%相同)
```csharp
// JsonMessageSerializer.cs (line 137-160)
public bool TrySerialize<T>(T value, Span<byte> destination, out int bytesWritten)
{
    try
    {
        using var pooledWriter = _poolManager.RentBufferWriter(destination.Length);
        Serialize(value, pooledWriter);  // ← 唯一差异
        
        if (pooledWriter.WrittenCount > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }
        
        pooledWriter.WrittenSpan.CopyTo(destination);
        bytesWritten = pooledWriter.WrittenCount;
        return true;
    }
    catch
    {
        bytesWritten = 0;
        return false;
    }
}

// MemoryPackMessageSerializer.cs (line 130-153) - 完全相同！
```

##### d) Serialize(Memory) (2个实现，完全相同)
```csharp
// JsonMessageSerializer.cs (line 163-171)
public void Serialize<T>(T value, Memory<byte> destination, out int bytesWritten)
{
    using var pooledWriter = _poolManager.RentBufferWriter(destination.Length);
    Serialize(value, pooledWriter);  // ← 调用虚方法
    
    if (pooledWriter.WrittenCount > destination.Length)
        throw new InvalidOperationException($"Destination buffer too small. Required: {pooledWriter.WrittenCount}, Available: {destination.Length}");
    
    pooledWriter.WrittenSpan.CopyTo(destination.Span);
    bytesWritten = pooledWriter.WrittenCount;
}

// MemoryPackMessageSerializer.cs (line 157-167) - 完全相同！
```

**影响**:
- ❌ 代码重复: ~200 行
- ❌ 维护成本高: 修改需要改2个地方
- ❌ Bug 风险: 容易只改一处而忘记另一处

---

#### 2. **批量操作模式重复** ⚠️⚠️

**问题**: RedisMessageTransport 的 PublishBatchAsync 和 SendBatchAsync 代码几乎相同

**重复代码**:
```csharp
// PublishBatchAsync (line 113-142)
public async Task PublishBatchAsync<TMessage>(
    IEnumerable<TMessage> messages,
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    ArgumentNullException.ThrowIfNull(messages);

    var messageList = messages as IList<TMessage> ?? messages.ToList();
    if (messageList.Count == 0)
        return;

    var pool = ArrayPool<Task>.Shared;
    var tasks = pool.Rent(messageList.Count);
    try
    {
        for (int i = 0; i < messageList.Count; i++)
        {
            tasks[i] = PublishAsync(messageList[i], context, cancellationToken);  // ← 差异1
        }

        await Task.WhenAll(tasks.AsMemory(0, messageList.Count).ToArray());
    }
    finally
    {
        pool.Return(tasks, clearArray: false);
    }
}

// SendBatchAsync (line 144-174) - 95% 相同！
public async Task SendBatchAsync<TMessage>(
    IEnumerable<TMessage> messages,
    string destination,  // ← 差异2
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    ArgumentNullException.ThrowIfNull(messages);

    var messageList = messages as IList<TMessage> ?? messages.ToList();
    if (messageList.Count == 0)
        return;

    var pool = ArrayPool<Task>.Shared;
    var tasks = pool.Rent(messageList.Count);
    try
    {
        for (int i = 0; i < messageList.Count; i++)
        {
            tasks[i] = SendAsync(messageList[i], destination, context, cancellationToken);  // ← 差异1
        }

        await Task.WhenAll(tasks.AsMemory(0, messageList.Count).ToArray());
    }
    finally
    {
        pool.Return(tasks, clearArray: false);
    }
}
```

**影响**:
- ❌ 代码重复: ~60 行
- ❌ 维护成本: 修改池化逻辑需要改2处

---

### 🟡 中等重复 (Priority 1)

#### 3. **Transport 层相似模式** ⚠️

**问题**: InMemoryMessageTransport, RedisMessageTransport, NatsMessageTransport 可能有相似的模式

需要Review:
- [ ] PublishAsync 模式
- [ ] SubscribeAsync 模式
- [ ] 错误处理模式

---

#### 4. **Persistence 层相似模式** ⚠️

**问题**: RedisOutboxPersistence, RedisInboxPersistence, NatsOutboxStore 等可能有相似模式

需要Review:
- [ ] GetPendingMessagesAsync 模式
- [ ] 批量操作模式
- [ ] 序列化/反序列化模式

---

## 🎯 重构目标

### 主要目标
1. **消除重复**: 减少 80%+ 重复代码
2. **提高可维护性**: 一处修改，全局生效
3. **减少 Bug 风险**: 避免"只改一处忘记另一处"
4. **保持 AOT 兼容**: 所有重构必须 AOT 安全
5. **保持性能**: 不能引入性能回归

### 次要目标
- 提高代码可读性
- 统一错误处理模式
- 简化新增序列化器的流程

---

## 📋 重构计划

### Phase 1: 创建序列化器基类 (P0) ⭐⭐⭐

#### Task 1.1: 创建 `MessageSerializerBase` 抽象基类

**文件**: `src/Catga/Serialization/MessageSerializerBase.cs` (NEW)

**设计**:
```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Pooling;

namespace Catga.Serialization;

/// <summary>
/// Base class for message serializers with common pooling logic (AOT-safe)
/// </summary>
/// <remarks>
/// Provides common implementations for:
/// - SerializeToMemory (pooled)
/// - Deserialize(ReadOnlySequence) (pooled)
/// - TrySerialize (pooled)
/// - Serialize(Memory) (pooled)
/// 
/// Derived classes only need to implement:
/// - Serialize(T, IBufferWriter) - Core serialization
/// - Deserialize(ReadOnlySpan) - Core deserialization
/// </remarks>
public abstract class MessageSerializerBase : IPooledMessageSerializer
{
    protected readonly MemoryPoolManager PoolManager;

    protected MessageSerializerBase(MemoryPoolManager? poolManager = null)
    {
        PoolManager = poolManager ?? MemoryPoolManager.Shared;
    }

    // === Abstract methods (must implement by derived classes) ===

    public abstract string Name { get; }
    
    /// <summary>Core serialization - must implement</summary>
    public abstract void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value, 
        IBufferWriter<byte> bufferWriter);
    
    /// <summary>Core deserialization - must implement</summary>
    public abstract T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data);
    
    public abstract int GetSizeEstimate<T>(T value);

    // === Common implementations (DRY, no duplication) ===

    /// <summary>
    /// Serialize to byte[] using pooled buffer (reduces GC pressure)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = PoolManager.RentBufferWriter(GetSizeEstimate(value));
        Serialize(value, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Serialize to IMemoryOwner (zero-allocation path, caller must dispose)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual IMemoryOwner<byte> SerializeToMemory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = PoolManager.RentBufferWriter(GetSizeEstimate(value));
        Serialize(value, writer);
        
        var owner = PoolManager.RentMemory(writer.WrittenCount);
        writer.WrittenSpan.CopyTo(owner.Memory.Span);
        return owner;
    }

    /// <summary>
    /// Deserialize from byte[] (delegates to Span version)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    /// <summary>
    /// Deserialize from ReadOnlyMemory (delegates to Span version)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlyMemory<byte> data)
        => Deserialize<T>(data.Span);

    /// <summary>
    /// Deserialize from ReadOnlySequence (handles multi-segment with pooling)
    /// </summary>
    public virtual T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
            return Deserialize<T>(data.FirstSpan);

        // Multi-segment: use pooled buffer
        using var owner = PoolManager.RentMemory((int)data.Length);
        data.CopyTo(owner.Memory.Span);
        return Deserialize<T>(owner.Memory.Span);
    }

    /// <summary>
    /// Try to serialize to destination span (zero-allocation if sufficient space)
    /// </summary>
    public virtual bool TrySerialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value, 
        Span<byte> destination, 
        out int bytesWritten)
    {
        try
        {
            using var pooledWriter = PoolManager.RentBufferWriter(destination.Length);
            Serialize(value, pooledWriter);

            if (pooledWriter.WrittenCount > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }

            pooledWriter.WrittenSpan.CopyTo(destination);
            bytesWritten = pooledWriter.WrittenCount;
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    /// <summary>
    /// Serialize to Memory destination (throws if insufficient space)
    /// </summary>
    public virtual void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value, 
        Memory<byte> destination, 
        out int bytesWritten)
    {
        using var pooledWriter = PoolManager.RentBufferWriter(destination.Length);
        Serialize(value, pooledWriter);

        if (pooledWriter.WrittenCount > destination.Length)
            throw new InvalidOperationException(
                $"Destination buffer too small. Required: {pooledWriter.WrittenCount}, Available: {destination.Length}");

        pooledWriter.WrittenSpan.CopyTo(destination.Span);
        bytesWritten = pooledWriter.WrittenCount;
    }

    /// <summary>
    /// Serialize to PooledBuffer (IPooledMessageSerializer implementation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual PooledBuffer SerializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        var owner = SerializeToMemory(value);
        return new PooledBuffer(owner, owner.Memory.Length);
    }

    /// <summary>
    /// Deserialize from ReadOnlySequence (IPooledMessageSerializer implementation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T? DeserializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
        => Deserialize<T>(data);

    /// <summary>
    /// Get pooled buffer writer (IPooledMessageSerializer implementation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual IPooledBufferWriter<byte> GetPooledWriter(int initialCapacity = 256)
        => PoolManager.RentBufferWriter(initialCapacity);

    // === Non-generic overloads (optional, can be overridden) ===

    public virtual byte[] Serialize(
        object? value, 
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic serialization. Use generic Serialize<T> instead.");
    }

    public virtual object? Deserialize(
        byte[] data, 
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic deserialization. Use generic Deserialize<T> instead.");
    }

    public virtual void Serialize(
        object? value, 
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, 
        IBufferWriter<byte> bufferWriter)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic serialization. Use generic Serialize<T> instead.");
    }

    public virtual object? Deserialize(
        ReadOnlySpan<byte> data, 
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic deserialization. Use generic Deserialize<T> instead.");
    }

    public virtual int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IEnumerable<T> values, 
        IBufferWriter<byte> bufferWriter)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support batch serialization.");
    }
}
```

**优点**:
- ✅ 消除 ~200 行重复代码
- ✅ 新增序列化器只需实现 3 个核心方法
- ✅ 所有池化逻辑集中管理
- ✅ 100% AOT 兼容
- ✅ Virtual 方法可按需覆盖

---

#### Task 1.2: 重构 JsonMessageSerializer

**Before** (212 lines):
```csharp
public class JsonMessageSerializer : IPooledMessageSerializer
{
    private readonly JsonSerializerOptions _options;
    private readonly MemoryPoolManager _poolManager;
    
    // ... 大量重复实现 ...
}
```

**After** (~100 lines):
```csharp
public class JsonMessageSerializer : MessageSerializerBase
{
    private readonly JsonSerializerOptions _options;

    public JsonMessageSerializer() 
        : this(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false }) { }

    public JsonMessageSerializer(JsonSerializerOptions options) 
        : this(options, null) { }

    public JsonMessageSerializer(JsonSerializerOptions options, MemoryPoolManager? poolManager)
        : base(poolManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override string Name => "JSON";

    // 只需实现核心方法 (3个)
    public override void Serialize<T>(T value, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    public override T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options);
    }

    public override int GetSizeEstimate<T>(T value) => 256;

    // 非泛型重载（可选，支持 System.Text.Json 特性）
    public override byte[] Serialize(object? value, Type type)
    {
        using var bufferWriter = PoolManager.RentBufferWriter(256);
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type, _options);
        return bufferWriter.WrittenSpan.ToArray();
    }

    public override object? Deserialize(byte[] data, Type type)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, type, _options);
    }

    // ... 其他非泛型重载 ...
}
```

**减少**: ~112 lines (212 → ~100) = -53% 代码量 ✅

---

#### Task 1.3: 重构 MemoryPackMessageSerializer

**Before** (221 lines):
```csharp
public class MemoryPackMessageSerializer : IPooledMessageSerializer
{
    private readonly MemoryPoolManager _poolManager;
    
    // ... 大量重复实现 ...
}
```

**After** (~80 lines):
```csharp
public class MemoryPackMessageSerializer : MessageSerializerBase
{
    public MemoryPackMessageSerializer() 
        : this(null) { }

    public MemoryPackMessageSerializer(MemoryPoolManager? poolManager)
        : base(poolManager) { }

    public override string Name => "MemoryPack";

    // 只需实现核心方法 (3个)
    public override void Serialize<T>(T value, IBufferWriter<byte> bufferWriter)
        => MemoryPackSerializer.Serialize(bufferWriter, value);

    public override T? Deserialize<T>(ReadOnlySpan<byte> data)
        => MemoryPackSerializer.Deserialize<T>(data);

    public override int GetSizeEstimate<T>(T value) => 128;

    // 非泛型重载（可选）
    public override byte[] Serialize(object? value, Type type)
        => MemoryPackSerializer.Serialize(type, value);

    public override object? Deserialize(byte[] data, Type type)
        => MemoryPackSerializer.Deserialize(type, data);

    // SerializeBatch 特殊实现（MemoryPack 特有）
    public override int SerializeBatch<T>(IEnumerable<T> values, IBufferWriter<byte> bufferWriter)
    {
        int totalBytes = 0;
        Span<byte> lengthBuffer = stackalloc byte[4];
        
        var count = values is ICollection<T> collection ? collection.Count : values.Count();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, count);
        bufferWriter.Write(lengthBuffer);
        totalBytes += 4;

        foreach (var value in values)
        {
            using var itemWriter = PoolManager.RentBufferWriter();
            MemoryPackSerializer.Serialize(itemWriter, value);

            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, itemWriter.WrittenCount);
            bufferWriter.Write(lengthBuffer);
            totalBytes += 4;

            bufferWriter.Write(itemWriter.WrittenSpan);
            totalBytes += itemWriter.WrittenCount;
        }

        return totalBytes;
    }
}
```

**减少**: ~141 lines (221 → ~80) = -64% 代码量 ✅

---

### Phase 2: 提取批量操作辅助类 (P1) ⭐⭐

#### Task 2.1: 创建 `BatchOperationHelper`

**文件**: `src/Catga/Core/BatchOperationHelper.cs` (NEW)

**设计**:
```csharp
using System.Buffers;

namespace Catga.Core;

/// <summary>
/// Helper for pooled batch operations (AOT-safe)
/// </summary>
public static class BatchOperationHelper
{
    /// <summary>
    /// Execute batch async operations with pooled Task array
    /// </summary>
    /// <typeparam name="T">Item type</typeparam>
    /// <param name="items">Items to process</param>
    /// <param name="operation">Async operation for each item</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>
    /// AOT-safe. Uses ArrayPool to reduce GC pressure.
    /// </remarks>
    public static async Task ExecuteBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);

        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        var pool = ArrayPool<Task>.Shared;
        var tasks = pool.Rent(itemList.Count);
        try
        {
            for (int i = 0; i < itemList.Count; i++)
            {
                tasks[i] = operation(itemList[i]);
            }

            await Task.WhenAll(tasks.AsMemory(0, itemList.Count).ToArray());
        }
        finally
        {
            pool.Return(tasks, clearArray: false);
        }
    }
}
```

#### Task 2.2: 重构 RedisMessageTransport

**Before**:
```csharp
public async Task PublishBatchAsync<TMessage>(...)
{
    // ... 30 lines of pooling logic ...
}

public async Task SendBatchAsync<TMessage>(...)
{
    // ... 30 lines of pooling logic (duplicate) ...
}
```

**After**:
```csharp
public async Task PublishBatchAsync<TMessage>(
    IEnumerable<TMessage> messages,
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    await BatchOperationHelper.ExecuteBatchAsync(
        messages,
        m => PublishAsync(m, context, cancellationToken),
        cancellationToken);
}

public async Task SendBatchAsync<TMessage>(
    IEnumerable<TMessage> messages,
    string destination,
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    await BatchOperationHelper.ExecuteBatchAsync(
        messages,
        m => SendAsync(m, destination, context, cancellationToken),
        cancellationToken);
}
```

**减少**: ~50 lines (60 → ~10) = -83% 代码量 ✅

---

### Phase 3: Review 其他重复模式 (P2) ⭐

#### Task 3.1: Transport 层 Review

**范围**:
- InMemoryMessageTransport
- RedisMessageTransport
- NatsMessageTransport

**检查项**:
- [ ] 是否有共同的订阅模式？
- [ ] 是否有共同的错误处理？
- [ ] 是否可以提取基类？

#### Task 3.2: Persistence 层 Review

**范围**:
- RedisOutboxPersistence
- RedisInboxPersistence
- NatsJSOutboxStore
- NatsJSInboxStore

**检查项**:
- [ ] 批量操作是否相似？
- [ ] 序列化模式是否相似？
- [ ] 是否可以提取基类？

---

## 📊 预期收益

### 代码量减少
```
JsonMessageSerializer:          212 → ~100 lines (-53%)
MemoryPackMessageSerializer:    221 → ~80  lines (-64%)
RedisMessageTransport:          230 → ~180 lines (-22%)
BatchOperationHelper:           NEW +50 lines

总计:                           663 → 410 lines (-38%)
实际减少:                       -253 lines
```

### 维护性提升
```
Before:
- 修改池化逻辑: 需要改 4-6 处
- 新增序列化器: 需要实现 15+ 方法
- Bug 风险:      高 (容易漏改)

After:
- 修改池化逻辑: 只改 1 处 (MessageSerializerBase)
- 新增序列化器: 只需实现 3 个核心方法
- Bug 风险:      低 (DRY 保证)
```

### 一致性提升
```
Before:
- 2个序列化器的 SerializeToMemory 实现不同 ❌
- 微小差异可能导致不一致行为

After:
- 所有序列化器使用相同基类实现 ✅
- 行为完全一致
```

---

## ⚠️ 风险与缓解

### 技术风险

| 风险 | 等级 | 缓解措施 |
|------|------|----------|
| Breaking Changes | 🟡 中 | 保持公共 API 不变，只重构内部实现 |
| 性能回归 | 🟢 低 | virtual 方法有微小开销，但可忽略 |
| AOT 兼容性 | 🟢 低 | 基类不使用反射，保持 AOT 安全 |

### 实施风险

| 风险 | 等级 | 缓解措施 |
|------|------|----------|
| 测试覆盖不足 | 🟡 中 | 所有现有测试必须通过 |
| 遗漏边缘情况 | 🟡 中 | 逐步重构，保留旧实现对比 |

---

## 📅 实施计划

### Week 1: Phase 1 - 序列化器基类
- [ ] Day 1-2: 创建 MessageSerializerBase
- [ ] Day 3: 重构 JsonMessageSerializer
- [ ] Day 4: 重构 MemoryPackMessageSerializer
- [ ] Day 5: 测试验证 (所有27个测试必须通过)

### Week 2: Phase 2 - 批量操作
- [ ] Day 1: 创建 BatchOperationHelper
- [ ] Day 2: 重构 RedisMessageTransport
- [ ] Day 3: 测试验证

### Week 3: Phase 3 - Review 其他模式
- [ ] Day 1-2: Transport 层 Review
- [ ] Day 3-4: Persistence 层 Review
- [ ] Day 5: 文档更新

---

## ✅ 验收标准

### Must Have
- [ ] 所有现有测试通过 (194 单元测试 + 27 集成测试)
- [ ] 无编译警告
- [ ] 无 linter 错误
- [ ] AOT 兼容性保持
- [ ] 代码量减少 > 30%

### Should Have
- [ ] 新增单元测试覆盖基类
- [ ] 性能无回归 (Benchmark 验证)
- [ ] 文档更新

### Nice to Have
- [ ] 示例代码展示新的简洁性
- [ ] 迁移指南（如果有 breaking changes）

---

## 📝 总结

### 关键问题
1. **代码重复严重**: JsonMessageSerializer 和 MemoryPackMessageSerializer 有 ~200 行重复代码
2. **维护成本高**: 修改需要改多处
3. **一致性风险**: 容易出现微小差异

### 解决方案
1. **MessageSerializerBase**: 抽象基类统一池化逻辑
2. **BatchOperationHelper**: 统一批量操作模式
3. **逐步 Review**: 发现更多重复模式

### 预期收益
- **代码量**: -38% (-253 lines)
- **维护性**: 大幅提升（1处修改 vs 4-6处）
- **一致性**: 完全保证
- **扩展性**: 新增序列化器只需 3 个方法

---

**Created by**: Catga Team  
**Next Review**: 实施Phase 1后

