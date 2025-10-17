# 🚀 SIMD 加速优化计划

## 📊 当前状态分析

### ✅ 已实现 SIMD
- **SnowflakeIdGenerator**: AVX2 批量 ID 生成（2-3x 性能提升）

### 🎯 可优化的关键路径

通过代码分析，发现以下热路径可以应用 SIMD 加速：

---

## 📋 优化计划

### Phase 1: 批量事件处理 SIMD 优化 ⚡ (高收益)

#### 位置：`CatgaMediator.PublishAsync` (行 202-217)

**当前实现**:
```csharp
// 串行创建 Task 数组
for (int i = 0; i < handlerList.Count; i++)
    tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);
```

**问题**:
- 串行循环创建 Task，无法利用 CPU 并行性
- 每个 handler 单独处理，缓存局部性差

**SIMD 优化方案**:
```csharp
// 使用 SIMD 批量初始化 Task 状态（预热）
// 然后并行启动所有 handlers
```

**预期收益**: 
- ✅ 10-20% 提升（大批量事件 >10 handlers）
- ✅ 改善 CPU 缓存局部性

---

### Phase 2: 批量命令处理 SIMD 优化 ⚡⚡ (中高收益)

#### 位置：`BatchOperationExtensions.ExecuteBatchWithResultsAsync` (行 54-59)

**当前实现**:
```csharp
// 串行创建 ValueTask
for (int i = 0; i < items.Count; i++)
    tasks[i] = action(items[i]);

// 串行等待结果
for (int i = 0; i < items.Count; i++)
    results[i] = await tasks[i].ConfigureAwait(false);
```

**问题**:
- 两次串行循环，无 SIMD 优化
- 内存访问模式非连续

**SIMD 优化方案**:
```csharp
#if NET7_0_OR_GREATER
// 使用 Vector256 批量处理状态检查
// 快速定位已完成的任务（位掩码）
if (Vector256.IsHardwareAccelerated && items.Count >= 4)
{
    // SIMD 批量检查 Task.IsCompleted
    // 减少轮询开销
}
#endif
```

**预期收益**:
- ✅ 15-30% 提升（批量 >16 items）
- ✅ 减少异步状态机开销

---

### Phase 3: 序列化器 SIMD 优化 ⚡⚡⚡ (最高收益)

#### 位置：`MemoryPackMessageSerializer` & 未来的自定义序列化器

**当前实现**:
```csharp
// 直接调用 MemoryPackSerializer（内部可能已有 SIMD）
public byte[] Serialize<T>(T value)
    => MemoryPackSerializer.Serialize(value);
```

**SIMD 优化方案**:

#### 3.1 字符串批量编码/解码
```csharp
// UTF-8 编码：使用 Vector256 批量转换
public static class SimdUtf8Encoder
{
#if NET7_0_OR_GREATER
    public static int EncodeUtf8Simd(ReadOnlySpan<char> source, Span<byte> destination)
    {
        if (Avx2.IsSupported && source.Length >= 16)
        {
            // 使用 AVX2 批量转换 ASCII 字符（最常见情况）
            // 处理速度 ~4x 提升
        }
        return Encoding.UTF8.GetBytes(source, destination);
    }
#endif
}
```

#### 3.2 校验和计算（CRC32/Hash）
```csharp
public static class SimdChecksum
{
#if NET7_0_OR_GREATER
    public static uint Crc32Simd(ReadOnlySpan<byte> data)
    {
        if (Sse42.IsSupported) // Intel CRC32 指令
        {
            // 硬件加速 CRC32
            // 处理速度 ~8-10x 提升
        }
        return SlowCrc32(data);
    }
#endif
}
```

#### 3.3 内存比较/拷贝加速
```csharp
public static class SimdMemoryOps
{
#if NET7_0_OR_GREATER
    public static bool EqualsSimd(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (Avx2.IsSupported && left.Length >= 32)
        {
            // 使用 Vector256 批量比较
            // 处理速度 ~4-8x 提升
        }
        return left.SequenceEqual(right);
    }
#endif
}
```

**预期收益**:
- ✅ 2-8x 提升（序列化/反序列化热路径）
- ✅ 极大降低 CPU 消耗

---

### Phase 4: 消息过滤/路由 SIMD 优化 ⚡ (中收益)

#### 位置：未来的消息路由逻辑

**SIMD 优化方案**:
```csharp
public static class SimdMessageRouter
{
#if NET7_0_OR_GREATER
    // 批量匹配消息类型（字符串比较）
    public static int FindHandlerIndexSimd(ReadOnlySpan<int> typeHashCodes, int targetHash)
    {
        if (Avx2.IsSupported && typeHashCodes.Length >= 8)
        {
            var targetVector = Vector256.Create(targetHash);
            // 一次比较 8 个 hash
            // 找到匹配索引
        }
        return LinearSearch(typeHashCodes, targetHash);
    }
#endif
}
```

**预期收益**:
- ✅ 20-40% 提升（大量 handlers 场景）

---

## 📦 实现优先级

### P0 - 立即实现（最高 ROI）
1. ✅ **Phase 3.2**: CRC32 校验和（极高收益，实现简单）
2. ✅ **Phase 3.3**: 内存比较加速（极高收益，实现简单）

### P1 - 短期实现（高 ROI）
3. ✅ **Phase 3.1**: UTF-8 编码加速（高收益，中等复杂度）
4. ✅ **Phase 2**: 批量命令处理（高收益，中等复杂度）

### P2 - 中期实现（中 ROI）
5. ⏭️ **Phase 1**: 批量事件处理（中收益，低复杂度）
6. ⏭️ **Phase 4**: 消息路由优化（中收益，中等复杂度）

---

## 🎯 实现细节

### 新增文件结构
```
src/Catga/
├── Performance/
│   ├── Simd/
│   │   ├── SimdMemoryOps.cs       // Phase 3.3 内存操作
│   │   ├── SimdChecksum.cs        // Phase 3.2 校验和
│   │   ├── SimdUtf8Encoder.cs     // Phase 3.1 UTF-8 编码
│   │   ├── SimdBatchProcessor.cs  // Phase 1,2 批处理
│   │   └── SimdMessageRouter.cs   // Phase 4 路由
```

### API 设计原则
1. **条件编译**: 使用 `#if NET7_0_OR_GREATER`
2. **硬件检测**: 运行时检查 `Avx2.IsSupported` / `Sse42.IsSupported`
3. **自动回退**: SIMD 不可用时回退到标准实现
4. **零分配**: 使用 `Span<T>` / `ReadOnlySpan<T>`
5. **内联**: 标记 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`

### Benchmark 计划
每个优化都需要对应的 Benchmark：
```csharp
[MemoryDiagnoser]
[HardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.BranchMispredictions)]
public class SimdMemoryOpsBenchmarks
{
    [Benchmark(Baseline = true)]
    public bool SequenceEqual_Standard() { ... }

    [Benchmark]
    public bool SequenceEqual_Simd() { ... }
}
```

---

## 📊 预期性能提升

### 综合性能提升（估算）
- **高吞吐场景**（批量操作 >100）: **30-50%** ⬆️
- **序列化密集场景**: **2-4x** ⬆️
- **低延迟场景**（单个请求）: **5-10%** ⬆️

### 目标平台
- ✅ **net9.0 / net8.0 / net7.0**: 完整 SIMD 支持
- ⚠️ **net6.0**: 自动回退到标准实现（无性能损失）

---

## ⚠️ 注意事项

### 1. 硬件兼容性
```csharp
// 始终检查硬件支持
if (Avx2.IsSupported) { ... }
else if (Sse2.IsSupported) { ... }
else { /* fallback */ }
```

### 2. 对齐要求
```csharp
// Vector256 需要 32 字节对齐（最优性能）
// 使用 ArrayPool 时确保对齐
```

### 3. 小数据集
```csharp
// 小数据集（<16 items）不使用 SIMD
// 避免 SIMD 启动开销
if (data.Length < 16) return StandardImpl(data);
```

### 4. AOT 兼容性
```csharp
// 所有 SIMD 代码都是 AOT 兼容的
// 使用静态类型和编译时条件
```

---

## ✅ 验证标准

每个优化必须通过：
1. ✅ **性能提升**: 至少 10% 提升（BenchmarkDotNet 验证）
2. ✅ **功能正确**: 所有单元测试通过
3. ✅ **兼容性**: net6.0 回退正常工作
4. ✅ **AOT**: 无 AOT 警告
5. ✅ **零分配**: MemoryDiagnoser 验证无额外分配

---

## 🚀 执行步骤

1. **Phase 0**: 创建 SIMD 基础设施和测试框架
2. **Phase P0**: 实现 CRC32 + 内存比较（立即收益）
3. **Phase P1**: 实现 UTF-8 + 批量命令（高收益）
4. **Benchmark**: 运行完整性能测试
5. **Phase P2**: 根据 Benchmark 结果决定是否实现剩余优化
6. **Documentation**: 更新性能文档

---

## 📝 总结

SIMD 优化将为 Catga 在高吞吐和序列化密集场景带来 **2-4倍** 的性能提升，同时保持：
- ✅ 100% AOT 兼容
- ✅ 100% 向后兼容（net6.0 自动回退）
- ✅ 零额外分配
- ✅ 生产级稳定性

**建议**: 先实现 P0（CRC32 + 内存比较），验证收益后再决定是否继续 P1/P2。

