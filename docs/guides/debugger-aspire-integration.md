# Catga Debugger + .NET Aspire 集成指南

将 Catga 的 Time-Travel Debugger 与 .NET Aspire Dashboard 完美集成。

---

## 📖 概述

Catga Debugger 与 .NET Aspire 天然契合：

| 特性 | Catga Debugger | .NET Aspire Dashboard | 集成优势 |
|------|----------------|----------------------|----------|
| **分布式追踪** | OpenTelemetry | OpenTelemetry | 统一追踪数据 |
| **日志聚合** | 结构化日志 | 日志查看器 | 集中日志查看 |
| **指标监控** | 自定义 Metrics | 指标仪表板 | 实时性能监控 |
| **健康检查** | 自动注册 | 健康状态面板 | 服务状态可视化 |
| **时间旅行** | 独有功能 | - | Catga 独创调试 |

**最佳实践**：在 Aspire Dashboard 查看全局监控，在 Catga Debugger 进行深度调试。

---

## 🚀 快速集成

### 1. AppHost 配置

在 `Program.cs` 中配置 Aspire：

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// ===== 添加 Catga 服务 =====
var api = builder.AddProject<Projects.OrderSystem_Api>("orderapi")
    .WithHttpEndpoint(port: 5000, name: "http")
    .WithExternalHttpEndpoints(); // 暴露给外部访问

// ===== 可选：添加依赖服务 =====
var redis = builder.AddRedis("redis")
    .WithDataVolume();

var nats = builder.AddNats("nats")
    .WithDataVolume();

// 连接依赖
api.WithReference(redis)
   .WithReference(nats);

var app = builder.Build();
await app.RunAsync();
```

### 2. API 项目配置

在 `OrderSystem.Api/Program.cs` 中：

```csharp
var builder = WebApplication.CreateBuilder(args);

// ===== 1. Aspire Service Defaults =====
builder.AddServiceDefaults();  // OpenTelemetry + Health Checks + Service Discovery

// ===== 2. Catga 核心配置 =====
builder.Services.AddCatga()
    .UseMemoryPack()
    .WithDebug()  // 启用原生调试
    .ForDevelopment();

builder.Services.AddInMemoryTransport();

// ===== 3. Catga Debugger 配置 =====
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
    {
        options.Mode = Catga.Debugger.Models.DebuggerMode.Development;
        options.SamplingRate = 1.0;  // 100% sampling in dev
        options.RingBufferCapacity = 10000;
        options.CaptureVariables = true;
        options.CaptureCallStacks = true;
        options.CaptureMemoryState = false;  // 内存快照（可选）
    });
}

// ===== 4. 自动注册 =====
builder.Services.AddGeneratedHandlers();
builder.Services.AddGeneratedServices();

var app = builder.Build();

// ===== 5. Aspire 默认端点 =====
app.MapDefaultEndpoints();  // /health, /health/live, /health/ready

// ===== 6. Catga Debugger UI =====
if (app.Environment.IsDevelopment())
{
    app.MapCatgaDebugger("/debug");
    // UI: http://localhost:5000/debug
    // API: http://localhost:5000/debug-api/*
}

// ===== 7. 应用端点 =====
app.MapCatgaRequest<CreateOrderCommand, OrderCreatedResult>("/api/orders")
    .WithName("CreateOrder")
    .WithTags("Orders");

app.Run();
```

---

## 📊 集成架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    .NET Aspire Dashboard                        │
│  http://localhost:15888                                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  📊 Traces   📈 Metrics   📝 Logs   ❤️ Health   🏗️ Resources   │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ orderapi (OrderSystem.Api)                               │  │
│  │ ├─ Status: ✅ Healthy                                    │  │
│  │ ├─ Traces: 1,234 spans (last 1h)                        │  │
│  │ ├─ Logs: 5,678 entries                                  │  │
│  │ ├─ Endpoints:                                            │  │
│  │ │   • http://localhost:5000                             │  │
│  │ │   • http://localhost:5000/debug (Catga Debugger) 🌟  │  │
│  │ └─ Dependencies: redis, nats                             │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ redis (Redis Cache)                                      │  │
│  │ └─ Status: ✅ Healthy                                    │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ nats (NATS Messaging)                                    │  │
│  │ └─ Status: ✅ Healthy                                    │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Click "Catga Debugger" link
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Catga Debugger UI (Vue 3)                    │
│  http://localhost:5000/debug                                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  🏠 Dashboard   📊 Flows   🔍 Flow Detail   ⏪ Replay          │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Time-Travel Replay - CreateOrderCommand (5分钟前)        │  │
│  │                                                            │  │
│  │  Timeline: ━━━━━━━●━━━━━━━━━━━━━━━━━━━━━━━━━━━━━         │  │
│  │            ^                                               │  │
│  │            └─ You are here (BeforeExecution)              │  │
│  │                                                            │  │
│  │  Variables:                                                │  │
│  │  ├─ CustomerId: "CUST-001"                                │  │
│  │  ├─ Items: [2 items] ▶                                    │  │
│  │  ├─ ShippingAddress: "123 Main St"                        │  │
│  │  └─ PaymentMethod: "Alipay"                               │  │
│  │                                                            │  │
│  │  Call Stack:                                               │  │
│  │  1. CreateOrderHandler.HandleAsync()       (Current)      │  │
│  │  2. InboxBehavior.HandleAsync()                           │  │
│  │  3. ValidationBehavior.HandleAsync()                      │  │
│  │  4. CatgaMediator.SendAsync()                             │  │
│  │                                                            │  │
│  │  [◀ Step Back]  [▶ Step Forward]  [⏸ Pause]  [⏹ Stop]   │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔗 URL 快速访问

启动 Aspire AppHost 后，你将拥有以下 URL：

| 名称 | URL | 用途 |
|------|-----|------|
| **Aspire Dashboard** | http://localhost:15888 | 全局监控面板 |
| **OrderSystem API** | http://localhost:5000 | API 端点 |
| **Swagger UI** | http://localhost:5000/swagger | API 文档 |
| **Catga Debugger UI** 🌟 | http://localhost:5000/debug | 时间旅行调试 |
| **Health Check** | http://localhost:5000/health | 健康状态 |
| **Live Check** | http://localhost:5000/health/live | 存活探测 |
| **Ready Check** | http://localhost:5000/health/ready | 就绪探测 |

---

## 📈 监控和追踪

### 1. OpenTelemetry 追踪

Catga 自动为所有命令/查询/事件生成追踪 Span：

```
Trace: CreateOrder (200ms)
├─ catga.mediator.send (180ms)
│  ├─ catga.inbox.check (5ms)
│  ├─ catga.validation (10ms)
│  ├─ catga.handler.execute (150ms)  ← CreateOrderHandler
│  │  ├─ db.query (20ms)
│  │  ├─ catga.event.publish (30ms)  ← OrderCreatedEvent
│  │  │  ├─ SendNotificationHandler (10ms)
│  │  │  └─ AuditOrderHandler (5ms)
│  │  └─ catga.outbox.save (10ms)
│  └─ catga.idempotency.save (5ms)
└─ http.response (20ms)
```

**在 Aspire Dashboard 中查看**：
1. 打开 `Traces` 标签
2. 筛选 `service.name = orderapi`
3. 点击任意 Trace 查看详细 Span 树

### 2. 自定义指标

Catga 自动发布以下 Metrics：

```csharp
// 命令处理计数
catga.mediator.commands.count{handler="CreateOrderHandler"}

// 命令处理延迟
catga.mediator.commands.duration{handler="CreateOrderHandler", p50=1ms, p99=5ms}

// 事件发布计数
catga.mediator.events.count{event="OrderCreatedEvent"}

// 错误率
catga.mediator.errors.count{handler="CreateOrderHandler", error_type="ValidationException"}

// Debugger 指标
catga.debugger.events_captured.count
catga.debugger.replay_sessions.count
catga.debugger.storage_size_bytes
```

**在 Aspire Dashboard 中查看**：
1. 打开 `Metrics` 标签
2. 选择 `orderapi` 服务
3. 查看实时图表

### 3. 结构化日志

Catga 使用 `LoggerMessage` Source Generator 生成高性能日志：

```csharp
// 自动包含 CorrelationId, TraceId, SpanId
[LoggerMessage(LogLevel.Information, "Processing command {CommandType} for {CorrelationId}")]
partial void LogCommandProcessing(string commandType, string correlationId);
```

**在 Aspire Dashboard 中查看**：
1. 打开 `Logs` 标签
2. 筛选 `service.name = orderapi`
3. 使用 `CorrelationId` 关联所有相关日志

---

## 🔍 调试工作流

### 场景 1：生产问题回溯

1. **用户报告**：订单创建失败（5 分钟前）
2. **Aspire Dashboard**：
   - 查看 `Traces` - 找到失败的 CreateOrder Trace
   - 记录 `CorrelationId`: `abc123`
3. **Catga Debugger**：
   - 打开 http://localhost:5000/debug/flows/abc123
   - 点击 `Replay` - 回到 5 分钟前
   - **时间旅行** - 逐步查看每个变量、调用栈
   - 发现：库存服务返回了错误状态码
4. **修复**：添加库存服务的错误处理和重试逻辑

### 场景 2：性能优化

1. **Aspire Dashboard**：
   - `Metrics` 显示 CreateOrder p99 延迟 > 500ms
2. **Catga Debugger**：
   - 查看最慢的 10 个流程
   - 发现：大部分时间消耗在数据库查询
   - **微观回放** - 查看具体的 SQL 查询和参数
3. **优化**：添加缓存层，延迟降至 50ms

### 场景 3：事件流调试

1. **问题**：OrderCreatedEvent 触发了 6 个 Handler，但邮件未发送
2. **Catga Debugger**：
   - 打开 `Flow Detail` - 查看事件发布树
   - 发现：`SendEmailHandler` 在 Handler 列表中，但未执行
   - **回放** - 查看 Handler 注册和调用过程
   - 发现：`SendEmailHandler` 抛出了 `ConfigurationException`
3. **修复**：添加邮件服务配置验证

---

## 🎯 最佳实践

### 1. 分层监控策略

| 层级 | 工具 | 用途 |
|------|------|------|
| **系统级** | Aspire Dashboard | 服务健康、全局追踪、资源监控 |
| **应用级** | Catga Debugger | 业务流程、变量快照、时间旅行 |
| **基础设施** | Prometheus/Grafana | 长期指标、告警 |

### 2. 环境配置

```csharp
// 开发环境：全功能
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
    {
        options.SamplingRate = 1.0;  // 100% 采样
        options.CaptureVariables = true;
        options.CaptureCallStacks = true;
        options.CaptureMemoryState = true;
    });
}

// 预生产环境：采样调试
else if (builder.Environment.IsStaging())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
    {
        options.Mode = DebuggerMode.Production;
        options.SamplingRate = 0.1;  // 10% 采样
        options.CaptureVariables = true;
        options.CaptureCallStacks = false;  // 关闭调用栈（性能）
        options.CaptureMemoryState = false;
    });
}

// 生产环境：最小化开销
else
{
    builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
    {
        options.Mode = DebuggerMode.Production;
        options.SamplingRate = 0.01;  // 1% 采样
        options.CaptureVariables = false;  // 只捕获事件流
        options.CaptureCallStacks = false;
        options.CaptureMemoryState = false;
    });
}
```

### 3. 健康检查配置

```csharp
// Catga 自动注册健康检查
builder.Services.AddCatga()
    .WithHealthChecks(healthChecks =>
    {
        healthChecks.AddCheck<CatgaMediatorHealthCheck>("catga-mediator");
        healthChecks.AddCheck<DebuggerStorageHealthCheck>("catga-debugger");
    });

// Aspire 自动发现并显示
```

### 4. 关联 ID 传播

Catga 自动传播 `CorrelationId` 和 `TraceId`：

```csharp
// 请求进入
HTTP Header: X-Correlation-Id = abc123

// Catga 自动传播
CreateOrderCommand.CorrelationId = abc123
Activity.TraceId = abc123  // OpenTelemetry

// 所有日志、追踪、调试数据共享同一 ID
```

---

## 📊 性能影响

| 功能 | 开发环境 | 生产环境（采样率 1%） |
|------|----------|---------------------|
| **CPU 开销** | +2-3% | < 0.01% |
| **内存开销** | +50MB | +5MB |
| **延迟增加** | +0.1ms | < 0.01ms |
| **网络带宽** | +10KB/req | +100B/req |

**结论**：生产环境影响可忽略不计。

---

## 🔧 高级配置

### 1. 自定义健康检查

```csharp
public class CatgaDebuggerHealthCheck : IHealthCheck
{
    private readonly IEventStore _eventStore;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _eventStore.GetStatisticsAsync(cancellationToken);

            if (stats.EventCount > 1_000_000)
            {
                return HealthCheckResult.Degraded(
                    "Event store size exceeds 1M events",
                    data: new Dictionary<string, object>
                    {
                        ["event_count"] = stats.EventCount,
                        ["storage_size_mb"] = stats.StorageSizeMB
                    });
            }

            return HealthCheckResult.Healthy("Debugger is operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Debugger storage error", ex);
        }
    }
}
```

### 2. 自定义指标

```csharp
public class OrderMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _ordersCreated;
    private readonly Histogram<double> _orderAmount;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("OrderSystem");
        _ordersCreated = _meter.CreateCounter<long>("orders.created");
        _orderAmount = _meter.CreateHistogram<double>("orders.amount");
    }

    public void RecordOrderCreated(decimal amount)
    {
        _ordersCreated.Add(1);
        _orderAmount.Record((double)amount);
    }
}
```

### 3. 自定义追踪标签

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderResult>
{
    protected override async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        using var activity = Activity.Current;

        // 添加自定义标签（在 Aspire Dashboard 中可筛选）
        activity?.SetTag("order.customer_id", request.CustomerId);
        activity?.SetTag("order.amount", request.Amount);
        activity?.SetTag("order.item_count", request.Items.Count);

        // ... 业务逻辑
    }
}
```

---

## 🚀 完整示例

查看 [OrderSystem 完整示例](../../examples/README-ORDERSYSTEM.md)：

- ✅ Aspire AppHost 配置
- ✅ Catga Debugger 集成
- ✅ OpenTelemetry 追踪
- ✅ 健康检查
- ✅ 自定义指标
- ✅ 优雅关闭

---

## 📚 相关文档

- [Time-Travel Debugger 完整指南](../DEBUGGER.md)
- [Debugger 架构设计](../../CATGA-DEBUGGER-PLAN.md)
- [OpenTelemetry 集成](observability.md)
- [健康检查配置](health-checks.md)
- [Aspire 部署指南](../deployment/aspire-deployment.md)

---

<div align="center">

**🎉 Catga Debugger + Aspire = 完美的监控和调试体验！**

[OrderSystem 示例](../../examples/README-ORDERSYSTEM.md) · [Debugger 指南](../DEBUGGER.md) · [返回文档](../INDEX.md)

</div>

