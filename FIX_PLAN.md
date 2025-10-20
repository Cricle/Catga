# 编译错误和警告修复计划

## 🚨 当前状态: 发现更多删除后遗症

删除功能后，测试和示例中还有大量引用。需要系统性修复。

## 📊 问题统计 (更新)

### 编译错误 (~60 个)

| # | 文件 | 错误类型 | 说明 |
|---|------|----------|------|
| 1 | `InMemoryMessageTransport.cs:129` | CS0411 | 无法推断 `ExecuteBatchAsync` 泛型类型参数 |
| 2 | `RedisMessageTransport.cs:120` | CS0411 | 无法推断 `ExecuteBatchAsync` 泛型类型参数 |
| 3 | `RedisMessageTransport.cs:133` | CS1501 | `ExecuteBatchAsync` 没有 4 参数重载 |
| 4 | `NatsMessageTransport.cs:135` | CS0411 | 无法推断 `ExecuteBatchAsync` 泛型类型参数 |

**根本原因**: 之前删除 LINQ 时，影响了 `BatchOperationHelper.ExecuteBatchAsync` 的重载解析。

### 警告 (10 个，重复计数)

| # | 文件 | 警告类型 | 说明 |
|---|------|----------|------|
| 1-4 | `JsonMessageSerializer.cs:45,55` | IL2026, IL3050 | AOT 不兼容 - `JsonSerializer.Serialize/Deserialize` |
| 5-6 | `NatsJSOutboxStore.cs:99,163` | CA2264 | `ArgumentNullException.ThrowIfNull` 传递不可为 null 的值 |
| 7 | `NatsKVEventStore.cs:215` | IL3050 | AOT 不兼容 - `MakeGenericMethod` 反射 |

---

## 🔧 修复计划

### Phase 1: 修复编译错误 (高优先级)

#### 1.1 修复 `BatchOperationHelper.ExecuteBatchAsync` 调用

**问题**: 泛型类型推断失败

**文件**:
- `src/Catga.Transport.InMemory/InMemoryMessageTransport.cs:129`
- `src/Catga.Transport.Redis/RedisMessageTransport.cs:120, 133`
- `src/Catga.Transport.Nats/NatsMessageTransport.cs:135`

**解决方案**:
1. 检查 `BatchOperationHelper.ExecuteBatchAsync` 签名
2. 显式指定泛型类型参数
3. 或简化为直接 `foreach` 循环（避免泛型推断问题）

**预计影响**: 4 个文件修改

---

### Phase 2: 修复 AOT 警告 (中优先级)

#### 2.1 修复 `JsonMessageSerializer` AOT 警告

**问题**: 使用了运行时反射的 `JsonSerializer.Serialize/Deserialize`

**文件**: `src/Catga.Serialization.Json/JsonMessageSerializer.cs:45, 55`

**解决方案**:
1. 添加 `[RequiresUnreferencedCode]` 特性到方法
2. 添加 `[RequiresDynamicCode]` 特性到方法
3. 或使用 `JsonTypeInfo` 参数重载（需要 Source Generator）

**当前状态**: 这是已知限制，`JsonMessageSerializer` 本身就不是 AOT 友好的
**建议**: 添加 suppression 特性，文档说明使用 `MemoryPackMessageSerializer` 支持 AOT

**预计影响**: 1 个文件修改

#### 2.2 修复 `NatsKVEventStore` AOT 警告

**问题**: 使用了 `MakeGenericMethod` 反射

**文件**: `src/Catga.Persistence.Nats/NatsKVEventStore.cs:215`

**解决方案**:
1. 检查代码逻辑
2. 如果可以，改为静态类型调用
3. 否则添加 `[RequiresDynamicCode]` 特性

**预计影响**: 1 个文件修改

---

### Phase 3: 修复代码分析警告 (低优先级)

#### 3.1 修复 `CA2264` - ArgumentNullException.ThrowIfNull

**问题**: 传递不可为 null 的值给 `ThrowIfNull`

**文件**:
- `src/Catga.Persistence.Nats/Stores/NatsJSOutboxStore.cs:99, 163`

**解决方案**:
1. 检查参数是否真的可为 null
2. 如果不可为 null，删除 `ThrowIfNull` 调用
3. 或改为 `Debug.Assert`

**预计影响**: 1 个文件修改

---

## 📋 执行顺序

```
1. Phase 1: 修复编译错误 (必须)
   ├─ 1.1 修复 BatchOperationHelper 调用 (4 个文件)
   └─ ✅ 编译成功

2. Phase 2: 修复 AOT 警告 (推荐)
   ├─ 2.1 JsonMessageSerializer (添加 suppression)
   ├─ 2.2 NatsKVEventStore (检查反射使用)
   └─ ✅ 减少警告

3. Phase 3: 修复代码分析警告 (可选)
   └─ 3.1 CA2264 (删除多余的 ThrowIfNull)
```

---

## 🎯 预期结果

| 指标 | Before | After | 目标 |
|------|--------|-------|------|
| **编译错误** | 5 个 | 0 个 | ✅ 100% |
| **AOT 警告** | 6 个 | 0-2 个 | ✅ 减少 67% |
| **分析警告** | 2 个 | 0 个 | ✅ 100% |

---

## 🚀 开始执行

执行顺序: Phase 1 → Phase 2 → Phase 3

**优先级**: Phase 1 必须完成，Phase 2/3 根据时间决定

