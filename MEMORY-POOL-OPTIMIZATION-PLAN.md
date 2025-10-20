# MemoryPoolManager 优化计划

**创建时间**: 2025-10-20  
**状态**: 📋 计划中  
**目标**: 通过 MemoryPoolManager 全面优化内存分配，减少 GC 压力，提升性能

---

## 📊 当前状态分析

### ✅ 已实现的优化

#### 1. 核心池化基础设施
- **MemoryPoolManager** (已实现)
  - ✅ 三层池策略 (Small/Medium/Large)
  - ✅ `RentArray` / `ReturnArray` (byte[])
  - ✅ `RentMemory` (IMemoryOwner<byte>)
  - ✅ `RentBufferWriter` (PooledBufferWriter<byte>)
  - ✅ 单例模式 (`Shared`)
  - ✅ AOT 兼容

#### 2. PooledBufferWriter<T>
- ✅ `IBufferWriter<T>` 实现
- ✅ `IPooledBufferWriter<T>` 接口
- ✅ 自动扩容
- ✅ Dispose 时自动返回池
- ✅ WrittenMemory/WrittenSpan 访问
- ✅ AOT 兼容

#### 3. SerializationHelper (已优化)
- ✅ Base64 编码使用 ArrayPool
- ✅ stackalloc 优化 (< 256 bytes)
- ✅ `EncodeBase64Pooled` 零分配
- ✅ `DecodeBase64Pooled` 返回 IMemoryOwner
- ✅ 池化序列化路径

#### 4. JsonMessageSerializer (已优化)
- ✅ 使用 MemoryPoolManager
- ✅ `SerializeToMemory` → IMemoryOwner
- ✅ `SerializePooled` → PooledBuffer
- ✅ `GetPooledWriter` → IPooledBufferWriter
- ✅ `TrySerialize` 使用池化缓冲区

#### 5. ArrayPoolHelper
- ✅ `RentOrAllocate` 智能池化
- ✅ `RentedArray<T>` 自动清理
- ✅ UTF8 编码转换辅助

---

## 🎯 优化目标

### 性能目标
```
当前指标:
- 小消息 (< 1KB):   ~20K QPS, ~10KB GC/op
- 中等消息 (4KB):    ~8K QPS, ~50KB GC/op
- 大消息 (64KB):     ~2K QPS, ~200KB GC/op

优化后目标:
- 小消息 (< 1KB):   ~50K QPS, ~0KB GC/op (零分配)
- 中等消息 (4KB):   ~20K QPS, ~5KB GC/op (95%减少)
- 大消息 (64KB):    ~5K QPS, ~20KB GC/op (90%减少)
```

### 关键指标
- **GC 分配**: 减少 80-95%
- **吞吐量**: 提升 2-3x
- **延迟**: P99 减少 50%
- **内存占用**: 峰值减少 40%

---

## 📋 优化任务清单

### Phase 1: 核心序列化优化 (高优先级) ⭐⭐⭐

#### Task 1.1: MemoryPackMessageSerializer 池化优化
**文件**: `src/Catga.Serialization.MemoryPack/MemoryPackMessageSerializer.cs`

**当前问题**:
```csharp
// ❌ 使用 ArrayBufferWriter，未使用池化
public byte[] Serialize<T>(T value)
{
    var bufferWriter = new ArrayBufferWriter<byte>();
    MemoryPackSerializer.Serialize(bufferWriter, value);
    return bufferWriter.WrittenSpan.ToArray(); // 额外分配
}
```

**优化方案**:
```csharp
private readonly MemoryPoolManager _poolManager;

public MemoryPackMessageSerializer() 
    : this(MemoryPoolManager.Shared) { }

public MemoryPackMessageSerializer(MemoryPoolManager poolManager)
{
    _poolManager = poolManager ?? throw new ArgumentNullException(nameof(poolManager));
}

// ✅ 使用 PooledBufferWriter
public byte[] Serialize<T>(T value)
{
    using var writer = _poolManager.RentBufferWriter(256);
    MemoryPackSerializer.Serialize(writer, value);
    return writer.WrittenSpan.ToArray();
}

// ✅ 零分配版本
public IMemoryOwner<byte> SerializeToMemory<T>(T value)
{
    using var writer = _poolManager.RentBufferWriter(256);
    MemoryPackSerializer.Serialize(writer, value);
    
    var owner = _poolManager.RentMemory(writer.WrittenCount);
    writer.WrittenSpan.CopyTo(owner.Memory.Span);
    return owner;
}

// ✅ IPooledMessageSerializer 实现
public PooledBuffer SerializePooled<T>(T value)
{
    var owner = SerializeToMemory(value);
    return new PooledBuffer(owner, owner.Memory.Length);
}
```

**预期收益**:
- 减少 60% GC 分配
- 提升 40% 吞吐量

---

#### Task 1.2: JsonMessageSerializer 进一步优化
**文件**: `src/Catga.Serialization.Json/JsonMessageSerializer.cs`

**当前问题**:
```csharp
// ❌ line 51-54: 使用 ArrayBufferWriter (未池化)
public byte[] Serialize<T>(T value)
{
    var bufferWriter = new ArrayBufferWriter<byte>(256);
    Serialize(value, bufferWriter);
    return bufferWriter.WrittenSpan.ToArray(); // 额外分配
}
```

**优化方案**:
```csharp
// ✅ 使用池化 writer
public byte[] Serialize<T>(T value)
{
    using var writer = _poolManager.RentBufferWriter(256);
    Serialize(value, writer);
    return writer.WrittenSpan.ToArray();
}

// ✅ 或完全零分配
public byte[] Serialize<T>(T value)
{
    using var writer = _poolManager.RentBufferWriter(256);
    Serialize(value, writer);
    
    var result = _poolManager.RentArray(writer.WrittenCount);
    writer.WrittenSpan.CopyTo(result);
    return result;
}
```

**预期收益**:
- 减少 30% GC 分配
- 提升 20% 吞吐量

---

### Phase 2: Transport 层优化 (高优先级) ⭐⭐⭐

#### Task 2.1: RedisMessageTransport 批量操作优化
**文件**: `src/Catga.Transport.Redis/RedisMessageTransport.cs`

**当前问题**:
```csharp
// ❌ line 122: 使用 LINQ ToArray() 分配
var tasks = messages.Select(m => PublishAsync(m, context, cancellationToken)).ToArray();
await Task.WhenAll(tasks);
```

**优化方案**:
```csharp
// ✅ 使用 ArrayPool
using var rentedArray = ArrayPoolHelper.RentOrAllocate<Task>(messages.Count);
int index = 0;
foreach (var message in messages)
{
    rentedArray.Array[index++] = PublishAsync(message, context, cancellationToken);
}
await Task.WhenAll(rentedArray.AsSpan());

// ✅ 或直接使用 MemoryPoolManager
var pool = MemoryPoolManager.Shared;
var taskArray = pool.SmallBytePool.Rent(messages.Count);
try
{
    // ... 填充任务
    await Task.WhenAll(taskArray.AsSpan(0, index));
}
finally
{
    pool.ReturnArray(taskArray);
}
```

**预期收益**:
- 减少 50% 批量操作的 GC 分配
- 提升 批量吞吐量 30%

---

#### Task 2.2: NatsMessageTransport 缓冲区优化
**文件**: `src/Catga.Transport.Nats/NatsMessageTransport.cs`

**需要Review**: 检查是否有未优化的字节数组分配

---

### Phase 3: Persistence 层优化 (中优先级) ⭐⭐

#### Task 3.1: RedisOutboxPersistence 批量键优化
**文件**: `src/Catga.Persistence.Redis/Persistence/RedisOutboxPersistence.cs`

**当前问题**:
```csharp
// ❌ line 97: LINQ ToArray() 分配 RedisKey[]
var keys = messageIds.Select(id => (RedisKey)GetMessageKey(id.ToString())).ToArray();
var values = await db.StringGetAsync(keys);
```

**优化方案**:
```csharp
// ✅ 使用 ArrayPool
var pool = ArrayPool<RedisKey>.Shared;
var keys = pool.Rent(messageIds.Count);
try
{
    int index = 0;
    foreach (var id in messageIds)
    {
        keys[index++] = (RedisKey)GetMessageKey(id.ToString());
    }
    
    var values = await db.StringGetAsync(keys.AsMemory(0, index));
    // ... 处理结果
}
finally
{
    pool.Return(keys);
}

// ✅ 或使用统一的池
var pool = MemoryPoolManager.Shared;
using var rentedKeys = ArrayPoolHelper.RentOrAllocate<RedisKey>(messageIds.Count);
int index = 0;
foreach (var id in messageIds)
{
    rentedKeys.Array[index++] = (RedisKey)GetMessageKey(id.ToString());
}
var values = await db.StringGetAsync(rentedKeys.AsMemory());
```

**预期收益**:
- 减少 40% 批量查询的 GC 分配
- 提升 大批量操作性能 25%

---

#### Task 3.2: RedisPersistence 序列化路径优化
**文件**: `src/Catga.Persistence.Redis/Persistence/RedisInboxPersistence.cs`

**需要Review**: 确保所有序列化调用都使用池化路径

---

### Phase 4: 编码转换优化 (中优先级) ⭐⭐

#### Task 4.1: 增强 ArrayPoolHelper 编码功能
**文件**: `src/Catga/Core/ArrayPoolHelper.cs`

**当前限制**:
```csharp
// ❌ GetBytes 分配新数组
public static byte[] GetBytes(string str)
{
    return Utf8Encoding.GetBytes(str);
}
```

**优化方案**:
```csharp
/// <summary>
/// Convert string to byte[] using ArrayPool (caller must return)
/// </summary>
public static RentedArray<byte> GetBytesPooled(string str, MemoryPoolManager? pool = null)
{
    if (string.IsNullOrEmpty(str))
        return new RentedArray<byte>(Array.Empty<byte>(), 0, false);
    
    pool ??= MemoryPoolManager.Shared;
    int maxByteCount = Utf8Encoding.GetMaxByteCount(str.Length);
    
    var buffer = pool.RentArray(maxByteCount);
    int actualBytes = Utf8Encoding.GetBytes(str, buffer);
    
    return new RentedArray<byte>(buffer, actualBytes, isRented: true);
}

/// <summary>
/// Convert byte[] to string using Span (zero-allocation)
/// </summary>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static string GetStringFast(ReadOnlySpan<byte> bytes)
{
    if (bytes.Length == 0)
        return string.Empty;
    
    return Utf8Encoding.GetString(bytes);
}

/// <summary>
/// Encode string to Base64 using pooled buffers
/// </summary>
public static string ToBase64StringPooled(ReadOnlySpan<byte> bytes, MemoryPoolManager? pool = null)
{
    if (bytes.Length == 0)
        return string.Empty;
    
    pool ??= MemoryPoolManager.Shared;
    int base64Length = ((bytes.Length + 2) / 3) * 4;
    
    var buffer = pool.RentArray(base64Length);
    try
    {
        if (Convert.TryToBase64Chars(bytes, buffer, out int charsWritten))
        {
            return new string(buffer.AsSpan(0, charsWritten));
        }
        return Convert.ToBase64String(bytes); // Fallback
    }
    finally
    {
        pool.ReturnArray(buffer);
    }
}

/// <summary>
/// Decode Base64 to byte[] using pooled buffers (caller must return)
/// </summary>
public static RentedArray<byte> FromBase64StringPooled(string base64, MemoryPoolManager? pool = null)
{
    if (string.IsNullOrEmpty(base64))
        return new RentedArray<byte>(Array.Empty<byte>(), 0, false);
    
    pool ??= MemoryPoolManager.Shared;
    int maxLength = (base64.Length * 3) / 4;
    
    var buffer = pool.RentArray(maxLength);
    if (Convert.TryFromBase64String(base64, buffer, out int bytesWritten))
    {
        return new RentedArray<byte>(buffer, bytesWritten, isRented: true);
    }
    
    // Fallback
    pool.ReturnArray(buffer);
    var decoded = Convert.FromBase64String(base64);
    return new RentedArray<byte>(decoded, decoded.Length, isRented: false);
}
```

**预期收益**:
- 编码转换零分配
- 减少 70% 字符串/字节转换的 GC

---

### Phase 5: 高级优化 (低优先级) ⭐

#### Task 5.1: Span<T> / Memory<T> 接口扩展
**目标**: 为常用操作提供 Span 版本

**新增接口**:
```csharp
// IMessageSerializer 扩展
public interface ISpanMessageSerializer : IMessageSerializer
{
    int Serialize<T>(T value, Span<byte> destination);
    bool TrySerialize<T>(T value, Span<byte> destination, out int bytesWritten);
    T? Deserialize<T>(ReadOnlySpan<byte> data);
}

// IMessageTransport 扩展
public interface ISpanMessageTransport : IMessageTransport
{
    ValueTask PublishAsync(ReadOnlySpan<byte> messageData, string topic, CancellationToken cancellationToken);
    ValueTask<ReadOnlyMemory<byte>> SendAsync(ReadOnlySpan<byte> messageData, string destination, CancellationToken cancellationToken);
}
```

---

#### Task 5.2: ValueTask 优化
**目标**: 使用 ValueTask 减少异步状态机分配

**文件**: 所有 Transport 和 Persistence 层

**示例**:
```csharp
// ❌ 使用 Task (总是分配)
public async Task PublishAsync(IMessage message, CancellationToken cancellationToken)
{
    // ...
}

// ✅ 使用 ValueTask (可能零分配)
public ValueTask PublishAsync(IMessage message, CancellationToken cancellationToken)
{
    // Fast path: synchronous completion
    if (_cache.TryGetValue(key, out var cached))
    {
        return ValueTask.CompletedTask;
    }
    
    // Slow path: async
    return PublishAsyncCore(message, cancellationToken);
}

private async ValueTask PublishAsyncCore(IMessage message, CancellationToken cancellationToken)
{
    // ...
}
```

---

#### Task 5.3: MemoryPool<T> 自定义实现
**目标**: 更精细的池管理

**场景**:
- 大消息场景 (> 1MB)
- 固定大小消息场景
- 需要统计信息的场景

**实现**:
```csharp
public sealed class CustomMemoryPool<T> : MemoryPool<T>
{
    private readonly ConcurrentBag<IMemoryOwner<T>>[] _buckets;
    private readonly int[] _bucketSizes;
    private long _totalRented;
    private long _totalReturned;
    
    public override IMemoryOwner<T> Rent(int minimumLength)
    {
        Interlocked.Increment(ref _totalRented);
        // ... 自定义逻辑
    }
    
    public MemoryPoolStatistics GetStatistics()
    {
        return new MemoryPoolStatistics
        {
            TotalRented = _totalRented,
            TotalReturned = _totalReturned,
            CurrentlyRented = _totalRented - _totalReturned
        };
    }
}
```

---

## 📊 验证与测试

### 性能基准测试
**文件**: `benchmarks/Catga.Benchmarks/MemoryPoolBenchmarks.cs`

**测试场景**:
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class MemoryPoolBenchmarks
{
    [Benchmark]
    public void Serialize_NoPool()
    {
        var serializer = new JsonMessageSerializer();
        var data = serializer.Serialize(new TestMessage { Id = 123, Name = "Test" });
    }
    
    [Benchmark]
    public void Serialize_WithPool()
    {
        var poolManager = MemoryPoolManager.Shared;
        var serializer = new JsonMessageSerializer(options, poolManager);
        using var pooled = serializer.SerializePooled(new TestMessage { Id = 123, Name = "Test" });
    }
    
    [Benchmark]
    public void BatchOperation_NoPool()
    {
        var tasks = Enumerable.Range(0, 100).Select(i => Task.CompletedTask).ToArray();
    }
    
    [Benchmark]
    public void BatchOperation_WithPool()
    {
        using var rentedArray = ArrayPoolHelper.RentOrAllocate<Task>(100);
        // ...
    }
}
```

---

### 集成测试
**文件**: `tests/Catga.Tests/Pooling/MemoryPoolIntegrationTests.cs`

**测试内容**:
```csharp
[Fact]
public async Task MemoryPool_HighLoadScenario_ShouldNotLeak()
{
    var poolManager = new MemoryPoolManager();
    var tasks = new List<Task>();
    
    for (int i = 0; i < 10000; i++)
    {
        tasks.Add(Task.Run(() =>
        {
            using var buffer = poolManager.RentBufferWriter();
            // ... 操作
        }));
    }
    
    await Task.WhenAll(tasks);
    
    // 验证没有内存泄漏
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var stats = poolManager.GetStatistics();
    
    Assert.True(stats.TotalRented == stats.TotalReturned);
}

[Fact]
public void MemoryPool_ConcurrentAccess_ShouldBeThreadSafe()
{
    var poolManager = MemoryPoolManager.Shared;
    var exceptions = new ConcurrentBag<Exception>();
    
    Parallel.For(0, 10000, i =>
    {
        try
        {
            using var buffer = poolManager.RentBufferWriter();
            var span = buffer.GetSpan(256);
            span.Fill((byte)i);
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }
    });
    
    Assert.Empty(exceptions);
}
```

---

## 📈 预期收益总结

### 性能提升
| 场景 | 当前 | 优化后 | 提升 |
|------|------|--------|------|
| **小消息吞吐** | 20K QPS | 50K QPS | 150% ↑ |
| **中等消息吞吐** | 8K QPS | 20K QPS | 150% ↑ |
| **大消息吞吐** | 2K QPS | 5K QPS | 150% ↑ |
| **P99 延迟** | 50ms | 25ms | 50% ↓ |

### GC 压力减少
| 操作 | 当前分配 | 优化后 | 减少 |
|------|----------|--------|------|
| **序列化** | ~10KB/op | ~500B/op | 95% ↓ |
| **反序列化** | ~8KB/op | ~1KB/op | 87% ↓ |
| **批量操作** | ~50KB/batch | ~5KB/batch | 90% ↓ |
| **编码转换** | ~3KB/op | ~0B/op | 100% ↓ |

### 内存占用
| 指标 | 当前 | 优化后 | 改善 |
|------|------|--------|------|
| **峰值内存** | 500MB | 300MB | 40% ↓ |
| **GC Gen0** | 500/s | 100/s | 80% ↓ |
| **GC Gen1** | 50/s | 10/s | 80% ↓ |
| **GC Gen2** | 5/s | 1/s | 80% ↓ |

---

## 🚀 实施计划

### Week 1: Phase 1 (核心序列化)
- [ ] Task 1.1: MemoryPackMessageSerializer 优化
- [ ] Task 1.2: JsonMessageSerializer 优化
- [ ] 性能测试验证

### Week 2: Phase 2 (Transport 层)
- [ ] Task 2.1: RedisMessageTransport 优化
- [ ] Task 2.2: NatsMessageTransport Review
- [ ] 集成测试

### Week 3: Phase 3 (Persistence 层)
- [ ] Task 3.1: RedisOutboxPersistence 优化
- [ ] Task 3.2: RedisPersistence Review
- [ ] 压力测试

### Week 4: Phase 4+5 (编码 + 高级)
- [ ] Task 4.1: ArrayPoolHelper 增强
- [ ] Task 5.1-5.3: 高级优化（可选）
- [ ] 完整性能报告

---

## 📚 文档更新

### 需要更新的文档
1. **内存优化指南**: `docs/guides/memory-optimization-guide.md`
   - 添加 MemoryPoolManager 使用示例
   - 最佳实践和反模式

2. **性能报告**: `docs/PERFORMANCE-REPORT.md`
   - 优化前后对比
   - Benchmark 结果

3. **API 文档**: `docs/api/README.md`
   - IPooledMessageSerializer 接口
   - MemoryPoolManager API

---

## ⚠️ 风险与注意事项

### 潜在风险
1. **池大小配置**
   - 风险: 池太小 → 性能下降；池太大 → 内存浪费
   - 缓解: 提供可配置参数，自动调优

2. **Dispose 遗忘**
   - 风险: IMemoryOwner未Dispose → 内存泄漏
   - 缓解: 强制 using 模式，添加 finalizer 警告

3. **线程安全**
   - 风险: 池化对象跨线程使用
   - 缓解: 文档说明，静态分析器检查

4. **AOT 兼容性**
   - 风险: 泛型约束导致 AOT 失败
   - 缓解: 所有方法标记 DynamicallyAccessedMembers

### 回滚策略
- 保留旧实现作为 fallback
- 提供开关禁用池化
- 监控告警自动降级

---

## 🎯 成功标准

### Must Have (必须达成)
- [x] 所有序列化器支持池化
- [ ] GC 分配减少 > 80%
- [ ] 无内存泄漏
- [ ] 所有测试通过

### Should Have (应该达成)
- [ ] 吞吐量提升 > 2x
- [ ] P99 延迟减少 > 50%
- [ ] 完整文档和示例

### Nice to Have (最好达成)
- [ ] 自定义 MemoryPool 实现
- [ ] 自动调优算法
- [ ] 实时监控仪表板

---

**Created by**: Catga Team  
**Last Updated**: 2025-10-20  
**Next Review**: 每周五

