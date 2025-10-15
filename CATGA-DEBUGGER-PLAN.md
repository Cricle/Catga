# Catga.Debugger - 全方位实时调试诊断系统

**版本**: v1.0  
**创建日期**: 2025-10-15  
**目标**: 打造业界最强大的 CQRS/Event Sourcing 调试诊断平台

---

## 🎯 核心目标

### 1. 全流程追踪
- ✅ 消息流完整链路追踪
- ✅ 跨服务分布式追踪
- ✅ 事件溯源回放
- ✅ 聚合状态演进历史

### 2. 全特性支持
- ✅ Command/Query 执行详情
- ✅ Event 发布和订阅跟踪
- ✅ Saga/Catga 事务状态机
- ✅ Read Model 投影构建过程
- ✅ Pipeline Behavior 执行链

### 3. 全方位诊断
- ✅ 性能分析（耗时、吞吐量）
- ✅ 内存分析（GC、分配）
- ✅ 并发分析（死锁检测）
- ✅ 错误分析（异常聚合）
- ✅ 健康检查（实时监控）

### 4. ASP.NET Core UI
- ✅ 实时 Web Dashboard
- ✅ 交互式调试控制台
- ✅ 可视化流程图
- ✅ 性能火焰图

---

## 📦 项目结构

```
src/Catga.Debugger/
├── Core/
│   ├── DebugSession.cs           # 调试会话管理
│   ├── MessageFlowTracker.cs     # 消息流追踪（增强版）
│   ├── PerformanceRecorder.cs    # 性能记录器
│   ├── StateSnapshotManager.cs   # 状态快照管理
│   └── DebugEventStore.cs        # 调试事件存储
│
├── Pipeline/
│   ├── DebugPipelineBehavior.cs  # 调试管道行为（增强版）
│   ├── PerformanceBehavior.cs    # 性能分析行为
│   ├── TracingBehavior.cs        # 追踪行为
│   └── DiagnosticBehavior.cs     # 诊断行为
│
├── Analyzers/
│   ├── PerformanceAnalyzer.cs    # 性能分析器
│   ├── ConcurrencyAnalyzer.cs    # 并发分析器
│   ├── MemoryAnalyzer.cs         # 内存分析器
│   └── ErrorAnalyzer.cs          # 错误分析器
│
├── Visualizers/
│   ├── FlowVisualizer.cs         # 流程可视化
│   ├── StateVisualizer.cs        # 状态可视化
│   ├── GraphBuilder.cs           # 图形构建器
│   └── TimelineBuilder.cs        # 时间线构建器
│
├── Storage/
│   ├── IDebugStorage.cs          # 调试数据存储接口
│   ├── InMemoryDebugStorage.cs   # 内存存储
│   ├── RedisDebugStorage.cs      # Redis 存储
│   └── FileDebugStorage.cs       # 文件存储
│
├── AspNetCore/
│   ├── DebugDashboardMiddleware.cs   # Dashboard 中间件
│   ├── DebugApiController.cs         # REST API
│   ├── DebugWebSocketHandler.cs      # WebSocket 实时推送
│   └── wwwroot/
│       ├── index.html                # 主页面
│       ├── dashboard.js              # Dashboard 逻辑
│       ├── visualizer.js             # 可视化组件
│       └── styles.css                # 样式
│
└── DependencyInjection/
    └── DebuggerServiceExtensions.cs  # DI 注册

tests/Catga.Debugger.Tests/
└── ... (完整测试套件)

examples/DebuggerDemo/
└── ... (完整示例)
```

---

## 🔧 核心功能设计

### 1. 调试会话管理

```csharp
/// <summary>Debug session - isolate debugging data by session</summary>
public sealed class DebugSession : IDisposable
{
    public string SessionId { get; }
    public DateTime StartTime { get; }
    public DebugSessionOptions Options { get; }
    
    // 会话级别的追踪器
    public IMessageFlowTracker FlowTracker { get; }
    public IPerformanceRecorder PerformanceRecorder { get; }
    public IStateSnapshotManager SnapshotManager { get; }
    
    // 实时数据流
    public IObservable<DebugEvent> EventStream { get; }
    
    // 控制方法
    public Task PauseAsync();
    public Task ResumeAsync();
    public Task StepAsync();
    public Task<Snapshot> CaptureSnapshotAsync();
}
```

### 2. 增强的消息流追踪

```csharp
/// <summary>Enhanced message flow tracker with deep insights</summary>
public sealed class MessageFlowTracker : IMessageFlowTracker
{
    // 流程跟踪
    public FlowContext BeginFlow(string correlationId, FlowType type);
    public void RecordStep(string correlationId, StepInfo step);
    public void RecordState(string correlationId, object state);
    public void RecordPerformance(string correlationId, PerformanceMetrics metrics);
    public FlowSummary EndFlow(string correlationId);
    
    // 查询和分析
    public FlowContext? GetFlow(string correlationId);
    public IEnumerable<FlowContext> GetActiveFlows();
    public IEnumerable<FlowContext> QueryFlows(FlowQuery query);
    public FlowStatistics GetStatistics(TimeRange? range = null);
    
    // 实时推送
    public IObservable<FlowEvent> FlowEvents { get; }
}

public class FlowContext
{
    public string CorrelationId { get; set; }
    public FlowType Type { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    
    // 步骤链
    public List<StepInfo> Steps { get; } = new();
    
    // 状态快照
    public List<StateSnapshot> Snapshots { get; } = new();
    
    // 性能数据
    public PerformanceMetrics Performance { get; set; }
    
    // 错误信息
    public ExceptionInfo? Exception { get; set; }
    
    // 元数据
    public Dictionary<string, object> Metadata { get; } = new();
}

public class StepInfo
{
    public int Sequence { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public DateTime Timestamp { get; set; }
    public TimeSpan Duration { get; set; }
    public StepStatus Status { get; set; }
    public object? Input { get; set; }
    public object? Output { get; set; }
    public Dictionary<string, object> Metadata { get; } = new();
}
```

### 3. 性能分析器

```csharp
/// <summary>Performance analyzer with zero-allocation tracking</summary>
public sealed class PerformanceRecorder
{
    // 记录性能指标
    public void RecordExecution(string operation, TimeSpan duration, long allocatedBytes);
    public void RecordThroughput(string operation, int count, TimeSpan window);
    public void RecordConcurrency(string operation, int concurrentCount);
    
    // 性能报告
    public PerformanceReport GetReport(TimeRange? range = null);
    public IEnumerable<PerformanceHotspot> GetHotspots(int topN = 10);
    public IEnumerable<PerformanceAnomaly> DetectAnomalies();
    
    // 实时监控
    public IObservable<PerformanceMetrics> MetricsStream { get; }
}

public class PerformanceMetrics
{
    // 耗时
    public TimeSpan Duration { get; set; }
    public TimeSpan AverageDuration { get; set; }
    public TimeSpan P95Duration { get; set; }
    public TimeSpan P99Duration { get; set; }
    
    // 吞吐量
    public double RequestsPerSecond { get; set; }
    public double EventsPerSecond { get; set; }
    
    // 内存
    public long AllocatedBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    
    // 并发
    public int ConcurrentOperations { get; set; }
    public int PeakConcurrency { get; set; }
    
    // 错误率
    public double ErrorRate { get; set; }
    public int TotalErrors { get; set; }
}
```

### 4. 状态快照管理

```csharp
/// <summary>State snapshot manager for time-travel debugging</summary>
public sealed class StateSnapshotManager
{
    // 捕获快照
    public Task<Snapshot> CaptureAsync<TAggregate>(string aggregateId) where TAggregate : IAggregateRoot;
    public Task<Snapshot> CaptureAtVersionAsync<TAggregate>(string aggregateId, long version);
    
    // 快照查询
    public Task<Snapshot?> GetSnapshotAsync(string snapshotId);
    public Task<IEnumerable<Snapshot>> GetSnapshotsAsync(string aggregateId);
    public Task<Snapshot?> GetSnapshotAtTimeAsync(string aggregateId, DateTime timestamp);
    
    // 快照对比
    public SnapshotDiff CompareSnapshots(Snapshot before, Snapshot after);
    
    // 时间旅行
    public Task<TAggregate> RehydrateAtVersionAsync<TAggregate>(string aggregateId, long version);
    public Task<TAggregate> RehydrateAtTimeAsync<TAggregate>(string aggregateId, DateTime timestamp);
}

public class Snapshot
{
    public string SnapshotId { get; set; }
    public string AggregateId { get; set; }
    public string AggregateType { get; set; }
    public long Version { get; set; }
    public DateTime Timestamp { get; set; }
    public object State { get; set; }
    public Dictionary<string, object> Metadata { get; } = new();
}
```

### 5. 可视化引擎

```csharp
/// <summary>Flow visualizer - generate visual representations</summary>
public sealed class FlowVisualizer
{
    // 生成流程图
    public FlowGraph GenerateFlowGraph(FlowContext flow);
    public FlowGraph GenerateCatgaGraph(CatgaTransaction transaction);
    
    // 生成时间线
    public Timeline GenerateTimeline(FlowContext flow);
    public Timeline GenerateEventTimeline(string aggregateId);
    
    // 生成依赖图
    public DependencyGraph GenerateDependencyGraph(IEnumerable<FlowContext> flows);
    
    // 导出格式
    public string ExportAsMermaid(FlowGraph graph);
    public string ExportAsGraphViz(FlowGraph graph);
    public string ExportAsJson(FlowGraph graph);
}

public class FlowGraph
{
    public List<FlowNode> Nodes { get; } = new();
    public List<FlowEdge> Edges { get; } = new();
    public GraphMetadata Metadata { get; set; }
}

public class FlowNode
{
    public string Id { get; set; }
    public string Label { get; set; }
    public NodeType Type { get; set; }
    public NodeStatus Status { get; set; }
    public TimeSpan? Duration { get; set; }
    public Dictionary<string, object> Data { get; } = new();
}
```

---

## 🌐 ASP.NET Core UI Dashboard

### 主要功能

#### 1. 实时监控面板
```
┌─────────────────────────────────────────────────────────────┐
│  Catga Debugger Dashboard                   🔴 LIVE          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Active Flows: 12        Throughput: 1,234/s   Errors: 0    │
│  Avg Latency: 12ms      P95: 45ms             P99: 89ms     │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 📊 Throughput (last 5 min)                             │ │
│  │                                                         │ │
│  │  2000 │           ╱╲                                    │ │
│  │  1500 │        ╱ ╱  ╲  ╱╲                              │ │
│  │  1000 │    ╱╲ ╱      ╲╱  ╲                             │ │
│  │   500 │ ╱ ╱  ╲╱                                        │ │
│  │     0 └────────────────────────────────────────        │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  Recent Flows                                                │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ID          Type      Status    Duration   Started      │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ abc123      Command   ✅ Done    12ms      10:23:45     │ │
│  │ def456      Event     🔄 Active  -         10:23:46     │ │
│  │ ghi789      Query     ✅ Done    8ms       10:23:44     │ │
│  │ jkl012      Catga     🔄 Active  1.2s      10:23:40     │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

#### 2. 流程详情视图
```
┌─────────────────────────────────────────────────────────────┐
│  Flow Details: abc123                                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  📋 Overview                                                 │
│    Type: Command (CreateOrder)                              │
│    Status: ✅ Completed                                     │
│    Duration: 124ms                                          │
│    Started: 2025-10-15 10:23:45.123                         │
│                                                              │
│  🔄 Flow Diagram                                            │
│    ┌─────────┐   ┌──────────┐   ┌──────────┐              │
│    │ Request │──>│ Handler  │──>│ Event    │              │
│    │ (8ms)   │   │ (45ms)   │   │ (12ms)   │              │
│    └─────────┘   └──────────┘   └──────────┘              │
│         │              │               │                    │
│         ↓              ↓               ↓                    │
│    Validation     DB Write        Publish                  │
│                                                              │
│  📊 Steps (3 total)                                         │
│    ✅ Validation         (8ms)    - OK                      │
│    ✅ Handler Execution  (45ms)   - Order created           │
│    ✅ Event Publishing   (12ms)   - OrderCreated published  │
│                                                              │
│  💾 State Snapshots (2)                                     │
│    📷 Before: Version 0                                     │
│    📷 After:  Version 1                                     │
│                                                              │
│  📈 Performance                                             │
│    Allocated: 2.4 KB                                        │
│    GC Collections: 0                                        │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

#### 3. 性能分析视图
```
┌─────────────────────────────────────────────────────────────┐
│  Performance Analysis                                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  🔥 Hotspots (Top 10)                                       │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Operation          Calls   Avg Time   Total   Alloc    │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ CreateOrder        1,234   45ms       55.5s   2.4KB    │ │
│  │ ValidateOrder      1,234   8ms        9.9s    120B     │ │
│  │ SaveToDatabase     1,234   32ms       39.5s   1.2KB    │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  📊 Latency Distribution                                    │
│    ████████████████████ 0-10ms:   45%                      │
│    ██████████          10-50ms:   35%                      │
│    ████                50-100ms:  15%                      │
│    █                   100ms+:     5%                      │
│                                                              │
│  🧠 Memory Profile                                          │
│    Gen0: 12 collections                                     │
│    Gen1: 3 collections                                      │
│    Gen2: 0 collections                                      │
│    Total Allocated: 2.4 MB                                  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### UI 技术栈

```javascript
// 前端技术
- Vanilla JS / Alpine.js (轻量)
- Chart.js (图表)
- Mermaid.js (流程图)
- SignalR (实时通信)
- Tailwind CSS (样式)

// 后端 API
- ASP.NET Core Minimal APIs
- SignalR Hubs
- WebSockets
```

---

## 🔌 集成方式

### 1. 基础集成

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga Debugger
builder.Services.AddCatgaDebugger(options =>
{
    // 基础配置
    options.Enabled = builder.Environment.IsDevelopment();
    options.SessionTimeout = TimeSpan.FromHours(1);
    
    // 追踪配置
    options.TrackMessageFlows = true;
    options.TrackPerformance = true;
    options.TrackStateSnapshots = true;
    options.TrackExceptions = true;
    
    // 性能配置
    options.MaxActiveFlows = 1000;
    options.FlowRetentionTime = TimeSpan.FromMinutes(30);
    
    // 存储配置
    options.UseInMemoryStorage(); // 或 UseRedisStorage() / UseFileStorage()
    
    // 采样配置
    options.SamplingRate = 1.0; // 100% in dev, 0.1 (10%) in prod
});

var app = builder.Build();

// 启用 Debugger Dashboard
app.MapCatgaDebugger("/debug"); // UI 界面在 /debug

// 启用 Debugger API
app.MapCatgaDebuggerApi("/debug-api");

app.Run();
```

### 2. 高级配置

```csharp
builder.Services.AddCatgaDebugger(options =>
{
    // 自定义过滤器
    options.AddFlowFilter(flow => flow.Duration > TimeSpan.FromMilliseconds(100));
    
    // 自定义分析器
    options.AddAnalyzer<CustomPerformanceAnalyzer>();
    
    // 自定义可视化器
    options.AddVisualizer<CustomFlowVisualizer>();
    
    // 事件订阅
    options.OnFlowStarted += (sender, e) => Console.WriteLine($"Flow started: {e.CorrelationId}");
    options.OnFlowCompleted += (sender, e) => Console.WriteLine($"Flow completed: {e.CorrelationId}");
    options.OnPerformanceAnomaly += (sender, e) => Console.WriteLine($"Performance anomaly: {e.Message}");
});
```

### 3. 编程式使用

```csharp
public class OrderService
{
    private readonly IDebugSession _debugSession;
    private readonly IMessageFlowTracker _flowTracker;
    
    public async Task<Order> CreateOrderAsync(CreateOrderCommand command)
    {
        // 开始追踪
        var flow = _flowTracker.BeginFlow(command.CorrelationId, FlowType.Command);
        
        try
        {
            // 记录步骤
            flow.RecordStep("Validation", () => ValidateOrder(command));
            
            // 捕获快照
            var snapshot = await _debugSession.SnapshotManager.CaptureAsync<Order>(command.OrderId);
            
            // 执行业务逻辑
            var order = await CreateOrder(command);
            
            // 记录性能
            flow.RecordPerformance(new PerformanceMetrics { ... });
            
            return order;
        }
        finally
        {
            // 结束追踪
            _flowTracker.EndFlow(command.CorrelationId);
        }
    }
}
```

---

## 📊 数据模型

### 调试事件

```csharp
public abstract class DebugEvent
{
    public string EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public string CorrelationId { get; set; }
}

public class FlowStartedEvent : DebugEvent
{
    public FlowType Type { get; set; }
    public string MessageType { get; set; }
}

public class StepRecordedEvent : DebugEvent
{
    public StepInfo Step { get; set; }
}

public class PerformanceRecordedEvent : DebugEvent
{
    public PerformanceMetrics Metrics { get; set; }
}

public class FlowCompletedEvent : DebugEvent
{
    public FlowSummary Summary { get; set; }
}
```

---

## 🎨 可视化示例

### 1. Mermaid 流程图

```mermaid
graph LR
    A[Request] -->|8ms| B[Validation]
    B -->|45ms| C[Handler]
    C -->|12ms| D[Event]
    D -->|5ms| E[Response]
    
    style B fill:#90EE90
    style C fill:#90EE90
    style D fill:#90EE90
    style E fill:#90EE90
```

### 2. 时间线视图

```
CreateOrder Flow (124ms total)
│
├─ 0ms     Request received
├─ 8ms     ✅ Validation completed
├─ 53ms    ✅ Handler executed
├─ 65ms    ✅ Event published
└─ 124ms   ✅ Response sent

Events:
  ├─ 65ms  OrderCreated published
  └─ 68ms  InventoryReserved received
```

---

## 🚀 实施计划

### Phase 1: 核心基础 (Week 1-2)
- [x] 项目结构搭建
- [ ] DebugSession 实现
- [ ] MessageFlowTracker 增强
- [ ] PerformanceRecorder 实现
- [ ] 基础存储实现 (InMemory)

### Phase 2: 分析器 (Week 3)
- [ ] PerformanceAnalyzer
- [ ] ConcurrencyAnalyzer
- [ ] MemoryAnalyzer
- [ ] ErrorAnalyzer

### Phase 3: 可视化 (Week 4)
- [ ] FlowVisualizer
- [ ] StateVisualizer
- [ ] GraphBuilder
- [ ] TimelineBuilder

### Phase 4: ASP.NET Core UI (Week 5-6)
- [ ] Dashboard 中间件
- [ ] REST API
- [ ] WebSocket 实时推送
- [ ] 前端 UI (HTML/JS/CSS)

### Phase 5: 高级功能 (Week 7)
- [ ] StateSnapshotManager
- [ ] 时间旅行调试
- [ ] 回放功能
- [ ] 导出/导入

### Phase 6: 优化和测试 (Week 8)
- [ ] 性能优化
- [ ] 内存优化
- [ ] 完整测试套件
- [ ] 文档和示例

---

## 📝 API 设计

### REST API Endpoints

```
GET    /debug-api/sessions                    # 获取所有会话
POST   /debug-api/sessions                    # 创建新会话
GET    /debug-api/sessions/{id}               # 获取会话详情
DELETE /debug-api/sessions/{id}               # 删除会话

GET    /debug-api/flows                       # 获取所有流程
GET    /debug-api/flows/{correlationId}       # 获取流程详情
GET    /debug-api/flows/active                # 获取活跃流程
POST   /debug-api/flows/query                 # 查询流程

GET    /debug-api/performance/report          # 性能报告
GET    /debug-api/performance/hotspots        # 性能热点
GET    /debug-api/performance/anomalies       # 性能异常

GET    /debug-api/snapshots/{aggregateId}     # 获取快照列表
GET    /debug-api/snapshots/{id}              # 获取快照详情
POST   /debug-api/snapshots/compare           # 对比快照

GET    /debug-api/visualize/flow/{id}         # 可视化流程
GET    /debug-api/visualize/timeline/{id}     # 可视化时间线
```

### SignalR Hubs

```csharp
public class DebugHub : Hub
{
    // 订阅实时流程事件
    public async Task SubscribeToFlows();
    
    // 订阅性能指标
    public async Task SubscribeToMetrics();
    
    // 控制会话
    public async Task PauseSession(string sessionId);
    public async Task ResumeSession(string sessionId);
}

// 客户端接收
connection.on("FlowStarted", (flow) => { ... });
connection.on("FlowCompleted", (flow) => { ... });
connection.on("MetricsUpdated", (metrics) => { ... });
```

---

## 🎯 性能目标

### 零侵入
- ✅ 开发环境 100% 采样
- ✅ 生产环境可配置采样率 (1-10%)
- ✅ 可完全禁用（零开销）

### 高性能
- ✅ 单个追踪 < 1μs 开销
- ✅ 内存占用 < 10MB (1000 活跃流程)
- ✅ 零 GC 压力（对象池）

### 可扩展
- ✅ 支持 10,000+ 并发流程
- ✅ 实时推送延迟 < 100ms
- ✅ Dashboard 支持 1000+ 并发用户

---

## 📚 文档计划

1. **快速开始** - 5分钟上手
2. **配置指南** - 详细配置说明
3. **UI 使用指南** - Dashboard 使用教程
4. **API 参考** - 完整 API 文档
5. **最佳实践** - 调试技巧和模式
6. **性能调优** - 生产环境优化
7. **扩展开发** - 自定义分析器和可视化器

---

## 🎉 创新特性

### 1. AI 辅助诊断
```csharp
// 未来: AI 分析性能瓶颈
var suggestions = await aiAnalyzer.AnalyzeAsync(flow);
// => "检测到 N+1 查询问题", "建议使用批量操作"
```

### 2. 对比调试
```csharp
// 对比两次执行
var diff = debugger.Compare(flowId1, flowId2);
// => 显示性能差异、状态变化
```

### 3. 压力测试模式
```csharp
// 回放流程进行压测
await debugger.ReplayAsync(flowId, concurrency: 100);
```

---

**状态**: 📝 计划阶段  
**下一步**: 开始 Phase 1 实施

