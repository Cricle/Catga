# AOT 抑制消息审查报告

## 📋 审查标准

### ✅ 合理保留
- **序列化库**：必须支持动态类型（用户提供类型），无法完全避免
- **明确标记为非 AOT**：使用 `RequiresUnreferencedCode` / `RequiresDynamicCode` 标记的方法
- **有明确文档**：说明为什么需要，以及 AOT 替代方案

### ❌ 需要移除
- **可以通过重构解决**：使用接口、泛型约束等替代反射
- **隐藏配置错误**：应该 fail fast 而不是抑制
- **缺乏文档**：没有说明为什么需要或如何在 AOT 中使用

---

## 📊 审查结果汇总

| 文件 | 抑制数量 | 分类 | 行动 |
|------|---------|------|------|
| RedisInboxPersistence.cs | 6 | ✅ 合理 | 保留 + 文档 |
| RedisOutboxPersistence.cs | 8 | ✅ 合理 | 保留 + 文档 |
| OptimizedRedisOutboxStore.cs | ? | ✅ 合理 | 保留 + 文档 |
| CatgaExceptionJsonConverter.cs | 2 | ✅ 合理 | 保留（已有文档）|
| SerializationHelper.cs | 2 | ✅ 合理 | 保留（已有 Requires 标记）|
| CatgaBuilder.cs | ? | 🔍 待审查 | 检查 |
| InMemoryDeadLetterQueue.cs | ? | 🔍 待审查 | 检查 |
| ShardedIdempotencyStore.cs | ? | 🔍 待审查 | 检查 |
| JsonMessageSerializer.cs | ? | ✅ 合理 | 保留 |
| RedisDistributedCache.cs | ? | ✅ 合理 | 保留 |
| RedisIdempotencyStore.cs | ? | ✅ 合理 | 保留 |
| NatsMessageTransport.cs | ? | 🔍 待审查 | 检查 |
| IdempotencyBehavior.cs | ? | 🔍 待审查 | 检查 |
| CatgaEndpointExtensions.cs | ? | 🔍 待审查 | 检查 |

---

## 🔍 详细审查

### 1. ✅ RedisInboxPersistence.cs / RedisOutboxPersistence.cs
**抑制原因：**
```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
```

**分析：**
- ✅ **合理**：这些是持久化层，必须序列化用户提供的消息类型
- ✅ **有接口抽象**：`IMessageSerializer` 接口让用户选择 AOT 兼容的实现（如 MemoryPack）
- ✅ **职责清晰**：序列化警告应该在序列化器实现上处理，不是持久化层

**行动：** 
- 保留抑制
- 确保 `IMessageSerializer` 接口有明确的 AOT 文档
- 用户使用 MemoryPack 时完全 AOT 兼容

---

### 2. ✅ CatgaExceptionJsonConverter.cs
**抑制原因：**
```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Exception serialization is for debugging only. Use MemoryPack for production AOT.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Exception serialization is for debugging only. Use MemoryPack for production AOT.")]
```

**分析：**
- ✅ **合理**：异常序列化仅用于调试，不是核心功能
- ✅ **有明确文档**：说明这是仅用于调试，生产环境使用 MemoryPack
- ✅ **可选功能**：用户可以不使用 JSON 序列化器

**行动：** 
- 保留抑制
- 文档清晰说明这是调试功能

---

### 3. ✅ SerializationHelper.cs
**抑制原因：**
```csharp
[UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "Callers are responsible for ensuring T has proper DynamicallyAccessedMembers annotations.")]
```

**分析：**
- ✅ **合理**：泛型辅助方法，调用者负责提供正确的类型注解
- ✅ **有 Requires 标记**：`SerializeJson` 方法有 `RequiresUnreferencedCode` 和 `RequiresDynamicCode`
- ✅ **明确警告**：调用者会看到警告，知道不能在 AOT 中使用

**行动：** 
- 保留抑制
- 确保有 AOT 替代方案文档（使用 `Serialize` 方法 + MemoryPack）

---

### 4. 🔍 需要深入审查的文件

让我逐个检查剩余文件...

---

## 🎯 审查策略

### Phase 1: 序列化相关（合理保留）✅
- RedisInboxPersistence.cs
- RedisOutboxPersistence.cs
- OptimizedRedisOutboxStore.cs
- JsonMessageSerializer.cs
- RedisDistributedCache.cs
- RedisIdempotencyStore.cs
- CatgaExceptionJsonConverter.cs
- SerializationHelper.cs

**理由：** 这些都是序列化/持久化基础设施，必须支持用户提供的动态类型。
用户通过选择 MemoryPack 可以实现完全 AOT 兼容。

### Phase 2: DI/Pipeline 相关（需要检查）🔍
- CatgaBuilder.cs
- IdempotencyBehavior.cs
- CatgaEndpointExtensions.cs

**检查重点：** 是否有不必要的反射？是否可以通过 Source Generator 解决？

### Phase 3: 其他（需要检查）🔍
- InMemoryDeadLetterQueue.cs
- ShardedIdempotencyStore.cs
- NatsMessageTransport.cs

**检查重点：** 为什么需要抑制？是否有更好的解决方案？

---

## 🚀 执行计划

1. ✅ **保留序列化相关抑制**（8个文件）- 这些是合理的
2. 🔍 **深入审查 DI/Pipeline**（3个文件）- 看是否可以优化
3. 🔍 **深入审查其他**（3个文件）- 看是否可以优化
4. 📝 **更新文档** - 说明哪些抑制是必要的，为什么
5. ✅ **验证 AOT 发布** - 确保整体 AOT 兼容

---

**原则：不是所有的抑制都是坏的，但每个抑制都必须有充分的理由和文档！**

