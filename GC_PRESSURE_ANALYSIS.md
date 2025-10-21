# Catga 关键路径 GC 压力分析

**日期**: 2025-10-21  
**分析范围**: 命令/查询/事件处理的热路径

---

## 🔴 严重 GC 问题（每次调用都分配）

### 1. **Diagnostics 指标分配** (最严重)

**位置**: `CatgaMediator.cs` 多处  
**问题**: 每次记录指标都创建新的 `KeyValuePair`

```csharp
❌ Line 104, 113, 155, 239:
CatgaDiagnostics.CommandsExecuted.Add(1, 
    new("request_type", reqType),     // ❌ 堆分配
    new("success", "false"));           // ❌ 堆分配

CatgaDiagnostics.EventsPublished.Add(1, 
    new("event_type", eventType),       // ❌ 堆分配
    new("handler_count", handlerList.Count.ToString()));  // ❌ 装箱 + 字符串分配
```

**影响**: 每个命令/事件都分配 2-4 个 `KeyValuePair<string, object?>`  
**频率**: **非常高** - 每次调用  
**估计分配**: ~100-200 bytes per call

**修复方案**:
```csharp
✅ 使用 TagList（栈分配）
var tags = new TagList
{
    { "request_type", reqType },
    { "success", "false" }
};
CatgaDiagnostics.CommandsExecuted.Add(1, tags);
```

---

### 2. **字符串分配**

#### a) 字符串插值
**位置**: `CatgaMediator.cs`

```csharp
❌ Line 60, 191:
$"Command: {reqType}"    // ❌ 每次都分配新字符串
$"Event: {eventType}"    // ❌ 每次都分配新字符串

❌ Line 105, 179:
$"No handler for {reqType}"  // ❌ 错误消息字符串分配
```

**修复方案**:
```csharp
✅ 使用 string.Concat 或预计算常量
activity.SetTag("catga.operation", "Command");
activity.SetTag("catga.type", reqType);
```

#### b) ToString() 装箱

```csharp
❌ Line 77, 209 (CatgaMediator.cs):
message.CorrelationId.Value.ToString()  // ❌ long 装箱 + ToString

❌ Line 239:
handlerList.Count.ToString()  // ❌ int 装箱 + ToString

❌ Line 58 (InMemoryMessageTransport.cs):
qos.ToString()  // ❌ enum 装箱 + ToString
```

**修复方案**:
```csharp
✅ 使用 Span 格式化（.NET 6+）
Span<char> buffer = stackalloc char[20];
correlationId.TryFormat(buffer, out int written);
```

---

### 3. **ServiceProvider.CreateScope()**

**位置**: `CatgaMediator.cs`

```csharp
❌ Line 92, 98, 176, 219:
using var scope = _serviceProvider.CreateScope();  // ❌ 每次分配新 Scope
```

**影响**: 每次调用分配 `IServiceScope` 实例  
**频率**: **每个命令/事件**  
**估计分配**: ~200-500 bytes per call

**修复方案**:
```csharp
✅ 对于无状态 Handler，优先使用 Singleton
✅ 缓存 Scoped ServiceProvider（如果安全）
✅ 使用对象池复用 Scope（如果可行）
```

---

### 4. **Lambda 闭包分配**

**位置**: `CatgaMediator.cs` Line 254

```csharp
❌ Line 254:
await BatchOperationHelper.ExecuteConcurrentBatchAsync(
    handlerList,
    handler => HandleEventSafelyAsync(handler, @event, cancellationToken),  // ❌ 闭包
    _eventConcurrencyLimiter.MaxConcurrency,
    cancellationToken);
```

**问题**: Lambda 捕获 `@event` 和 `cancellationToken`，创建闭包对象

**修复方案**:
```csharp
✅ 使用静态方法 + 参数传递
✅ 或使用 ValueTuple 传递状态
```

---

### 5. **Task 数组分配**

**位置**: `CatgaMediator.cs` Line 261

```csharp
❌ Line 261:
var tasks = new Task[handlerList.Count];  // ❌ 数组分配
for (var i = 0; i < handlerList.Count; i++)
    tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);
```

**影响**: 每次事件发布分配新数组  
**频率**: 每个有多 Handler 的事件  
**估计分配**: 8 * handler_count bytes

**修复方案**:
```csharp
✅ 使用 ArrayPool<Task>
using var pooledArray = MemoryPoolManager.RentArray<Task>(handlerList.Count);
var tasks = pooledArray.Span;
// ... use tasks
```

---

### 6. **TransportContext 分配**

**位置**: `InMemoryMessageTransport.cs` Line 52

```csharp
❌ Line 52:
var ctx = context ?? new TransportContext { 
    MessageId = MessageExtensions.NewMessageId(), 
    MessageType = TypeNameCache<TMessage>.FullName, 
    SentAt = DateTime.UtcNow 
};
```

**影响**: 当 context == null 时，每次分配新对象  
**频率**: **非常高** - 大部分调用  
**估计分配**: ~100-150 bytes

**修复方案**:
```csharp
✅ 使用对象池
private static readonly ObjectPool<TransportContext> _contextPool = ...;

✅ 或使用 struct (需要修改接口)
public readonly struct TransportContext { ... }
```

---

## 🟡 中等 GC 问题

### 7. **Activity 创建**

```csharp
❌ Line 45 (InMemoryTransport), 58-62 (CatgaMediator):
using var activity = CatgaDiagnostics.ActivitySource.StartActivity(...);
```

**注意**: 这个是 OpenTelemetry 的开销，已经有 `HasListeners()` 优化，可接受。

---

## 📊 GC 压力估算

### 热路径分配总结（单次命令处理）

| 分配类型 | 次数 | 单次大小 | 总大小 |
|---------|------|----------|--------|
| KeyValuePair (指标) | 2-6 | ~32B | ~64-192B |
| 字符串插值 | 2-4 | ~50-100B | ~100-400B |
| ToString() | 1-3 | ~20-50B | ~20-150B |
| ServiceScope | 1-2 | ~200-500B | ~200-1000B |
| TransportContext | 0-1 | ~100-150B | ~0-150B |
| Lambda 闭包 | 0-n | ~40B | ~0-400B |
| Task 数组 | 0-1 | 8n | ~0-800B |
| **总计** | - | - | **~384-3092B** |

### 典型场景

**命令处理** (单 Handler):
- 最小: ~400B
- 典型: ~1KB
- 最大: ~2KB

**事件发布** (3 Handlers):
- 最小: ~600B
- 典型: ~1.5KB
- 最大: ~3KB

### Gen0 压力

假设吞吐量 = **10K ops/s**:
- **每秒分配**: 10MB - 30MB
- **Gen0 GC 频率**: 每 1-3 秒一次（假设 16MB Eden）
- **Gen0 GC 暂停**: ~1-5ms per GC
- **总 GC 开销**: ~0.3-1.5% CPU

---

## ✅ 优化优先级

### 🔴 高优先级（立即执行）

1. **修复 Diagnostics 指标分配**
   - 使用 `TagList`（栈分配）
   - 预期减少: **50-60%** 分配

2. **优化字符串分配**
   - 避免字符串插值
   - 使用 `Span<char>` 格式化
   - 预期减少: **20-30%** 分配

3. **缓存/池化 TransportContext**
   - 使用对象池或 struct
   - 预期减少: **10-15%** 分配

### 🟡 中优先级

4. **优化 Lambda 闭包**
   - 使用静态方法
   - 预期减少: **5-10%** 分配

5. **池化 Task 数组**
   - 使用 `ArrayPool<Task>`
   - 预期减少: **5-10%** 分配

### 🟢 低优先级（架构改进）

6. **Scope 管理优化**
   - 需要架构调整
   - 可能减少: **20-30%** 分配

---

## 🎯 建议下一步

1. **立即修复**: Diagnostics 指标分配（使用 `TagList`）
2. **立即修复**: 字符串分配（避免插值，使用 Span）
3. **快速修复**: TransportContext 池化
4. **后续优化**: Lambda 和 Task 数组

**预期总体效果**:
- GC 压力减少: **60-80%**
- Gen0 GC 频率降低: **50-70%**
- 热路径延迟降低: **5-15%**

---

**最后更新**: 2025-10-21  
**分析工具**: 代码审查 + 估算
**建议**: 使用 BenchmarkDotNet `[MemoryDiagnoser]` 验证实际效果

