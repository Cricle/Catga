# Catga 下一步行动计划

**日期**: 2025-10-21  
**当前状态**: ✅ 测试已修复，0错误，0警告

---

## ✅ 已完成

### 1. 删除错误的单元测试
- ✅ 删除 `CatgaMediatorTests.cs` (多个 Handler 违反 CQRS)
- ✅ 重写测试，符合 CQRS 原则
- ✅ 编译成功：0 错误，0 警告

---

## 🎯 接下来需要做（用户要求）

### 2. 审查 AOT 警告的真实问题
**问题**: 用户说"aot warn是有问题的，真的没问题才屏蔽"

**当前状况**:
- `src/Catga/Serialization.cs` 有大量 IL2026/IL3050/IL2111 警告
- 这些来自**非泛型序列化方法内部使用反射调用泛型方法**
- 例如: `MakeGenericMethod`, `MethodInfo.Invoke`

**根本问题**:
```csharp
// MessageSerializerBase.cs 的非泛型方法实现
public virtual byte[] Serialize(object value, Type type)
{
    // ❌ 这里用反射调用泛型方法，不是真正的AOT友好！
    var method = typeof(MessageSerializerBase).GetMethod(nameof(Serialize), 1, new[] { type })!;
    var genericMethod = method.MakeGenericMethod(type);  // IL3050 警告
    return (byte[])genericMethod.Invoke(this, new[] { value })!;  // IL2111 警告
}
```

**这确实有问题！** 我们**声称**是非泛型，但**实际**还是用反射。

### 3. 修复 AOT 问题或合理屏蔽
**方案 A**: 真正的非泛型实现（推荐）
```csharp
// 让子类直接实现非泛型方法
public abstract byte[] Serialize(object value, Type type);
public abstract object? Deserialize(byte[] data, Type type);
```

**方案 B**: 标记 RequiresDynamicCode（诚实）
```csharp
[RequiresDynamicCode("Uses reflection to call generic methods")]
public virtual byte[] Serialize(object value, Type type)
{
    // 保持当前实现，但诚实标记
}
```

### 4. 减少关键路径 GC 压力
**用户说**: "关键路径gc还是很大"

需要审查:
1. `CatgaMediator.SendAsync` - 命令处理路径
2. `CatgaMediator.PublishAsync` - 事件发布路径  
3. `InMemoryMessageTransport.PublishAsync` - 传输路径
4. `BatchOperationHelper` - 批处理路径

**常见GC来源**:
- ❌ `List<T>` 动态扩容
- ❌ Lambda 闭包
- ❌ `Task` 分配
- ❌ 装箱/拆箱
- ❌ 字符串拼接

---

## 🔍 行动步骤

### 步骤 1: 修复非泛型序列化的 AOT 问题
1. 检查 `JsonMessageSerializer` 和 `MemoryPackMessageSerializer`
2. 决定是否可以真正实现非泛型方法（不用反射）
3. 如果不能，诚实添加 `RequiresDynamicCode` 属性

### 步骤 2: 审查关键路径 GC
1. 使用 BenchmarkDotNet 的 `[MemoryDiagnoser]` 查看分配
2. 识别 GC 热点
3. 优化:
   - 预分配集合
   - 使用 `ValueTask` 代替 `Task`
   - 避免闭包
   - 使用对象池

### 步骤 3: 验证优化效果
1. 运行 benchmark 对比优化前后
2. 确保功能正确性
3. 更新文档

---

## 📊 当前警告统计

```
总 IL 警告: ~200 (跨多个框架)
- IL2026 (RequiresUnreferencedCode): 来自泛型序列化调用
- IL3050 (RequiresDynamicCode): 来自 MakeGenericMethod
- IL2111 (DynamicallyAccessedMembers): 来自反射调用

主要来源:
- src/Catga/Serialization.cs (非泛型序列化实现)
- src/Catga/Core/SerializationExtensions.cs
- src/Catga.Persistence.Nats/Stores/* (合理的泛型调用)
```

---

## 💡 建议优先级

### 🔴 高优先级 (立即执行)
1. **修复非泛型序列化的反射使用**
   - 选择方案 A 或 B
   - 这是真正的 AOT 兼容性问题

### 🟡 中优先级 (今天完成)
2. **审查关键路径 GC 压力**
   - 运行 memory profiler
   - 识别热点

### 🟢 低优先级 (后续优化)
3. **持续优化**
   - 增加更多单元测试
   - 性能基准测试

---

**建议**: 先修复非泛型序列化的 AOT 问题，然后审查 GC 压力。

