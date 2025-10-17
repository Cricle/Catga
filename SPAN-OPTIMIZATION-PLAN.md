# 🚀 Span<T> 优化计划

## 📊 优化目标

通过使用 `Span<T>` / `ReadOnlySpan<T>` / `Memory<T>` 减少内存分配和拷贝，提升性能。

---

## 🎯 优化分类

### Priority 0: 高价值优化（序列化层）✅ 已完成

#### ✅ IBufferedMessageSerializer 接口（已实现）
**文件**: `src/Catga/Abstractions/IBufferedMessageSerializer.cs`

**现状**: ✅ 已支持 `ReadOnlySpan<byte>` 和 `IBufferWriter<byte>`

```csharp
public interface IBufferedMessageSerializer : IMessageSerializer
{
    void Serialize<T>(T value, IBufferWriter<byte> bufferWriter);  // ✅ 零拷贝写入
    T? Deserialize<T>(ReadOnlySpan<byte> data);                     // ✅ 零拷贝读取
    int GetSizeEstimate<T>(T value);
}
```

**收益**: ✅ 已实现零分配序列化/反序列化（MemoryPack + JSON）

---

### Priority 1: RPC 层优化（高频调用）🔥

#### 1.1 RpcMessage 改为使用 ReadOnlyMemory<byte>

**文件**: `src/Catga/Rpc/RpcMessage.cs`

**问题**:
```csharp
public sealed class RpcRequest
{
    public required byte[] Payload { get; set; }  // ❌ 强制分配和拷贝
}

public sealed class RpcResponse
{
    public byte[]? Payload { get; set; }  // ❌ 强制分配和拷贝
}
```

**优化方案**:
```csharp
public sealed class RpcRequest
{
    // ✅ 支持零拷贝的 Payload
    public required ReadOnlyMemory<byte> Payload { get; set; }

    // 向后兼容
    public byte[] PayloadArray => Payload.ToArray();
}

public sealed class RpcResponse
{
    // ✅ 支持零拷贝的 Payload
    public ReadOnlyMemory<byte>? Payload { get; set; }

    // 向后兼容
    public byte[]? PayloadArray => Payload?.ToArray();
}
```

**收益**:
- 减少 RPC 调用中 2 次数组分配（请求 + 响应）
- 减少 2 次内存拷贝
- 估计性能提升：**+15-30%**（高频 RPC 场景）

**破坏性**: ⚠️ 中等
- 需要更新 `IRpcHandler.HandleAsync` 签名
- 需要更新所有 RPC 相关代码

---

#### 1.2 IRpcHandler 改为使用 ReadOnlySpan<byte>

**文件**: `src/Catga/Rpc/RpcServer.cs`

**问题**:
```csharp
internal interface IRpcHandler
{
    Task<byte[]> HandleAsync(byte[] payload, CancellationToken cancellationToken);
    //           ^^^^^^ 返回数组            ^^^^^^ 接受数组
}
```

**优化方案 A（激进）**:
```csharp
internal interface IRpcHandler
{
    ValueTask<int> HandleAsync(
        ReadOnlySpan<byte> requestPayload,      // ✅ 零拷贝输入
        IBufferWriter<byte> responseWriter,     // ✅ 零拷贝输出
        CancellationToken cancellationToken);
}
```

**优化方案 B（温和，推荐）**:
```csharp
internal interface IRpcHandler
{
    // 保留原签名用于兼容
    Task<byte[]> HandleAsync(byte[] payload, CancellationToken cancellationToken);

    // 新增零拷贝签名
    ValueTask HandleAsync(
        ReadOnlyMemory<byte> requestPayload,
        IBufferWriter<byte> responseWriter,
        CancellationToken cancellationToken);
}
```

**收益**:
- 减少每次 RPC 调用的数组分配
- 减少序列化/反序列化中间缓冲
- 估计性能提升：**+20-40%**（高频 RPC 场景）

**破坏性**: ⚠️ 高（方案 A）/ 低（方案 B）

---

### Priority 2: Redis 序列化优化 🎯

#### 2.1 OptimizedRedisOutboxStore.GetBytes 优化

**文件**: `src/Catga.Persistence.Redis/OptimizedRedisOutboxStore.cs:81`

**问题**:
```csharp
var message = _serializer.Deserialize<OutboxMessage>(
    System.Text.Encoding.UTF8.GetBytes(value));  // ❌ 分配 byte[]
```

**优化方案**:
```csharp
// 使用 stackalloc 或 ArrayPool
Span<byte> buffer = value.Length <= 1024
    ? stackalloc byte[value.Length]  // ✅ 栈分配（小字符串）
    : new byte[value.Length];         // 大字符串仍需堆分配

var bytesWritten = System.Text.Encoding.UTF8.GetBytes(value, buffer);
var message = _serializer.Deserialize<OutboxMessage>(buffer.Slice(0, bytesWritten));
```

**进一步优化（如果 IBufferedMessageSerializer 可用）**:
```csharp
// ✅ 直接从字符串解码到 Span，零拷贝
Span<byte> buffer = stackalloc byte[value.Length * 3]; // UTF-8 最多 3 字节/字符
var bytesWritten = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), buffer);

if (_serializer is IBufferedMessageSerializer bufferedSerializer)
{
    var message = bufferedSerializer.Deserialize<OutboxMessage>(buffer.Slice(0, bytesWritten));
}
```

**收益**:
- 减少 1 次 `byte[]` 分配（每次 Outbox 查询）
- 小字符串（<1KB）完全零分配
- 估计性能提升：**+10-20%**（Outbox 查询）

**破坏性**: 无

---

### Priority 3: 批量操作优化 🔧

#### 3.1 BatchOperationExtensions.Array.Copy 改为 Span.CopyTo

**文件**: `src/Catga/Core/BatchOperationExtensions.cs:71`

**问题**:
```csharp
var finalResults = new TResult[items.Count];
Array.Copy(results, finalResults, items.Count);  // ❌ 传统拷贝
```

**优化方案**:
```csharp
var finalResults = new TResult[items.Count];
results.AsSpan(0, items.Count).CopyTo(finalResults);  // ✅ Span 拷贝（更快）
```

**收益**:
- 轻微性能提升（~5-10%）
- 更现代的 API
- 可能触发 JIT 的 SIMD 优化

**破坏性**: 无

---

### Priority 4: SnowflakeIdGenerator 内部优化 ⚡

#### 4.1 SIMD 批量生成优化（已部分实现）

**文件**: `src/Catga/Core/SnowflakeIdGenerator.cs`

**现状**: ✅ 已使用 SIMD（`Vector256<long>`）在 net7.0+

**进一步优化机会**:
```csharp
// 当前实现
for (int i = 0; i < batchSize; i++)
{
    destination[generated++] = baseId | seq;  // ❌ 逐个赋值
}

// ✅ 使用 Span 批量操作
var destSpan = destination.AsSpan(generated, batchSize);
for (int i = 0; i < destSpan.Length; i++)
{
    destSpan[i] = baseId | (startSequence + i);
}
generated += batchSize;
```

**收益**:
- 更好的局部性（Span 边界检查优化）
- 可能触发更多 JIT 优化
- 估计性能提升：**+5-10%**（边际收益）

**破坏性**: 无

---

### Priority 5: 字符串操作优化（低频路径）💡

#### 5.1 MessageHelper.ValidateMessageId 优化

**文件**: `src/Catga/Common/MessageHelper.cs:29`

**问题**:
```csharp
public static void ValidateMessageId(string? messageId, string paramName = "messageId")
{
    if (string.IsNullOrEmpty(messageId))  // ✅ 已经很高效
        throw new ArgumentException("MessageId is required", paramName);
}
```

**现状**: ✅ 已高效，无需优化

---

#### 5.2 TypeNameCache 优化（已最优）

**文件**: `src/Catga/Core/TypeNameCache.cs`

**现状**: ✅ 已使用静态缓存，零反射（首次后）

**无需优化**: 已经是最优实现

---

## 📈 优化收益汇总

| 优先级 | 优化项 | 预期收益 | 破坏性 | 实施难度 | 推荐 |
|--------|--------|----------|--------|----------|------|
| **P0** | IBufferedMessageSerializer | ✅ 已完成 | 无 | - | ✅ |
| **P1.1** | RpcMessage 使用 ReadOnlyMemory | +15-30% | 中 | 中 | 🔥🔥 |
| **P1.2** | IRpcHandler 零拷贝 | +20-40% | 高/低 | 高 | 🔥🔥 |
| **P2.1** | Redis GetBytes 优化 | +10-20% | 无 | 低 | 🔥 |
| **P3.1** | Array.Copy → Span.CopyTo | +5-10% | 无 | 极低 | ✅ |
| **P4.1** | SnowflakeIdGenerator Span | +5-10% | 无 | 低 | 💡 |
| **P5** | 字符串操作 | 无需优化 | - | - | ❌ |

---

## 🎯 推荐实施顺序

### Phase 1: 低风险快速收益（推荐立即执行）✅
1. **P3.1**: `Array.Copy` → `Span.CopyTo`（5分钟，零破坏）
2. **P2.1**: Redis `GetBytes` 优化（15分钟，零破坏）
3. **P4.1**: SnowflakeIdGenerator Span 优化（10分钟，零破坏）

**总耗时**: ~30分钟
**总收益**: +10-20% 性能（局部路径）
**破坏性**: 无

---

### Phase 2: RPC 层重构（高价值，需仔细设计）🔥
1. **P1.1**: RpcMessage 改为 `ReadOnlyMemory<byte>`（1小时）
2. **P1.2**: IRpcHandler 零拷贝接口（2小时，使用方案 B）
3. **集成测试**: 验证 RPC 调用正常（1小时）
4. **性能测试**: Benchmark 验证收益（30分钟）

**总耗时**: ~4.5小时
**总收益**: +20-40% 性能（RPC 密集场景）
**破坏性**: 低（方案 B）

---

### Phase 3: 文档和迁移指南
1. 更新 API 文档
2. 提供迁移示例
3. 更新 OrderSystem 示例

**总耗时**: ~1小时

---

## ⚠️ 注意事项

### 1. Span<T> 使用限制
```csharp
// ❌ 错误：Span 不能作为字段
public class MyClass
{
    private Span<byte> _buffer;  // 编译错误
}

// ✅ 正确：使用 Memory<T> 或 byte[]
public class MyClass
{
    private Memory<byte> _buffer;  // OK
    private byte[] _buffer;        // OK
}
```

### 2. Span<T> 不能跨 await
```csharp
// ❌ 错误：Span 不能跨 await
public async Task ProcessAsync(Span<byte> data)
{
    await Task.Delay(100);  // 编译错误
}

// ✅ 正确：使用 Memory<T>
public async Task ProcessAsync(Memory<byte> data)
{
    await Task.Delay(100);  // OK
    Process(data.Span);     // 使用时转换为 Span
}
```

### 3. stackalloc 大小限制
```csharp
// ✅ 小缓冲：使用 stackalloc（推荐 <1KB）
Span<byte> smallBuffer = stackalloc byte[512];

// ❌ 大缓冲：避免 stackalloc（可能栈溢出）
Span<byte> largeBuffer = stackalloc byte[100_000];  // 危险！

// ✅ 大缓冲：使用 ArrayPool
using var rented = ArrayPoolHelper.RentOrAllocate<byte>(100_000);
var largeBuffer = rented.AsSpan();
```

---

## 🎉 预期最终效果

### 整体性能提升
- **RPC 密集场景**: +20-40% ⬆️
- **Redis 持久化**: +10-20% ⬆️
- **批量操作**: +5-10% ⬆️
- **ID 生成**: +5-10% ⬆️

### 内存优化
- **RPC 层**: 减少 50-70% 分配
- **Redis 层**: 减少 30-50% 分配
- **批量操作**: 减少 10-20% 拷贝

### 整体收益
- **高吞吐场景**: +15-30% 性能 ⬆️
- **低延迟场景**: -20-40% P99 延迟 ⬇️
- **内存效率**: -30-50% GC 压力 ⬇️

---

## 📝 实施决策

请选择：
- **A**: 立即执行 Phase 1（低风险快速收益）✅
- **B**: 执行 Phase 1 + Phase 2（完整优化）🔥
- **C**: 仅执行最高价值的 P1.2（RPC 零拷贝）⚡
- **D**: 制定更详细的计划，先验证收益 📊

---

## 🔗 相关文档
- [ARRAYPOOL-OPTIMIZATION-PLAN.md](./ARRAYPOOL-OPTIMIZATION-PLAN.md) - ArrayPool 优化（已完成）
- [SIMD-OPTIMIZATION-PLAN.md](./SIMD-OPTIMIZATION-PLAN.md) - SIMD 加速计划
- [MULTI-TARGETING-COMPLETE.md](./MULTI-TARGETING-COMPLETE.md) - 多目标框架支持

🎯 **Span<T> + ArrayPool + SIMD = 极致性能！**

