# Catga Debugger 页面功能完善计划

## 🔍 **问题分析**

用户反馈："实际上页面说的功能没有完全实现"

经过全面检查，发现以下问题：

### 1. **时间旅行调试器 (replay-player.html)** ✅ 基本实现

**API端点状态**:
- ✅ `GET /debug-api/flows` - 获取所有流
- ✅ `POST /debug-api/replay/flow` - 启动replay session
- ✅ `GET /debug-api/replay/flow/{id}/timeline` - 获取时间线
- ✅ `GET /debug-api/replay/flow/{id}/state` - 获取当前状态
- ✅ `POST /debug-api/replay/flow/{id}/step` - 单步执行
- ✅ `POST /debug-api/replay/flow/{id}/jump` - 跳转到时间点

**实现状态**: ✅ **完整实现**
- FlowReplay、FlowStateMachine 已实现
- ReplaySessionManager 管理会话
- Timeline、Variables、CallStack 都有数据支持

**潜在问题**:
- ⚠️ CallStack 数据可能为空（依赖 `CaptureCallStacks` 配置）
- ⚠️ Variables 数据可能为空（依赖 `CaptureVariables` 配置）

---

### 2. **断点调试器 (breakpoints.html)** ⚠️ **部分实现**

**API端点状态**:
- ✅ `GET /debug-api/breakpoints` - 获取所有断点
- ✅ `POST /debug-api/breakpoints` - 添加断点
- ✅ `DELETE /debug-api/breakpoints/{id}` - 删除断点
- ✅ `POST /debug-api/breakpoints/{id}/toggle` - 切换断点状态
- ✅ `POST /debug-api/breakpoints/continue/{correlationId}` - 继续执行

**后端服务**: `BreakpointManager`
- ✅ 断点的CRUD操作
- ✅ 条件断点支持（Always、MessageType）
- ✅ 命中计数
- ⚠️ **缺少实际断点触发逻辑**

**关键缺失**:
1. ❌ **断点触发机制**: `BreakpointBehavior<TRequest, TResponse>` 虽然存在，但**未被注册到Pipeline**
2. ❌ **断点暂停执行**: 没有实现真正的"暂停"机制
3. ❌ **当前暂停的请求列表**: 页面显示"当前暂停的请求"，但没有数据源
4. ❌ **StepOver/StepInto/StepOut**: Continue API支持了这些操作，但没有实际实现逻辑

**实现缺口**:
```csharp
// BreakpointManager 有方法，但没有真正的暂停逻辑
public void Pause(string correlationId)
{
    // TODO: 实现暂停逻辑
}

public bool Continue(string correlationId, DebugAction action)
{
    // TODO: 实现继续执行逻辑
    return false;
}
```

---

### 3. **性能分析器 (profiling.html)** ⚠️ **部分实现**

**API端点状态**:
- ✅ `GET /debug-api/profiling/slow-queries` - 慢查询
- ✅ `GET /debug-api/profiling/hot-spots` - 热点分析
- ✅ `GET /debug-api/profiling/gc-analysis` - GC分析
- ✅ `GET /debug-api/profiling/flame-graph/{correlationId}` - 火焰图

**后端服务实现状态**:

#### A. **`PerformanceAnalyzer`** - ⚠️ 简化实现
```csharp
// DetectSlowQueriesAsync - 基于EventStore的事件持续时间
// 问题:
// 1. 需要事件有Duration字段（当前DebugEvent没有）
// 2. 算法过于简单，实际返回空列表

// IdentifyHotSpotsAsync - 按消息类型分组统计
// 问题:
// 1. 只统计调用次数，没有CPU/内存分析
// 2. 算法过于简单

// AnalyzeGcPressure - 调用GC.CollectionCount()
// 状态: ✅ 有数据，但只是全局GC统计，不是特定于Catga的
```

#### B. **`FlameGraphBuilder`** - ⚠️ Mock实现
```csharp
// BuildCpuFlameGraphAsync / BuildMemoryFlameGraphAsync
// 问题:
// 1. 返回Mock数据（"Handler" / "Event Processing" 等静态节点）
// 2. 没有真实的性能采样数据
```

**关键缺失**:
1. ❌ **性能数据采集**: 没有在Pipeline中采集执行时间、CPU、内存数据
2. ❌ **DebugEvent缺少Duration字段**: 无法准确计算慢查询
3. ❌ **火焰图数据源**: 没有真实的调用堆栈和耗时数据
4. ❌ **实时性能监控**: 没有实时采集机制

---

## 🎯 **修复计划**

### **Phase 1: 断点调试器完善** (高优先级)

#### 1.1 实现断点触发机制
- [ ] 修改 `BreakpointBehavior<TRequest, TResponse>` 实现真正的暂停逻辑
- [ ] 在 `DebuggerServiceCollectionExtensions` 中注册 `BreakpointBehavior`
- [ ] 使用 `SemaphoreSlim` 或 `ManualResetEventSlim` 实现暂停/继续

#### 1.2 实现暂停队列
```csharp
public class BreakpointManager
{
    // 新增: 存储暂停的请求
    private readonly ConcurrentDictionary<string, PausedRequest> _pausedRequests = new();

    public sealed record PausedRequest
    {
        public string CorrelationId { get; init; }
        public string MessageType { get; init; }
        public DateTime PausedAt { get; init; }
        public Dictionary<string, object?> State { get; init; }
        public SemaphoreSlim WaitHandle { get; init; } // 用于控制暂停/继续
    }

    public List<PausedRequest> GetPausedRequests() =>
        _pausedRequests.Values.ToList();
}
```

#### 1.3 新增API端点
- [ ] `GET /debug-api/breakpoints/paused` - 获取当前暂停的请求列表

#### 1.4 更新UI
- [ ] 实时轮询 `/debug-api/breakpoints/paused` (或使用SignalR推送)
- [ ] 显示暂停请求的详细信息

---

### **Phase 2: 性能分析器数据采集** (中优先级)

#### 2.1 增强DebugEvent结构
```csharp
public sealed class DebugEvent
{
    // 现有字段...

    // 新增: 性能相关字段
    public TimeSpan? Duration { get; init; }
    public long? MemoryAllocated { get; init; }
    public int? ThreadId { get; init; }
    public DateTime? CompletedAt { get; init; }
}
```

#### 2.2 创建性能采集Pipeline Behavior
```csharp
public class PerformanceCaptureBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEventStore _eventStore;

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var startMemory = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            var response = await next();

            var endTime = DateTime.UtcNow;
            var endMemory = GC.GetAllocatedBytesForCurrentThread();

            // 记录性能事件
            await _eventStore.StoreEventAsync(new DebugEvent
            {
                Type = EventType.RequestCompleted,
                Timestamp = startTime,
                CompletedAt = endTime,
                Duration = endTime - startTime,
                MemoryAllocated = endMemory - startMemory,
                ThreadId = Environment.CurrentManagedThreadId,
                // ... 其他字段
            });

            return response;
        }
        catch { /* ... */ }
    }
}
```

#### 2.3 改进 PerformanceAnalyzer
- [ ] 基于真实Duration字段实现慢查询检测
- [ ] 基于MemoryAllocated实现内存热点分析
- [ ] 添加P50/P95/P99百分位统计

#### 2.4 改进 FlameGraphBuilder
- [ ] 从CallStack数据构建火焰图
- [ ] 聚合相同调用路径的耗时
- [ ] 支持CPU和内存两种视图

---

### **Phase 3: UI体验优化** (低优先级)

#### 3.1 实时更新
- [ ] 使用SignalR替代轮询（debugger hub已存在）
- [ ] 断点触发时推送通知到UI
- [ ] 性能数据实时流式更新

#### 3.2 数据可视化
- [ ] 火焰图使用D3.js或ECharts渲染（当前只是占位符）
- [ ] Timeline使用交互式SVG
- [ ] GC分析添加趋势图

#### 3.3 错误处理
- [ ] API失败时显示友好错误信息
- [ ] 空数据状态提示
- [ ] Loading状态

---

## 📊 **功能完成度矩阵**

| 功能模块 | API端点 | 后端逻辑 | 数据采集 | UI交互 | 完成度 |
|---------|--------|---------|---------|--------|-------|
| 时间旅行 - 流回放 | ✅ | ✅ | ✅ | ✅ | 90% |
| 时间旅行 - 单步执行 | ✅ | ✅ | ⚠️ | ✅ | 80% |
| 时间旅行 - 变量监视 | ✅ | ✅ | ⚠️ | ✅ | 70% |
| 断点 - CRUD | ✅ | ✅ | N/A | ✅ | 100% |
| 断点 - 触发暂停 | ✅ | ❌ | ❌ | ✅ | 30% |
| 断点 - 单步调试 | ✅ | ❌ | ❌ | ✅ | 20% |
| 性能 - 慢查询 | ✅ | ⚠️ | ❌ | ✅ | 40% |
| 性能 - 热点分析 | ✅ | ⚠️ | ❌ | ✅ | 40% |
| 性能 - GC分析 | ✅ | ⚠️ | ✅ | ✅ | 60% |
| 性能 - 火焰图 | ✅ | ⚠️ | ❌ | ⚠️ | 30% |

**总体完成度**: 约 **56%**

---

## 🚀 **实施建议**

### **快速修复 (1-2小时)**
1. 实现断点触发和暂停机制（Phase 1.1, 1.2）
2. 添加性能数据采集Behavior（Phase 2.1, 2.2）
3. 更新PerformanceAnalyzer使用真实数据（Phase 2.3）

### **完整实现 (4-6小时)**
- 完成Phase 1和Phase 2的所有内容
- 关键是性能数据采集和断点暂停机制

### **增强体验 (可选, 2-3小时)**
- Phase 3的实时更新和可视化优化

---

## ⚠️ **重要说明**

### 1. **生产环境配置**
断点和性能采集会影响性能，必须通过配置控制：

```csharp
services.AddCatgaDebugger(options =>
{
    // 生产环境应该禁用断点
    options.Mode = DebuggerMode.ProductionOptimized;

    // 性能采集使用低采样率
    options.SamplingRate = 0.01; // 1%
});
```

### 2. **条件编译**
高级调试功能应该在Release构建中完全移除：
```csharp
#if DEBUG
services.AddSingleton<BreakpointBehavior<,>>();
#endif
```

### 3. **数据存储**
性能数据会快速增长，需要：
- 滚动窗口（只保留最近1小时）
- 自动清理旧数据
- 可选的持久化到Redis/MongoDB

---

## 📋 **执行顺序**

建议按以下顺序实施：

1. **Phase 2.1 + 2.2** - 性能数据采集（基础设施）
2. **Phase 2.3** - 改进PerformanceAnalyzer（立即可见效果）
3. **Phase 1.1 + 1.2** - 断点触发机制（核心功能）
4. **Phase 1.3 + 1.4** - 断点UI更新
5. **Phase 2.4** - 火焰图（复杂但有价值）
6. **Phase 3** - UI优化（锦上添花）

---

**你希望从哪个Phase开始执行？**
- A: Phase 1 - 断点调试器完善（高优先级，核心功能）
- B: Phase 2 - 性能分析器数据采集（中优先级，基础设施）
- C: Phase 3 - UI体验优化（低优先级，可选）
- D: 全部执行（完整实现）

