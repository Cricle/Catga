# Catga Debugger 能力增强计划

**制定日期**: 2025-10-17
**目标**: 打造生产级、功能完备的分布式系统调试器

---

## 📊 当前能力评估

### ✅ 已有功能
1. **基础监控**
   - 消息流实时监控（SignalR）
   - 统计信息展示（成功率、延迟）
   - 基础时间旅行回放

2. **事件存储**
   - InMemoryEventStore（Ring Buffer）
   - 事件捕获（ReplayableEventCapturer）
   - 状态重构（StateReconstructor）

3. **UI 界面**
   - 消息流列表
   - 统计面板
   - 时间旅行播放器（基础）

### ❌ 缺失能力

#### 1️⃣ **实时调试能力** ⭐⭐⭐⭐⭐
- ❌ 断点设置（消息级、处理器级）
- ❌ 变量监视（实时更新）
- ❌ 条件断点（表达式评估）
- ❌ 单步执行（Step Into/Over/Out）
- ❌ 调用栈追踪（完整链路）

#### 2️⃣ **性能分析能力** ⭐⭐⭐⭐⭐
- ❌ 火焰图（Flame Graph）
- ❌ 性能瓶颈分析
- ❌ 内存分配追踪
- ❌ GC 压力分析
- ❌ CPU 热点识别
- ❌ 慢查询检测

#### 3️⃣ **日志与追踪** ⭐⭐⭐⭐⭐
- ❌ 结构化日志查看
- ❌ 日志过滤与搜索
- ❌ 日志级别控制
- ❌ 分布式追踪可视化（已有 Jaeger 集成但缺 UI）
- ❌ Span 详情查看

#### 4️⃣ **错误诊断** ⭐⭐⭐⭐⭐
- ❌ 异常聚合分析
- ❌ 错误堆栈美化
- ❌ 相似错误分组
- ❌ 错误趋势分析
- ❌ 根因分析（Root Cause Analysis）

#### 5️⃣ **数据探查** ⭐⭐⭐⭐
- ❌ 消息 Payload 查看器（JSON/XML/Binary）
- ❌ Diff 对比工具（请求前后对比）
- ❌ 数据流图（Data Flow Diagram）
- ❌ 聚合根状态查看
- ❌ 事件溯源历史

#### 6️⃣ **测试与验证** ⭐⭐⭐⭐
- ❌ 流量回放（Replay Traffic）
- ❌ 消息注入（Inject Message）
- ❌ 压力测试（Stress Test）
- ❌ A/B 对比测试
- ❌ Chaos Engineering 支持

#### 7️⃣ **协作与分享** ⭐⭐⭐
- ❌ 调试会话分享
- ❌ 快照导出/导入
- ❌ 注释与标记
- ❌ 团队协作（多用户）

#### 8️⃣ **智能分析** ⭐⭐⭐⭐
- ❌ 异常模式识别
- ❌ 性能回归检测
- ❌ 流量异常告警
- ❌ 推荐优化建议
- ❌ 自动诊断报告

---

## 🎯 增强计划（分阶段）

### 阶段 1：核心调试能力（P0 - 必须）⭐⭐⭐⭐⭐

#### 1.1 消息断点系统
**优先级**: P0
**工作量**: 大（5-7天）

**功能**:
- 设置消息断点（按类型、CorrelationId、条件）
- 断点触发时暂停处理
- 断点管理（启用/禁用/删除）
- 条件表达式评估（基于消息内容）

**实现**:
```csharp
// Catga.Debugger/Breakpoints/BreakpointManager.cs
public class BreakpointManager
{
    public void SetBreakpoint(string messageType, string? condition = null);
    public void SetBreakpoint(Func<IMessage, bool> predicate);
    public bool ShouldBreak(IMessage message);
}

// Catga.Debugger/Pipeline/BreakpointBehavior.cs
public class BreakpointBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(...)
    {
        if (_breakpointManager.ShouldBreak(request))
        {
            await _debuggerHub.NotifyBreakpointHit(request);
            await WaitForContinue(); // 等待用户操作
        }
        return await next();
    }
}
```

**UI**:
- 断点列表面板
- 断点设置对话框
- 断点命中通知
- 继续/单步按钮

---

#### 1.2 变量监视器
**优先级**: P0
**工作量**: 中（3-4天）

**功能**:
- 实时监视变量值
- 支持表达式（如 `order.TotalAmount > 1000`）
- 历史值追踪
- 值变化高亮

**实现**:
```csharp
// Catga.Debugger/Watch/WatchExpression.cs
public class WatchExpression
{
    public string Expression { get; set; }
    public object? Evaluate(CaptureContext context);
    public List<object?> History { get; }
}

// UI 集成
public class DebuggerHub : Hub
{
    public async Task AddWatch(string expression);
    public async Task<object?> EvaluateWatch(string sessionId, string expression);
}
```

**UI**:
- 监视面板
- 表达式编辑器
- 历史值时间线

---

#### 1.3 完整调用栈
**优先级**: P0
**工作量**: 中（3-4天）

**功能**:
- 完整调用链追踪（Handler → Event → Handler）
- 跨服务追踪（基于 CorrelationId）
- 调用时长标注
- 栈帧导航

**实现**:
```csharp
// Catga.Debugger/CallStack/CallStackTracker.cs
public class CallStackFrame
{
    public string MethodName { get; set; }
    public string? FileName { get; set; }
    public int? LineNumber { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, object?> LocalVariables { get; set; }
}

public class CallStackTracker
{
    private readonly AsyncLocal<Stack<CallStackFrame>> _stack = new();

    public IDisposable PushFrame(CallStackFrame frame);
    public IReadOnlyList<CallStackFrame> GetCurrentStack();
}
```

**UI**:
- 调用栈面板（类似 VS Debugger）
- 栈帧详情查看
- 变量值展示

---

### 阶段 2：性能分析（P0 - 必须）⭐⭐⭐⭐⭐

#### 2.1 火焰图生成器
**优先级**: P0
**工作量**: 大（5-6天）

**功能**:
- CPU 火焰图
- 内存分配火焰图
- 异步调用火焰图
- 交互式缩放与过滤

**实现**:
```csharp
// Catga.Debugger/Profiling/FlameGraphBuilder.cs
public class FlameGraphBuilder
{
    public FlameGraph BuildCpuFlameGraph(string correlationId);
    public FlameGraph BuildMemoryFlameGraph(string correlationId);
}

public class FlameGraph
{
    public FlameGraphNode Root { get; set; }
    public long TotalSamples { get; set; }
}

public class FlameGraphNode
{
    public string Name { get; set; }
    public long SelfTime { get; set; }
    public long TotalTime { get; set; }
    public List<FlameGraphNode> Children { get; set; }
}
```

**UI**:
- D3.js/ECharts 火焰图可视化
- Tooltip 显示详细信息
- 点击展开/折叠
- 导出 SVG/PNG

---

#### 2.2 性能瓶颈分析
**优先级**: P0
**工作量**: 中（3-4天）

**功能**:
- 慢查询检测（>500ms）
- 热点方法识别（Top 10）
- GC 暂停分析
- 线程池饥饿检测

**实现**:
```csharp
// Catga.Debugger/Profiling/PerformanceAnalyzer.cs
public class PerformanceAnalyzer
{
    public List<SlowQuery> DetectSlowQueries(TimeSpan threshold);
    public List<HotSpot> IdentifyHotSpots(int topN = 10);
    public GcAnalysis AnalyzeGcPressure();
}

public class SlowQuery
{
    public string CorrelationId { get; set; }
    public string RequestType { get; set; }
    public TimeSpan Duration { get; set; }
    public List<CallStackFrame> Bottleneck { get; set; }
}
```

**UI**:
- 慢查询列表
- 热点方法排名
- GC 压力图表
- 优化建议面板

---

### 阶段 3：日志与追踪（P1 - 重要）⭐⭐⭐⭐

#### 3.1 结构化日志查看器
**优先级**: P1
**工作量**: 中（3-4天）

**功能**:
- 日志流实时展示
- 多级过滤（级别、时间、关键字、CorrelationId）
- 日志高亮与格式化
- 日志导出

**实现**:
```csharp
// Catga.Debugger/Logging/LogEntry.cs
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, object?> Properties { get; set; }
    public Exception? Exception { get; set; }
}

// Catga.Debugger/Logging/LogStore.cs
public class LogStore : ILoggerProvider
{
    public IEnumerable<LogEntry> Query(LogFilter filter);
    public event Action<LogEntry> LogReceived;
}
```

**UI**:
- 日志流面板（虚拟滚动）
- 过滤器工具栏
- 日志详情弹窗
- 颜色编码（Error=红，Warn=黄）

---

#### 3.2 分布式追踪可视化
**优先级**: P1
**工作量**: 中（4-5天）

**功能**:
- Span 时间线视图
- 服务依赖图
- Trace 搜索
- Span 详情查看（Tags、Logs、Baggage）

**实现**:
```csharp
// Catga.Debugger/Tracing/TraceVisualization.cs
public class TraceVisualization
{
    public TraceTimeline BuildTimeline(string traceId);
    public ServiceDependencyGraph BuildDependencyGraph();
}

public class TraceTimeline
{
    public List<SpanNode> Spans { get; set; }
    public TimeSpan TotalDuration { get; set; }
}
```

**UI**:
- 时间线图（类似 Jaeger UI）
- 服务拓扑图（D3.js）
- Span 详情面板

---

### 阶段 4：错误诊断（P1 - 重要）⭐⭐⭐⭐

#### 4.1 异常聚合与分析
**优先级**: P1
**工作量**: 中（3-4天）

**功能**:
- 异常自动分组（按堆栈签名）
- 异常趋势图
- 受影响用户数
- 首次/最后出现时间

**实现**:
```csharp
// Catga.Debugger/Errors/ExceptionAggregator.cs
public class ExceptionAggregator
{
    public List<ExceptionGroup> GroupExceptions();
    public ExceptionTrend GetTrend(string exceptionSignature);
}

public class ExceptionGroup
{
    public string Signature { get; set; } // 堆栈哈希
    public string ExceptionType { get; set; }
    public string Message { get; set; }
    public int Count { get; set; }
    public List<string> AffectedCorrelationIds { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}
```

**UI**:
- 异常分组列表
- 趋势图（ECharts）
- 堆栈美化显示
- 相关流跳转

---

### 阶段 5：数据探查（P2 - 有用）⭐⭐⭐

#### 5.1 智能 Payload 查看器
**优先级**: P2
**工作量**: 小（2-3天）

**功能**:
- JSON/XML 格式化展示
- 语法高亮
- 折叠/展开
- 搜索与高亮
- Diff 对比（请求前后）

**实现**:
```csharp
// Catga.Debugger/Data/PayloadViewer.cs
public class PayloadViewer
{
    public string FormatJson(string json);
    public string FormatXml(string xml);
    public DiffResult DiffPayloads(object before, object after);
}
```

**UI**:
- Monaco Editor 集成
- 分屏对比视图
- 高亮差异

---

#### 5.2 数据流图
**优先级**: P2
**工作量**: 中（3-4天）

**功能**:
- 消息流向可视化
- 数据转换追踪
- 状态变更历史

**实现**:
```csharp
// Catga.Debugger/Visualization/DataFlowDiagram.cs
public class DataFlowDiagram
{
    public List<DataFlowNode> Nodes { get; set; }
    public List<DataFlowEdge> Edges { get; set; }
}
```

**UI**:
- Mermaid.js 流程图
- 交互式节点点击

---

### 阶段 6：测试与验证（P2 - 有用）⭐⭐⭐

#### 6.1 流量回放
**优先级**: P2
**工作量**: 大（5-6天）

**功能**:
- 捕获真实流量
- 回放到开发环境
- 修改参数回放
- 批量回放

**实现**:
```csharp
// Catga.Debugger/Replay/TrafficReplayer.cs
public class TrafficReplayer
{
    public async Task<ReplayResult> ReplayMessage(string correlationId);
    public async Task<BatchReplayResult> ReplayBatch(IEnumerable<string> correlationIds);
}
```

**UI**:
- 流量选择器
- 参数编辑器
- 回放进度条
- 结果对比

---

#### 6.2 消息注入器
**优先级**: P2
**工作量**: 小（2-3天）

**功能**:
- 手动构造消息
- 模板库
- 批量生成

**实现**:
```csharp
// Catga.Debugger/Testing/MessageInjector.cs
public class MessageInjector
{
    public async Task<CatgaResult> InjectMessage<TRequest>(TRequest request);
}
```

**UI**:
- 消息编辑器
- 模板管理
- 快速注入按钮

---

### 阶段 7：智能分析（P3 - 锦上添花）⭐⭐⭐

#### 7.1 异常模式识别（AI）
**优先级**: P3
**工作量**: 大（7-10天）

**功能**:
- 识别异常模式
- 预测潜在问题
- 推荐修复方案

**实现**:
```csharp
// Catga.Debugger/AI/PatternRecognition.cs
public class PatternRecognition
{
    public List<AnomalyPattern> DetectPatterns();
    public List<Recommendation> SuggestFixes(ExceptionGroup group);
}
```

---

## 📊 优先级总结

| 阶段 | 功能 | 优先级 | 工作量 | 预计时间 |
|------|------|--------|--------|----------|
| **阶段 1** | 核心调试（断点、监视、栈） | P0 | 大 | 11-15天 |
| **阶段 2** | 性能分析（火焰图、瓶颈） | P0 | 大 | 8-10天 |
| **阶段 3** | 日志与追踪 | P1 | 中 | 7-9天 |
| **阶段 4** | 错误诊断 | P1 | 中 | 3-4天 |
| **阶段 5** | 数据探查 | P2 | 中 | 5-7天 |
| **阶段 6** | 测试与验证 | P2 | 大 | 7-9天 |
| **阶段 7** | 智能分析 | P3 | 大 | 7-10天 |

**总工作量**: 约 48-64天（6-8周）

---

## 🎨 UI 架构升级

### 当前问题
- Alpine.js 过于简单，不适合复杂交互
- 无状态管理
- 无路由系统
- 性能受限（大数据量）

### 建议方案
**保持当前 Alpine.js** + **渐进增强**

**理由**:
1. 已有代码可复用
2. 轻量快速
3. 渐进式添加复杂功能

**增强方案**:
```javascript
// 1. 引入 Pinia（状态管理）
import { createPinia } from 'pinia'

const debuggerStore = defineStore('debugger', {
  state: () => ({
    flows: [],
    breakpoints: [],
    watchExpressions: []
  })
})

// 2. 虚拟滚动（大数据）
import { RecycleScroller } from 'vue-virtual-scroller'

// 3. Monaco Editor（代码编辑）
import * as monaco from 'monaco-editor'
```

---

## 🔧 技术选型

### 前端
- **基础**: Alpine.js 3.x（保持）
- **图表**: ECharts 5.x（火焰图、趋势图）
- **图形**: Mermaid.js（流程图、拓扑图）
- **编辑器**: Monaco Editor（JSON/代码编辑）
- **虚拟化**: Alpine Intersect（轻量级虚拟滚动）

### 后端
- **存储**: 保持 InMemoryEventStore（可选 Redis 持久化）
- **实时通信**: SignalR（已有）
- **序列化**: MemoryPack（高性能）
- **并发**: AsyncLocal + ConcurrentDictionary

---

## 📝 实施建议

### 快速开始（MVP）
**目标**: 2周内完成核心价值

**功能子集**:
1. ✅ 消息断点（基础版）
2. ✅ 变量监视（5个表达式）
3. ✅ 火焰图（CPU）
4. ✅ 慢查询检测

**跳过**:
- 条件断点
- 内存火焰图
- AI 分析
- 流量回放

### 长期规划
- **第1个月**: 阶段 1 + 阶段 2（核心调试 + 性能）
- **第2个月**: 阶段 3 + 阶段 4（日志 + 错误）
- **第3个月**: 阶段 5 + 阶段 6（数据 + 测试）
- **第4个月**: 阶段 7（智能分析）+ 优化

---

## ✅ 验收标准

### 功能完整性
- [ ] 支持断点调试（消息级）
- [ ] 支持变量监视（至少10个表达式）
- [ ] 生成火焰图（CPU + 内存）
- [ ] 检测慢查询（Top 10）
- [ ] 日志实时查看（支持过滤）
- [ ] 异常聚合分析（自动分组）

### 性能标准
- [ ] UI 响应时间 < 100ms
- [ ] 10,000+ 流处理无卡顿
- [ ] 内存占用 < 500MB（生产模式）
- [ ] SignalR 延迟 < 50ms

### 用户体验
- [ ] 界面直观易用
- [ ] 快捷键支持
- [ ] 响应式设计
- [ ] 暗色模式支持

---

## 🎯 最终愿景

**打造业界领先的 .NET 分布式系统调试器**

**核心价值**:
1. **生产可用** - 低开销、高性能
2. **功能完备** - 调试、性能、日志、追踪一体化
3. **易于使用** - 无需配置，开箱即用
4. **智能分析** - AI 辅助诊断
5. **开源生态** - 社区驱动

**对标产品**:
- Seq（日志）
- Application Insights（监控）
- Jaeger（追踪）
- dotTrace（性能）

**差异化**:
- ✅ 零配置集成
- ✅ 分布式 CQRS 原生支持
- ✅ 时间旅行调试
- ✅ 完全开源

---

## 📋 下一步行动

1. **确认优先级**: 与团队/用户确认阶段 1-2 的必要性
2. **技术验证**: 火焰图生成算法验证
3. **UI 原型**: 设计断点面板 Mockup
4. **开始编码**: 从 BreakpointManager 开始

**准备好开始了吗？** 🚀

