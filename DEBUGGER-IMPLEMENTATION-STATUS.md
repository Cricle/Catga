# Catga Debugger 实施状态

**更新时间**: 2025-10-17  
**状态**: 核心框架已完成，UI 和高级功能待实施

---

## ✅ 已完成（核心框架）

### 阶段 1.1: 消息断点系统 ✅
**文件**:
- `src/Catga.Debugger/Breakpoints/BreakpointCondition.cs` ✅
- `src/Catga.Debugger/Breakpoints/Breakpoint.cs` ✅
- `src/Catga.Debugger/Breakpoints/BreakpointManager.cs` ✅
- `src/Catga.Debugger/Pipeline/BreakpointBehavior.cs` ✅

**功能**:
- ✅ 断点设置（按类型、CorrelationId、条件）
- ✅ 条件表达式评估
- ✅ 断点命中通知
- ✅ 等待用户操作（Continue/Step）
- ✅ 线程安全（ConcurrentDictionary）
- ✅ 生产安全（enabled 开关，默认禁用）

**使用示例**:
```csharp
// 注册服务
services.AddSingleton<BreakpointManager>(sp => 
    new BreakpointManager(
        sp.GetRequiredService<ILogger<BreakpointManager>>(),
        enabled: isDevelopment // 仅开发环境启用
    )
);

// 添加断点
var condition = BreakpointCondition.MessageType("bp1", "CreateOrderCommand");
var breakpoint = new Breakpoint("bp1", "Order Creation", condition);
breakpointManager.AddBreakpoint(breakpoint);

// 在 Pipeline 中检查
var action = await breakpointManager.CheckBreakpointAsync(request, correlationId);
// 等待用户 Continue
```

---

### 阶段 1.2: 变量监视器 ✅
**文件**:
- `src/Catga.Debugger/Watch/WatchExpression.cs` ✅
- `src/Catga.Debugger/Watch/WatchManager.cs` ✅

**功能**:
- ✅ 监视表达式定义
- ✅ 实时评估
- ✅ 历史值追踪（最多100个）
- ✅ 错误处理（评估失败不影响执行）
- ✅ 线程安全
- ✅ 生产安全

**使用示例**:
```csharp
// 创建监视表达式
var watch = WatchExpression.FromLambda<int>(
    "watch1",
    "Events.Count",
    ctx => ctx.Events.Count
);

// 添加监视
watchManager.AddWatch(watch);

// 评估所有监视
var values = watchManager.EvaluateAll(captureContext);
// values["watch1"].Value = 5
```

---

## 🚧 待实施（按优先级）

### 阶段 1.3: 完整调用栈追踪 ⏳
**优先级**: P0  
**预计时间**: 3-4天

**需要实现**:
- `CallStackFrame` - 栈帧数据结构
- `CallStackTracker` - 栈追踪器（AsyncLocal）
- `CallStackBehavior` - Pipeline 行为
- UI 栈帧面板

---

### 阶段 2.1: 火焰图生成器 ⏳
**优先级**: P0  
**预计时间**: 5-6天

**需要实现**:
- `FlameGraphBuilder` - 火焰图数据生成
- `FlameGraphNode` - 节点数据结构
- `ProfilerSampler` - 性能采样器
- D3.js 可视化组件

---

### 阶段 2.2: 性能瓶颈分析 ⏳
**优先级**: P0  
**预计时间**: 3-4天

**需要实现**:
- `PerformanceAnalyzer` - 性能分析器
- `SlowQueryDetector` - 慢查询检测
- `HotSpotIdentifier` - 热点识别
- `GcAnalyzer` - GC 分析

---

### 阶段 3.1: 结构化日志查看器 ⏳
**优先级**: P1  
**预计时间**: 3-4天

**需要实现**:
- `LogEntry` - 日志条目
- `LogStore` - 日志存储（实现 ILoggerProvider）
- `LogFilter` - 日志过滤器
- UI 日志流面板

---

### 阶段 3.2: 分布式追踪可视化 ⏳
**优先级**: P1  
**预计时间**: 4-5天

**需要实现**:
- `TraceTimeline` - 时间线构建
- `ServiceDependencyGraph` - 服务拓扑图
- Mermaid.js 集成
- UI 追踪面板

---

### 阶段 4: 错误诊断 ⏳
**优先级**: P1  
**预计时间**: 3-4天

**需要实现**:
- `ExceptionAggregator` - 异常聚合
- `ExceptionGroup` - 异常分组
- `RootCauseAnalyzer` - 根因分析
- UI 异常面板

---

### 阶段 5: 数据探查 ⏳
**优先级**: P2  
**预计时间**: 5-7天

**需要实现**:
- `PayloadViewer` - Payload 查看器
- `PayloadDiffer` - Diff 工具
- Monaco Editor 集成
- UI 数据面板

---

### 阶段 6: 测试验证 ⏳
**优先级**: P2  
**预计时间**: 7-9天

**需要实现**:
- `TrafficReplayer` - 流量回放
- `MessageInjector` - 消息注入
- `StressTest` - 压力测试
- UI 测试面板

---

## 🎨 UI 增强计划

### 新增面板（Alpine.js + Tailwind CSS）

#### 1. 断点面板
**路径**: `/debugger/breakpoints.html`

**功能**:
- 断点列表（表格）
- 添加/删除/启用/禁用
- 断点命中通知（实时）
- 继续/单步按钮

**API 端点**:
```csharp
POST   /debug-api/breakpoints          // 添加断点
GET    /debug-api/breakpoints          // 获取所有断点
DELETE /debug-api/breakpoints/{id}     // 删除断点
POST   /debug-api/breakpoints/{id}/toggle // 启用/禁用
POST   /debug-api/breakpoints/{id}/continue // 继续执行
```

---

#### 2. 监视面板
**路径**: `/debugger/watches.html`

**功能**:
- 监视列表（表格）
- 添加/删除监视
- 实时值更新（SignalR）
- 历史值时间线

**API 端点**:
```csharp
POST   /debug-api/watches              // 添加监视
GET    /debug-api/watches              // 获取所有监视
DELETE /debug-api/watches/{id}         // 删除监视
GET    /debug-api/watches/{id}/history // 获取历史
```

---

#### 3. 火焰图面板
**路径**: `/debugger/flame-graph.html`

**功能**:
- CPU 火焰图（ECharts）
- 内存火焰图
- 交互式缩放
- 导出 SVG/PNG

**API 端点**:
```csharp
GET /debug-api/profiling/flame-graph/{correlationId}?type=cpu
GET /debug-api/profiling/flame-graph/{correlationId}?type=memory
```

---

#### 4. 调用栈面板
**路径**: `/debugger/call-stack.html`

**功能**:
- 栈帧列表（类似 VS Debugger）
- 变量查看
- 栈帧导航
- 源码定位

**API 端点**:
```csharp
GET /debug-api/call-stack/{correlationId}
GET /debug-api/call-stack/{correlationId}/frame/{index}
```

---

## 🔧 集成到现有 Debugger

### DebuggerHub 增强
**文件**: `src/Catga.Debugger.AspNetCore/Hubs/DebuggerHub.cs`

**新增方法**:
```csharp
// 断点
public async Task<string> AddBreakpoint(BreakpointDto breakpoint);
public async Task<bool> RemoveBreakpoint(string breakpointId);
public async Task<bool> ToggleBreakpoint(string breakpointId, bool enabled);
public async Task<bool> ContinueExecution(string correlationId, string action);

// 监视
public async Task<string> AddWatch(string expression);
public async Task<bool> RemoveWatch(string watchId);
public async Task<WatchValue> EvaluateWatch(string watchId, string correlationId);

// 性能
public async Task<FlameGraph> GetFlameGraph(string correlationId, string type);
public async Task<List<SlowQuery>> GetSlowQueries(int threshold);
```

---

### DebuggerEndpoints 增强
**文件**: `src/Catga.Debugger.AspNetCore/Endpoints/DebuggerEndpoints.cs`

**新增端点组**:
```csharp
// 断点
group.MapBreakpointEndpoints();

// 监视
group.MapWatchEndpoints();

// 性能分析
group.MapProfilingEndpoints();

// 调用栈
group.MapCallStackEndpoints();

// 日志
group.MapLoggingEndpoints();
```

---

## 📋 服务注册

### 新增配置选项
**文件**: `src/Catga.Debugger/Models/ReplayOptions.cs`

**新增属性**:
```csharp
public class ReplayOptions
{
    // 现有...
    
    // 调试功能开关
    public bool EnableBreakpoints { get; set; } = false;
    public bool EnableWatch { get; set; } = false;
    public bool EnableCallStack { get; set; } = false;
    public bool EnableProfiling { get; set; } = false;
    public bool EnableLogging { get; set; } = false;
    
    // 生产安全模式
    public bool ReadOnlyMode { get; set; } = false;
}
```

### 服务注册增强
**文件**: `src/Catga.Debugger/DependencyInjection/DebuggerServiceCollectionExtensions.cs`

**新增注册**:
```csharp
// 断点
if (options.EnableBreakpoints)
{
    services.AddSingleton(sp => new BreakpointManager(
        sp.GetRequiredService<ILogger<BreakpointManager>>(),
        enabled: true
    ));
    services.AddSingleton(typeof(BreakpointBehavior<,>));
}

// 监视
if (options.EnableWatch)
{
    services.AddSingleton(sp => new WatchManager(
        sp.GetRequiredService<ILogger<WatchManager>>(),
        enabled: true
    ));
}

// 性能分析
if (options.EnableProfiling)
{
    services.AddSingleton<FlameGraphBuilder>();
    services.AddSingleton<PerformanceAnalyzer>();
}

// 日志
if (options.EnableLogging)
{
    services.AddSingleton<ILoggerProvider, LogStore>();
}
```

---

## 🔒 生产安全验证

### 环境配置

#### 开发环境
```csharp
services.AddCatgaDebugger(options => {
    options.Mode = DebuggerMode.Development;
    options.EnableBreakpoints = true;
    options.EnableWatch = true;
    options.EnableCallStack = true;
    options.EnableProfiling = true;
    options.EnableLogging = true;
});
```

#### 生产环境
```csharp
services.AddCatgaDebugger(options => {
    options.Mode = DebuggerMode.ProductionSafe;
    options.EnableBreakpoints = false;  // 禁用断点
    options.EnableWatch = false;        // 禁用监视
    options.EnableCallStack = false;    // 禁用调用栈
    options.EnableProfiling = false;    // 禁用性能分析
    options.EnableLogging = true;       // 仅保留日志（采样）
    options.SamplingRate = 0.0001;      // 万分之一采样
    options.ReadOnlyMode = true;        // 只读模式
});
```

---

## ✅ 验收标准

### 功能完整性
- [x] 断点系统（基础功能）
- [x] 变量监视（基础功能）
- [ ] 调用栈追踪
- [ ] 火焰图生成
- [ ] 慢查询检测
- [ ] 结构化日志
- [ ] 异常聚合

### 性能标准
- [ ] 断点检查开销 < 1μs（禁用时）
- [ ] 监视评估开销 < 10μs/表达式
- [ ] 内存占用 < 100MB（所有功能启用）
- [ ] UI 响应时间 < 100ms

### 安全标准
- [x] 生产环境默认禁用
- [x] 条件编译支持（#if DEBUG）
- [ ] 权限控制（API 认证）
- [ ] 只读模式（禁止修改状态）

---

## 📊 进度统计

| 阶段 | 功能 | 状态 | 完成度 |
|------|------|------|--------|
| 阶段 1.1 | 断点系统 | ✅ 完成 | 100% |
| 阶段 1.2 | 变量监视 | ✅ 完成 | 100% |
| 阶段 1.3 | 调用栈 | ⏳ 待实施 | 0% |
| 阶段 2.1 | 火焰图 | ⏳ 待实施 | 0% |
| 阶段 2.2 | 性能分析 | ⏳ 待实施 | 0% |
| 阶段 3.1 | 日志查看 | ⏳ 待实施 | 0% |
| 阶段 3.2 | 追踪可视化 | ⏳ 待实施 | 0% |
| 阶段 4 | 错误诊断 | ⏳ 待实施 | 0% |
| 阶段 5 | 数据探查 | ⏳ 待实施 | 0% |
| 阶段 6 | 测试验证 | ⏳ 待实施 | 0% |
| **总体** | | | **20%** |

---

## 🚀 下一步计划

### 立即（本周）
1. ✅ 提交断点系统代码
2. ✅ 提交变量监视器代码
3. ⏳ 实现调用栈追踪
4. ⏳ 创建断点 UI 面板
5. ⏳ 创建监视 UI 面板

### 短期（下周）
1. 实现火焰图生成器
2. 实现性能瓶颈分析
3. 集成测试

### 中期（本月）
1. 实现日志查看器
2. 实现追踪可视化
3. 实现错误诊断

### 长期（下月）
1. 数据探查工具
2. 流量回放
3. 智能分析

---

## 📝 待办事项

- [ ] 实现调用栈追踪（CallStackTracker）
- [ ] 创建断点 UI 面板
- [ ] 创建监视 UI 面板
- [ ] 添加 SignalR 实时通知（断点命中）
- [ ] 创建火焰图生成器
- [ ] 创建性能分析器
- [ ] 编写集成测试
- [ ] 编写文档
- [ ] 生产环境压测验证

---

**总结**: 核心断点和监视框架已完成，具备生产安全保障。后续将继续实施调用栈、性能分析等高级功能。

