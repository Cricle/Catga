# Catga 全局代码优化计划

## 🎯 优化目标

**功能不变**的前提下，优化所有库：
1. ✅ **减少代码量** - 消除冗余、简化逻辑、合并重复
2. ✅ **性能优化** - 减少分配、提升效率、优化算法
3. ✅ **代码质量** - 提高可读性、降低复杂度

## 📊 当前项目统计

### 核心库代码量（src/）

| 项目 | 文件数 | 总行数 | 优先级 |
|------|--------|--------|--------|
| **Catga** (核心) | 46 | 3,178 | 🔴 **高** |
| **Catga.InMemory** | 34 | 2,267 | 🔴 **高** |
| **Catga.Debugger** | 13 | 1,470 | 🟡 **中** |
| **Catga.Persistence.Redis** | 16 | 1,323 | 🟡 **中** |
| **Catga.SourceGenerator** | 10 | 1,215 | 🟢 **低** |
| **Catga.Debugger.AspNetCore** | 4 | 539 | 🟢 **低** |
| **Catga.Transport.Nats** | 5 | 448 | 🟢 **低** |
| **Catga.AspNetCore** | 7 | 262 | 🟢 **低** |
| **Catga.Serialization.Json** | 2 | 160 | 🟢 **低** |
| **Catga.Serialization.MemoryPack** | 2 | 59 | 🟢 **低** |
| **Catga.Distributed** | 1 | 30 | 🟢 **低** |
| **总计** | **140** | **10,951** | - |

### 代码量最多的文件（Top 10）

| 文件 | 行数 | 优化潜力 |
|------|------|---------|
| SnowflakeIdGenerator.cs | 377 | 🟡 中 |
| CatgaHandlerGenerator.cs | 270 | 🟢 低（生成器） |
| InMemoryEventStore.cs | 265 | 🔴 高 |
| ReplayableEventCapturer.cs | 263 | 🔴 高 |
| OrderCommandHandlers.cs | 256 | 🔴 高 |
| ServiceRegistrationGenerator.cs | 224 | 🟢 低（生成器） |
| IReplayEngine.cs | 221 | 🟡 中 |
| DebuggerEndpoints.cs | 208 | 🔴 高 |
| NatsEventStore.cs | 202 | 🟡 中 |
| RedisOutboxPersistence.cs | 200 | 🟡 中 |

## 🚀 优化策略

### Phase 1: 核心库优化（Catga）

**目标**：3,178 lines → ~2,500 lines (-21%)

#### 1.1 SnowflakeIdGenerator.cs (377 lines)
**优化点**：
- ❌ 过多的注释和文档（~100 lines）
- ❌ 可提取的常量和辅助方法
- ❌ 重复的位运算逻辑

**预期**：377 → **250 lines** (-34%)

#### 1.2 EventStoreRepository.cs (200 lines)
**优化点**：
- ❌ Task → ValueTask
- ❌ 简化 LINQ 查询
- ❌ 合并重复的错误处理

**预期**：200 → **150 lines** (-25%)

#### 1.3 其他核心文件
- SafeRequestHandler.cs: 优化日志和错误处理
- CatgaResult.cs: 简化扩展方法
- ResultMetadata.cs: 优化字典操作

**预期总计**：3,178 → **~2,500 lines** (-21%)

---

### Phase 2: InMemory 传输层优化（Catga.InMemory）

**目标**：2,267 lines → ~1,800 lines (-20%)

#### 2.1 CatgaMediator.cs (170 lines)
**优化点**：
- ❌ 简化异步逻辑
- ❌ 减少临时集合分配
- ❌ 优化错误处理

**预期**：170 → **120 lines** (-29%)

#### 2.2 InMemory Transport & Store
**优化点**：
- ❌ 合并重复的 ConcurrentDictionary 操作
- ❌ ValueTask 替代 Task
- ❌ 减少不必要的 LINQ

**预期总计**：2,267 → **~1,800 lines** (-20%)

---

### Phase 3: Debugger 优化（Catga.Debugger）

**目标**：1,470 lines → ~1,100 lines (-25%)

#### 3.1 InMemoryEventStore.cs (265 lines)
**优化点**：
- ❌ 简化事件存储逻辑
- ❌ 优化查询性能
- ❌ 减少分配（ArrayPool）

**预期**：265 → **180 lines** (-32%)

#### 3.2 ReplayableEventCapturer.cs (263 lines)
**优化点**：
- ❌ 简化捕获逻辑
- ❌ 移除冗余的状态跟踪
- ❌ 优化序列化

**预期**：263 → **180 lines** (-31%)

#### 3.3 StateReconstructor.cs (191 lines)
**优化点**：
- ❌ 简化状态重建算法
- ❌ 减少临时对象

**预期**：191 → **130 lines** (-32%)

**预期总计**：1,470 → **~1,100 lines** (-25%)

---

### Phase 4: Debugger.AspNetCore 优化

**目标**：539 lines → ~400 lines (-26%)

#### 4.1 DebuggerEndpoints.cs (208 lines)
**优化点**：
- ❌ 合并重复的端点逻辑
- ❌ 提取公共响应格式化
- ❌ 简化查询参数处理

**预期**：208 → **140 lines** (-33%)

**预期总计**：539 → **~400 lines** (-26%)

---

### Phase 5: 示例优化（OrderSystem.Api）

**目标**：~800 lines → ~500 lines (-37%)

#### 5.1 Program.cs (184 lines)
**优化点**：
- ❌ 静态 Demo 数据
- ❌ 减少匿名对象分配
- ❌ 合并重复逻辑

**预期**：184 → **100 lines** (-45%)

#### 5.2 OrderCommandHandlers.cs (256 lines)
**优化点**：
- ❌ 移除扩展指南注释（放文档）
- ❌ LoggerMessage Source Generator
- ❌ 优化 ResultMetadata

**预期**：256 → **160 lines** (-37%)

#### 5.3 其他文件优化
- Repository: Task → ValueTask
- Services: 合并接口文件

**预期总计**：~800 → **~500 lines** (-37%)

---

### Phase 6: 其他库优化

#### 6.1 Catga.Persistence.Redis (1,323 lines)
**优化点**：
- ❌ 简化 Redis 操作
- ❌ 减少序列化开销
- ❌ 优化批处理逻辑

**预期**：1,323 → **~1,000 lines** (-24%)

#### 6.2 Catga.Transport.Nats (448 lines)
**优化点**：
- ❌ 简化 NATS 订阅逻辑
- ❌ 减少重复代码

**预期**：448 → **~350 lines** (-22%)

## 📊 预期优化成果

### 总体目标

| 指标 | 当前 | 优化后 | 减少 |
|------|------|--------|------|
| **总代码行数** | 10,951 | **~7,650** | **-30%** |
| **核心库 (Catga)** | 3,178 | **~2,500** | **-21%** |
| **InMemory** | 2,267 | **~1,800** | **-20%** |
| **Debugger** | 1,470 | **~1,100** | **-25%** |
| **示例 (OrderSystem)** | ~800 | **~500** | **-37%** |

### 性能提升预期

1. **内存优化**:
   - ✅ ValueTask 替代 Task（避免分配）
   - ✅ ArrayPool 重用数组
   - ✅ 减少 LINQ 中间对象
   - ✅ 静态数据重用

2. **CPU 优化**:
   - ✅ LoggerMessage Source Generator（零分配日志）
   - ✅ 简化算法逻辑
   - ✅ 减少不必要的序列化

3. **代码质量**:
   - ✅ 消除重复代码
   - ✅ 提取公共逻辑
   - ✅ 降低圈复杂度

## 🔧 通用优化技巧

### 1. Task → ValueTask
```csharp
// ❌ Before
public Task<Order?> GetByIdAsync(string id)
    => Task.FromResult(_orders.TryGetValue(id, out var order) ? order : null);

// ✅ After
public ValueTask<Order?> GetByIdAsync(string id)
    => new(_orders.TryGetValue(id, out var order) ? order : null);
```

### 2. LoggerMessage Source Generator
```csharp
// ❌ Before
_logger.LogInformation("Order created: {OrderId}, Amount: {Amount}", orderId, amount);

// ✅ After
[LoggerMessage(Level = LogLevel.Information, Message = "Order created: {OrderId}, Amount: {Amount}")]
partial void LogOrderCreated(string orderId, decimal amount);

LogOrderCreated(orderId, amount);
```

### 3. 减少 LINQ 分配
```csharp
// ❌ Before
var filtered = items.Where(x => x.IsActive).Select(x => x.Id).ToList();

// ✅ After
var filtered = new List<string>(items.Count);
foreach (var item in items)
{
    if (item.IsActive) filtered.Add(item.Id);
}
```

### 4. 静态数据重用
```csharp
// ❌ Before (每次都 new)
return new { Success = true, Data = result };

// ✅ After
private static readonly object SuccessResponse = new { Success = true };
return SuccessResponse;
```

### 5. Collection Initializer
```csharp
// ❌ Before
var metadata = new ResultMetadata();
metadata.Add("Key1", "Value1");
metadata.Add("Key2", "Value2");

// ✅ After
var metadata = new ResultMetadata
{
    ["Key1"] = "Value1",
    ["Key2"] = "Value2"
};
```

## ✅ 执行计划

### 优先级排序

1. **Phase 5 (OrderSystem)** - 最容易，立即见效 ✅ **立即执行**
2. **Phase 1 (Catga Core)** - 影响最大 ✅ **高优先级**
3. **Phase 2 (InMemory)** - 性能关键 ✅ **高优先级**
4. **Phase 3 (Debugger)** - 可观测性 ✅ **中优先级**
5. **Phase 4 (Debugger.AspNetCore)** - UI 层 ✅ **中优先级**
6. **Phase 6 (Redis/NATS)** - 可选优化 ✅ **低优先级**

### 验证流程

每个 Phase 完成后：
1. ✅ 编译验证（零错误零警告）
2. ✅ 单元测试通过
3. ✅ 性能基准测试对比
4. ✅ 功能验证（所有 API 行为一致）

## 🎯 最终目标

- ✅ **代码量减少 30%**（10,951 → 7,650 lines）
- ✅ **性能提升 20-30%**（内存分配、执行时间）
- ✅ **代码质量提升**（更清晰、更易维护）
- ✅ **功能完全不变**（所有测试通过）

---

**准备好执行全局优化了吗？** 🚀

