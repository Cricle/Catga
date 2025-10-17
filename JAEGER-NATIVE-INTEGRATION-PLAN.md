# Catga Jaeger 原生集成计划

## 🎯 **核心理念**

**不要重复造轮子！** 利用 Jaeger + OpenTelemetry 的强大功能，完美展示：
- 分布式事务（Catga）的完整流程
- 命令（Command）执行链路
- 事件（Event）传播路径
- 聚合根（Aggregate）状态变更
- 性能指标和错误信息

---

## 📋 **当前问题分析**

### ❌ **问题 1: Catga.Debugger 库重复造轮子**
- 自己实现了事件存储（IEventStore）
- 自己实现了时间旅行调试（TimeTravelReplay）
- 自己实现了性能分析（PerformanceAnalyzer）
- 自己实现了火焰图（FlameGraphBuilder）
- 自己实现了UI界面

**Jaeger 已经提供了这些功能！**

### ❌ **问题 2: OTEL 集成不完整**
当前只有基础的 Trace：
```csharp
// 只有简单的 Activity
activity.SetTag("request.type", typeof(TRequest).Name);
activity.SetTag("response.type", typeof(TResponse).Name);
```

**应该记录更丰富的信息：**
- Catga 特有的业务语义（事务步骤、补偿逻辑）
- 聚合根状态变更
- 事件发布/订阅关系
- 性能瓶颈数据

### ❌ **问题 3: 缺少语义化的 Span**
- 没有为每个 Catga 步骤创建独立的 Span
- 没有记录补偿逻辑的执行
- 没有区分正向步骤和补偿步骤

---

## 🎯 **目标架构**

### **在 Jaeger 中看到的内容：**

```
HTTP Request: POST /api/orders
  │
  ├─ Command: CreateOrderCommand
  │   ├─ Validate Order
  │   ├─ Reserve Inventory (Catga Step 1)
  │   │   ├─ Publish: InventoryReservedEvent
  │   │   └─ State: inventory.reserved = true
  │   ├─ Process Payment (Catga Step 2)
  │   │   ├─ Publish: PaymentProcessedEvent
  │   │   └─ State: payment.status = "completed"
  │   ├─ Ship Order (Catga Step 3) ❌ FAILED
  │   │   └─ Error: Shipping service unavailable
  │   ├─ [COMPENSATION] Refund Payment
  │   │   └─ Publish: PaymentRefundedEvent
  │   └─ [COMPENSATION] Release Inventory
  │       └─ Publish: InventoryReleasedEvent
  │
  └─ Response: 500 Internal Server Error
```

**每个 Span 包含：**
- Tags: `catga.step`, `catga.type`, `aggregate.id`, `event.type`
- Events: 状态变更、事件发布、错误信息
- Logs: 性能指标、业务日志
- Baggage: CorrelationId、UserId、TenantId

---

## 📐 **实施计划**

### **Phase 1: 清理 - 删除 Catga.Debugger** (30分钟)

#### 1.1 删除项目和文件
```bash
# 删除 Debugger 相关项目
rm -rf src/Catga.Debugger/
rm -rf src/Catga.Debugger.AspNetCore/

# 删除相关文档
rm DEBUGGER-*.md
rm TIME-TRAVEL-*.md
```

#### 1.2 从示例中移除 Debugger 依赖
- `examples/OrderSystem.Api/Program.cs` - 移除 `AddCatgaDebugger()`
- `examples/OrderSystem.ServiceDefaults/Extensions.cs` - 移除 DebuggerHealthCheck
- 所有 `.csproj` - 移除 `Catga.Debugger` 引用

#### 1.3 保留的内容
- `docs/observability/JAEGER-INTEGRATION.md` - 保留并增强
- OpenTelemetry 相关配置 - 保留并增强

---

### **Phase 2: 增强 Catga 主库的 OTEL 集成** (2-3小时)

#### 2.1 创建 Catga 专用的 ActivitySource
```csharp
// src/Catga/Observability/CatgaActivitySource.cs
public static class CatgaDiagnostics
{
    public const string ActivitySourceName = "Catga";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    // Catga 特定的 Tag 名称
    public static class Tags
    {
        public const string CatgaType = "catga.type";              // command | event | catga | aggregate
        public const string CatgaStepId = "catga.step.id";
        public const string CatgaStepName = "catga.step.name";
        public const string CatgaStepType = "catga.step.type";     // forward | compensation
        public const string AggregateId = "aggregate.id";
        public const string AggregateType = "aggregate.type";
        public const string EventType = "event.type";
        public const string EventName = "event.name";
        public const string CommandType = "command.type";
        public const string CorrelationId = "correlation.id";
    }

    // Catga 特定的 Event 名称
    public static class Events
    {
        public const string StateChanged = "catga.state.changed";
        public const string EventPublished = "catga.event.published";
        public const string StepStarted = "catga.step.started";
        public const string StepCompleted = "catga.step.completed";
        public const string StepFailed = "catga.step.failed";
        public const string CompensationStarted = "catga.compensation.started";
        public const string CompensationCompleted = "catga.compensation.completed";
    }
}
```

#### 2.2 增强命令处理的追踪
```csharp
// src/Catga.InMemory/CatgaMediator.cs
public async ValueTask<CatgaResult<TResponse>> SendAsync<TResponse>(
    IRequest<TResponse> request,
    CancellationToken ct = default)
{
    using var activity = CatgaDiagnostics.ActivitySource.StartActivity(
        $"Command: {request.GetType().Name}",
        ActivityKind.Internal);

    activity?.SetTag(CatgaDiagnostics.Tags.CatgaType, "command");
    activity?.SetTag(CatgaDiagnostics.Tags.CommandType, request.GetType().FullName);

    // 如果请求有 CorrelationId
    if (request is IHasCorrelationId hasCorrelation)
    {
        activity?.SetTag(CatgaDiagnostics.Tags.CorrelationId, hasCorrelation.CorrelationId);
        activity?.SetBaggage("correlation.id", hasCorrelation.CorrelationId);
    }

    // 如果请求有 AggregateId
    if (request is IHasAggregateId hasAggregate)
    {
        activity?.SetTag(CatgaDiagnostics.Tags.AggregateId, hasAggregate.AggregateId);
        activity?.SetTag(CatgaDiagnostics.Tags.AggregateType, hasAggregate.AggregateType);
    }

    try
    {
        var result = await ExecutePipelineAsync(request, ct);

        activity?.SetStatus(result.IsSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        if (!result.IsSuccess)
        {
            activity?.SetTag("error", true);
            activity?.SetTag("error.message", result.Error?.Message);
        }

        return result;
    }
    catch (Exception ex)
    {
        activity?.RecordException(ex);
        throw;
    }
}
```

#### 2.3 增强事件发布的追踪
```csharp
// src/Catga.InMemory/CatgaMediator.cs
public async ValueTask PublishAsync<TEvent>(
    TEvent @event,
    CancellationToken ct = default) where TEvent : IEvent
{
    using var activity = CatgaDiagnostics.ActivitySource.StartActivity(
        $"Event: {@event.GetType().Name}",
        ActivityKind.Producer);  // Producer for event publishing

    activity?.SetTag(CatgaDiagnostics.Tags.CatgaType, "event");
    activity?.SetTag(CatgaDiagnostics.Tags.EventType, @event.GetType().FullName);
    activity?.SetTag(CatgaDiagnostics.Tags.EventName, @event.GetType().Name);

    // 记录事件发布
    activity?.AddEvent(new ActivityEvent(
        CatgaDiagnostics.Events.EventPublished,
        tags: new ActivityTagsCollection
        {
            { "event.data", System.Text.Json.JsonSerializer.Serialize(@event) }
        }
    ));

    await base.PublishAsync(@event, ct);
}
```

#### 2.4 为 Catga 事务的每个步骤创建 Span
```csharp
// src/Catga/Distributed/ICatgaStep.cs - 新增接口
public interface ITrackedCatgaStep<TContext> : ICatgaStep<TContext>
{
    string StepName { get; }  // 用于 Span 名称
}

// src/Catga/Distributed/CatgaCoordinator.cs
public async Task<CatgaResult> ExecuteAsync(TContext context, CancellationToken ct)
{
    using var activity = CatgaDiagnostics.ActivitySource.StartActivity(
        "Catga Transaction",
        ActivityKind.Internal);

    activity?.SetTag(CatgaDiagnostics.Tags.CatgaType, "catga");
    activity?.SetTag("catga.steps.count", _steps.Count);

    var executedSteps = new List<int>();

    try
    {
        // 执行每个步骤
        for (int i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var stepName = step is ITrackedCatgaStep<TContext> tracked
                ? tracked.StepName
                : $"Step {i + 1}";

            using var stepActivity = CatgaDiagnostics.ActivitySource.StartActivity(
                stepName,
                ActivityKind.Internal);

            stepActivity?.SetTag(CatgaDiagnostics.Tags.CatgaStepId, i);
            stepActivity?.SetTag(CatgaDiagnostics.Tags.CatgaStepName, stepName);
            stepActivity?.SetTag(CatgaDiagnostics.Tags.CatgaStepType, "forward");

            stepActivity?.AddEvent(new ActivityEvent(CatgaDiagnostics.Events.StepStarted));

            try
            {
                await step.ExecuteAsync(context, ct);
                executedSteps.Add(i);

                stepActivity?.AddEvent(new ActivityEvent(CatgaDiagnostics.Events.StepCompleted));
                stepActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                stepActivity?.AddEvent(new ActivityEvent(
                    CatgaDiagnostics.Events.StepFailed,
                    tags: new ActivityTagsCollection { { "exception", ex.Message } }
                ));
                stepActivity?.RecordException(ex);
                stepActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        return CatgaResult.Success();
    }
    catch (Exception ex)
    {
        activity?.SetTag("error", true);
        activity?.SetTag("compensation.triggered", true);

        // 补偿逻辑
        await CompensateAsync(context, executedSteps, ct);

        return CatgaResult.Failure(CatgaError.Validation(ex.Message));
    }
}

private async Task CompensateAsync(TContext context, List<int> executedSteps, CancellationToken ct)
{
    using var compensationActivity = CatgaDiagnostics.ActivitySource.StartActivity(
        "Catga Compensation",
        ActivityKind.Internal);

    compensationActivity?.SetTag("catga.compensation.steps", executedSteps.Count);
    compensationActivity?.AddEvent(new ActivityEvent(CatgaDiagnostics.Events.CompensationStarted));

    // 反向补偿
    for (int i = executedSteps.Count - 1; i >= 0; i--)
    {
        var stepIndex = executedSteps[i];
        var step = _steps[stepIndex];
        var stepName = step is ITrackedCatgaStep<TContext> tracked
            ? tracked.StepName
            : $"Step {stepIndex + 1}";

        using var stepActivity = CatgaDiagnostics.ActivitySource.StartActivity(
            $"[COMPENSATION] {stepName}",
            ActivityKind.Internal);

        stepActivity?.SetTag(CatgaDiagnostics.Tags.CatgaStepId, stepIndex);
        stepActivity?.SetTag(CatgaDiagnostics.Tags.CatgaStepName, stepName);
        stepActivity?.SetTag(CatgaDiagnostics.Tags.CatgaStepType, "compensation");

        try
        {
            await step.CompensateAsync(context, ct);
            stepActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            stepActivity?.RecordException(ex);
            stepActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // 继续补偿其他步骤
        }
    }

    compensationActivity?.AddEvent(new ActivityEvent(CatgaDiagnostics.Events.CompensationCompleted));
}
```

#### 2.5 聚合根状态变更追踪
```csharp
// src/Catga/Aggregates/AggregateRoot.cs
protected void RaiseEvent<TEvent>(TEvent @event) where TEvent : class
{
    _uncommittedEvents.Add(@event);

    // 记录状态变更到当前 Activity
    Activity.Current?.AddEvent(new ActivityEvent(
        CatgaDiagnostics.Events.StateChanged,
        tags: new ActivityTagsCollection
        {
            { "aggregate.id", Id },
            { "aggregate.type", GetType().Name },
            { "aggregate.version", Version + 1 },
            { "event.type", typeof(TEvent).Name },
            { "event.data", System.Text.Json.JsonSerializer.Serialize(@event) }
        }
    ));
}
```

---

### **Phase 3: 配置 OpenTelemetry Exporter** (30分钟)

#### 3.1 更新 ServiceDefaults
```csharp
// examples/OrderSystem.ServiceDefaults/Extensions.cs
public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
{
    builder.AddOpenTelemetryExporters();

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        activity.SetTag("http.request.id", request.HttpContext.TraceIdentifier);
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                // ✅ 关键：添加 Catga 的 ActivitySource
                .AddSource("Catga")
                .AddSource("Catga.*");
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Catga.*");
        });

    return builder;
}

private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
{
    var useOtlpExporter = !string.IsNullOrWhiteSpace(
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    if (useOtlpExporter)
    {
        // 导出到 Jaeger (via OTLP)
        builder.Services.AddOpenTelemetry()
            .UseOtlpExporter();
    }

    return builder;
}
```

#### 3.2 AppHost 配置 Jaeger
```csharp
// examples/OrderSystem.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// 添加 Jaeger 服务
var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "jaeger-ui")  // UI
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")        // OTLP gRPC
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otlp-http");       // OTLP HTTP

// API 服务引用 Jaeger
var apiService = builder.AddProject<Projects.OrderSystem_Api>("api")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")  // Jaeger OTLP endpoint
    .WithReference(jaeger);

builder.Build().Run();
```

---

### **Phase 4: 增强 Jaeger 查询体验** (1小时)

#### 4.1 自定义 Tag 索引
在 Jaeger UI 中可以按以下字段搜索：
- `catga.type` = command | event | catga | aggregate
- `catga.step.name` = "Reserve Inventory"
- `aggregate.id` = "order-12345"
- `correlation.id` = "trace-abc-123"

#### 4.2 创建 Catga 特定的 Dashboard
在 Grafana 中创建仪表板，展示：
- Catga 事务成功率
- 补偿执行频率
- 每个步骤的平均耗时
- 聚合根状态变更频率

#### 4.3 告警规则
```yaml
# prometheus-alerts.yml
groups:
  - name: catga
    rules:
      - alert: HighCatgaCompensationRate
        expr: rate(catga_compensation_total[5m]) > 0.1
        annotations:
          summary: "Catga 补偿率过高"
          description: "5分钟内补偿率 > 10%"

      - alert: SlowCatgaStep
        expr: histogram_quantile(0.95, catga_step_duration_seconds) > 5
        annotations:
          summary: "Catga 步骤执行缓慢"
          description: "P95 耗时 > 5秒"
```

---

### **Phase 5: 文档更新** (30分钟)

#### 5.1 更新 README.md
- 移除 Debugger 相关内容
- 强调 Jaeger 原生集成
- 添加 Jaeger UI 截图

#### 5.2 创建 JAEGER-BEST-PRACTICES.md
- 如何在 Jaeger 中查看 Catga 事务
- 如何追踪补偿逻辑
- 如何分析性能瓶颈
- 如何设置告警

#### 5.3 更新示例文档
- OrderSystem 示例中添加 Jaeger 访问说明
- 添加典型 Trace 的截图和解释

---

## 📊 **对比：之前 vs 之后**

| 功能 | 之前 (Catga.Debugger) | 之后 (Jaeger 集成) |
|------|----------------------|-------------------|
| **时间旅行调试** | 自己实现 InMemoryEventStore | ❌ 删除 - Jaeger 有完整历史 |
| **性能分析** | 自己实现 PerformanceAnalyzer | ✅ Jaeger 火焰图 + Grafana |
| **火焰图** | 自己实现 FlameGraphBuilder | ✅ Jaeger 原生火焰图 |
| **UI** | 自己的 Vue UI | ✅ Jaeger UI (专业、强大) |
| **事务流程** | 需要手动拼接 | ✅ Jaeger 自动展示 Span 树 |
| **补偿逻辑** | 看不到 | ✅ 清晰标记 [COMPENSATION] |
| **分布式追踪** | 不支持 | ✅ 完美支持多服务 |
| **指标监控** | 基础统计 | ✅ Prometheus + Grafana |
| **告警** | 不支持 | ✅ Grafana Alerts |
| **搜索/过滤** | 基础功能 | ✅ 强大的查询语言 |

---

## 🚀 **实施步骤**

### **立即执行 (30分钟)**
1. 删除 `Catga.Debugger` 和 `Catga.Debugger.AspNetCore` 项目
2. 从所有 `.csproj` 中移除引用
3. 从示例中移除 Debugger 调用

### **核心实现 (2-3小时)**
4. 增强 `CatgaDiagnostics` 和 Tag 常量
5. 在 `CatgaMediator` 中添加详细的 Activity
6. 在 `CatgaCoordinator` 中为每个步骤创建 Span
7. 在 `AggregateRoot` 中记录状态变更

### **配置和验证 (1小时)**
8. 配置 OpenTelemetry 导出到 Jaeger
9. 在 AppHost 中添加 Jaeger 容器
10. 运行示例，验证 Jaeger UI 中的数据

### **文档和优化 (1小时)**
11. 更新 README 和文档
12. 创建 Jaeger 最佳实践指南
13. 添加 Grafana Dashboard

---

## ✅ **验收标准**

### 在 Jaeger UI 中应该能看到：

1. **完整的请求链路**
   - HTTP Request → Command → Catga Transaction → Events

2. **Catga 事务的每个步骤**
   - Step 1: Reserve Inventory
   - Step 2: Process Payment
   - Step 3: Ship Order (Failed)
   - [COMPENSATION] Refund Payment
   - [COMPENSATION] Release Inventory

3. **详细的 Tags**
   - `catga.type`, `catga.step.name`, `aggregate.id`
   - `correlation.id`, `event.type`

4. **Events (日志点)**
   - `catga.step.started`
   - `catga.step.completed`
   - `catga.state.changed`
   - `catga.event.published`

5. **性能指标**
   - 每个 Span 的耗时
   - 自动的火焰图

---

## 📦 **删除的文件清单**

```
src/Catga.Debugger/                       # 整个项目
src/Catga.Debugger.AspNetCore/            # 整个项目
DEBUGGER-PAGES-IMPLEMENTATION-PLAN.md
DEBUGGER-IMPLEMENTATION-STATUS.md
DEBUGGER-PRODUCTION-SAFE.md
DEBUGGER-COMPLETE.md
TIME-TRAVEL-COMPLETE.md
DEBUGGER-FINAL-SUMMARY.md
src/Catga.Debugger.AspNetCore/wwwroot/debugger/  # UI 文件
```

---

## 🎯 **最终目标**

用户只需：
1. 启动 OrderSystem
2. 打开 Jaeger UI (`http://localhost:16686`)
3. 搜索 `catga.type=command` 或 `catga.type=catga`
4. 点击任一 Trace，完美看到：
   - 整个事务流程
   - 每个步骤的耗时
   - 状态变更历史
   - 补偿逻辑执行
   - 事件发布关系

**不需要任何自定义 Debugger UI！**

---

## ❓ **确认执行？**

请选择：

**A. 立即执行全部计划** (推荐)
- 删除 Catga.Debugger
- 增强 OTEL 集成
- 配置 Jaeger
- 更新文档

**B. 分阶段执行**
- 先增强 OTEL，保留 Debugger
- 验证 Jaeger 足够后再删除 Debugger

**C. 修改计划**
- 你有其他想法或要求

---

**我的建议：选择 A，一次性完成转型！** 🚀

Jaeger + OpenTelemetry 是行业标准，功能远超我们自己实现的 Debugger。

