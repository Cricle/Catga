# Catga Debugger + Aspire 深度集成计划

**日期**: 2025-10-17
**目标**: 避免重复造轮子，充分利用 Aspire 生态系统

---

## 🔍 代码审查结论

### ✅ 优势（保留）

| 功能 | 当前实现 | 理由 |
|------|---------|------|
| **时间旅行调试** | Catga 独有 | Aspire 不支持，核心价值 |
| **断点调试** | Catga 独有 | 生产级断点系统，Aspire 无 |
| **变量监视** | Catga 独有 | 实时表达式评估，Aspire 无 |
| **调用栈追踪** | Catga 独有 | AsyncLocal 栈帧，Aspire 无 |
| **火焰图** | Catga 独有 | 业务流程火焰图，Aspire 无 |
| **流程回放** | Catga 独有 | 事件回放，Aspire 无 |

### ⚠️ 重复造轮子（需整合）

| 功能 | 当前实现 | Aspire 已有 | 建议 |
|------|---------|------------|------|
| **节点监控** | `NodeRegistry` | Aspire Dashboard Resources | ❌ 删除，使用 Aspire |
| **健康检查** | 自定义 Health API | `.MapDefaultEndpoints()` | ✅ 集成到 Aspire |
| **OpenTelemetry 指标** | `CatgaMetrics` | Aspire Metrics | ✅ 保留但标准化 |
| **分布式追踪** | `CatgaDiagnostics` | Aspire Traces | ✅ 保留但标准化 |
| **日志聚合** | 未实现 | Aspire Logs | ✅ 使用 Aspire |
| **集群统计** | `ClusterStats` | Aspire Dashboard | ❌ 删除，使用 Aspire |

### 🎯 核心问题

1. **`NodeRegistry` 与 Aspire Resources 重复**
   - Aspire 已经有完整的节点发现和监控
   - 不需要自己维护节点列表

2. **集群监控 UI 与 Aspire Dashboard 重复**
   - Aspire Dashboard 已经有完整的资源监控
   - 不需要自己做节点列表页面

3. **健康检查重复**
   - Aspire 已经有标准的健康检查集成
   - 应该直接注册到 Aspire 的健康检查系统

---

## 🎯 重构计划

### 阶段 1: 移除重复功能（高优先级）

#### 1.1 删除节点监控相关代码
```
❌ 删除文件:
- src/Catga.Debugger/Monitoring/NodeInfo.cs
- src/Catga.Debugger/Monitoring/NodeRegistry.cs
- src/Catga.Debugger.AspNetCore/Endpoints/ClusterEndpoints.cs
- src/Catga.Debugger.AspNetCore/wwwroot/debugger/cluster.html

✅ 原因:
- Aspire Dashboard 已经有完整的 Resources 监控
- 显示所有服务实例、健康状态、URL
- 实时更新，无需自己实现
```

#### 1.2 从主页移除集群监控入口
```csharp
// ❌ 删除
<a href="/debugger/cluster.html">🌐 集群监控</a>

// ✅ 替换为
<a href="http://localhost:15888" target="_blank">🌐 Aspire Dashboard</a>
```

---

### 阶段 2: 深度集成 Aspire（中优先级）

#### 2.1 增强健康检查集成
```csharp
// src/Catga.Debugger/HealthChecks/DebuggerHealthCheck.cs
public class DebuggerHealthCheck : IHealthCheck
{
    private readonly IEventStore _eventStore;
    private readonly ReplaySessionManager _sessionManager;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _eventStore.GetStatsAsync();
            var activeSessions = _sessionManager.ActiveSessionsCount;

            var data = new Dictionary<string, object>
            {
                ["event_count"] = stats.TotalEvents,
                ["active_sessions"] = activeSessions,
                ["storage_size_mb"] = stats.TotalEvents * 1024 / 1024 / 1024
            };

            // 检查存储大小
            if (stats.TotalEvents > 1_000_000)
            {
                return HealthCheckResult.Degraded(
                    "Event store size exceeds 1M events",
                    data: data
                );
            }

            // 检查活跃会话
            if (activeSessions > 100)
            {
                return HealthCheckResult.Degraded(
                    "Too many active replay sessions",
                    data: data
                );
            }

            return HealthCheckResult.Healthy("Debugger is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Debugger error", ex);
        }
    }
}
```

#### 2.2 注册到 Aspire 健康检查系统
```csharp
// src/Catga.Debugger/DependencyInjection/DebuggerServiceCollectionExtensions.cs
public static IServiceCollection AddCatgaDebugger(
    this IServiceCollection services,
    Action<ReplayOptions>? configureOptions = null)
{
    // ... 现有代码 ...

    // 注册健康检查（自动在 Aspire Dashboard 显示）
    services.AddHealthChecks()
        .AddCheck<DebuggerHealthCheck>(
            "catga-debugger",
            tags: new[] { "ready", "catga" }
        );

    return services;
}
```

#### 2.3 标准化 OpenTelemetry 集成
```csharp
// src/Catga.Debugger/Observability/CatgaActivitySource.cs
public static class CatgaDiagnostics
{
    // 使用标准命名约定
    public static readonly ActivitySource ActivitySource = new(
        "Catga.Framework",  // 会自动在 Aspire Dashboard 显示
        "1.0.0"
    );

    // 标准 Meter 命名
    public static readonly Meter Meter = new(
        "Catga.Framework",  // 会自动在 Aspire Metrics 显示
        "1.0.0"
    );
}
```

---

### 阶段 3: UI 重构（低优先级）

#### 3.1 在 Aspire Dashboard 中添加 Catga Debugger 链接
```csharp
// examples/OrderSystem.AppHost/Program.cs
var api = builder.AddProject<Projects.OrderSystem_Api>("orderapi")
    .WithHttpEndpoint(port: 5000, name: "http")
    .WithExternalHttpEndpoints()
    .WithEnvironment("CatgaDebugger__Enabled", "true")
    .WithAnnotation(new EndpointAnnotation(
        name: "debugger",
        protocol: "http",
        uriScheme: "http",
        port: 5000,
        path: "/debugger"
    ));
```

**结果**: 在 Aspire Dashboard 的 Resources 页面，OrderAPI 会显示：
```
OrderAPI
├─ Status: ✅ Healthy
├─ Endpoints:
│  ├─ http: http://localhost:5000
│  └─ debugger: http://localhost:5000/debugger  🌟 (Catga Debugger)
└─ Health Checks:
   ├─ self: ✅ Healthy
   └─ catga-debugger: ✅ Healthy (event_count: 1234, active_sessions: 0)
```

#### 3.2 Catga Debugger 主页添加 Aspire 链接
```html
<!-- src/Catga.Debugger.AspNetCore/wwwroot/debugger/index.html -->
<header class="bg-white shadow-sm">
    <div class="max-w-7xl mx-auto px-4 py-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold">🐱 Catga Debugger</h1>
            <div class="flex items-center space-x-4">
                <!-- Aspire Dashboard 快捷链接 -->
                <a href="http://localhost:15888"
                   target="_blank"
                   class="flex items-center px-3 py-1 bg-blue-500 text-white rounded hover:bg-blue-600">
                    <svg class="w-4 h-4 mr-2">...</svg>
                    Aspire Dashboard
                </a>
            </div>
        </div>
    </div>
</header>
```

---

## 📊 新架构对比

### Before（现在）

```
┌─────────────────────────────────────────────┐
│            Catga Debugger UI                 │
│  - 消息流监控 ✅                              │
│  - 时间旅行调试 ✅                            │
│  - 断点调试 ✅                                │
│  - 性能分析 ✅                                │
│  - 集群监控 ⚠️ (重复)                         │
│  - 节点列表 ⚠️ (重复)                         │
│  - 健康检查 ⚠️ (重复)                         │
└─────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│           Aspire Dashboard                   │
│  - Resources ✅                              │
│  - Traces ✅                                 │
│  - Metrics ✅                                │
│  - Logs ✅                                   │
│  - Health ✅                                 │
└─────────────────────────────────────────────┘
```

### After（重构后）

```
┌─────────────────────────────────────────────────────────────────┐
│                      Aspire Dashboard                            │
│  http://localhost:15888                                          │
├─────────────────────────────────────────────────────────────────┤
│  📊 Resources │ 🔍 Traces │ 📈 Metrics │ 📝 Logs │ ❤️ Health    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ orderapi (OrderSystem.Api)              ✅ Healthy         │ │
│  │ ├─ Endpoints:                                              │ │
│  │ │  • http: http://localhost:5000                          │ │
│  │ │  • debugger: http://localhost:5000/debugger 🌟         │ │
│  │ ├─ Health Checks:                                          │ │
│  │ │  • self: ✅ Healthy                                     │ │
│  │ │  • catga-debugger: ✅ Healthy                           │ │
│  │ │    ├─ event_count: 1,234                                │ │
│  │ │    ├─ active_sessions: 0                                │ │
│  │ │    └─ storage_size_mb: 12                               │ │
│  │ └─ Metrics:                                                │ │
│  │    ├─ catga.commands.executed: 1,234                      │ │
│  │    └─ catga.events.published: 5,678                       │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  [点击 "debugger" 链接跳转到 Catga Debugger UI]                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Click
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Catga Debugger UI                             │
│  http://localhost:5000/debugger                                  │
├─────────────────────────────────────────────────────────────────┤
│  [← Aspire Dashboard] 🐱 Catga Debugger                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  🏠 消息流   ⏮️ 时间旅行   🔴 断点调试   🔥 性能分析            │
│                                                                  │
│  ✅ 保留核心功能:                                                │
│  - 时间旅行调试（逐步回放）                                       │
│  - 断点系统（暂停执行）                                          │
│  - 变量监视（实时评估）                                          │
│  - 调用栈追踪（AsyncLocal）                                      │
│  - 火焰图生成（性能分析）                                         │
│                                                                  │
│  ❌ 移除重复功能:                                                │
│  - 集群监控（使用 Aspire Resources）                             │
│  - 节点列表（使用 Aspire Resources）                             │
│  - 健康检查 UI（使用 Aspire Health）                             │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🎯 核心价值定位

### Aspire Dashboard（系统级监控）
- ✅ **多服务编排**: 所有服务统一视图
- ✅ **资源监控**: CPU、内存、网络
- ✅ **分布式追踪**: 跨服务调用链
- ✅ **日志聚合**: 集中日志查看
- ✅ **健康检查**: 服务健康状态

### Catga Debugger（业务级调试）
- ✅ **时间旅行**: 回到过去任意时刻
- ✅ **断点调试**: 暂停业务流程
- ✅ **变量监视**: 实时查看业务数据
- ✅ **流程回放**: 逐步查看事件流
- ✅ **根因分析**: 快速定位问题

**结论**: 两者互补，不冲突！

---

## 📋 实施步骤

### 步骤 1: 删除重复代码 ⏱️ 30分钟
```bash
# 删除文件
rm src/Catga.Debugger/Monitoring/NodeInfo.cs
rm src/Catga.Debugger/Monitoring/NodeRegistry.cs
rm src/Catga.Debugger.AspNetCore/Endpoints/ClusterEndpoints.cs
rm src/Catga.Debugger.AspNetCore/wwwroot/debugger/cluster.html

# 更新服务注册
# 删除 NodeRegistry 注册
# 删除 ClusterEndpoints 映射
```

### 步骤 2: 增强健康检查 ⏱️ 1小时
```bash
# 创建文件
touch src/Catga.Debugger/HealthChecks/DebuggerHealthCheck.cs

# 更新服务注册
# 添加 .AddHealthChecks()
```

### 步骤 3: 标准化 OpenTelemetry ⏱️ 30分钟
```bash
# 更新现有文件
# 确保使用标准命名约定
# 添加更多标签
```

### 步骤 4: 更新文档 ⏱️ 1小时
```bash
# 更新文档
# 强调 Aspire 集成
# 移除集群监控文档
```

### 步骤 5: 更新示例 ⏱️ 30分钟
```bash
# 更新 OrderSystem 示例
# 展示 Aspire + Catga 最佳实践
```

**总时间**: 约 3.5 小时

---

## ✅ 验收标准

1. ✅ 删除所有与 Aspire 重复的功能
2. ✅ Catga 健康检查在 Aspire Dashboard 正确显示
3. ✅ Catga 指标在 Aspire Metrics 正确显示
4. ✅ Catga 追踪在 Aspire Traces 正确显示
5. ✅ 从 Aspire Dashboard 可以直接跳转到 Catga Debugger
6. ✅ 从 Catga Debugger 可以直接跳转到 Aspire Dashboard
7. ✅ 文档清晰说明两者的定位和配合使用

---

## 📚 参考文档

- [.NET Aspire Dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard)
- [Aspire Health Checks](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/health-checks)
- [OpenTelemetry in Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry)

---

## 🎉 结论

**不要重复造轮子！**

- ❌ 删除: 节点监控、集群统计（Aspire 已有）
- ✅ 保留: 时间旅行、断点、变量监视（Catga 独有）
- ✅ 集成: 健康检查、OpenTelemetry（标准化）
- ✅ 互补: Aspire 看全局，Catga 看细节

**最终效果**:
- 用户在 Aspire Dashboard 看系统健康
- 发现问题后，点击链接进入 Catga Debugger 深度调试
- 完美的工作流！🚀

