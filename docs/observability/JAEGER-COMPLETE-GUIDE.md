# Catga + Jaeger 完整集成指南

## 🎯 核心理念

**Catga 不重复造轮子！** 我们完全拥抱 Jaeger + OpenTelemetry 的强大生态，让你在 Jaeger UI 中完美看到：

- ✅ **分布式事务（Catga）完整流程** - 每个步骤清晰可见
- ✅ **命令（Command）执行链路** - 从HTTP请求到Handler
- ✅ **事件（Event）传播路径** - 发布者→订阅者
- ✅ **聚合根（Aggregate）状态变更** - 所有领域事件
- ✅ **性能指标和错误信息** - 自动记录耗时、异常

---

## 📊 在 Jaeger 中看到什么

### 示例：创建订单的完整 Trace

```
HTTP POST /api/orders (145ms)
  │
  ├─ Command: CreateOrderCommand (142ms)
  │   ├─ catga.type: command
  │   ├─ catga.correlation_id: trace-abc-123
  │   ├─ catga.success: true
  │   │
  │   ├─ Event: OrderCreatedEvent (5ms)
  │   │   ├─ catga.type: event
  │   │   ├─ Timeline: EventPublished
  │   │   │
  │   │   ├─ Handle: OrderCreatedEvent (3ms) [Consumer 1]
  │   │   │   └─ Timeline: EventReceived
  │   │   │
  │   │   └─ Handle: OrderCreatedEvent (2ms) [Consumer 2]
  │   │       └─ Timeline: EventReceived
  │   │
  │   └─ Event: InventoryReservedEvent (3ms)
  │       └─ Timeline: EventPublished
  │
  └─ Response: 200 OK
```

**每个 Span 包含：**
- **Tags**: `catga.type`, `catga.message.id`, `catga.correlation_id`, `catga.success`
- **Events**: `EventPublished`, `EventReceived`, `StateChanged`
- **Duration**: 自动记录执行时间
- **Status**: Ok / Error（自动失败标记）

---

## 🔍 如何在 Jaeger UI 中搜索

### 1. 查看所有命令执行
```
Service: order-api
Tags: catga.type=command
```

### 2. 查看所有事件发布
```
Service: order-api
Tags: catga.type=event
```

### 3. 追踪特定请求的完整流程
```
Service: order-api
Tags: catga.correlation_id={your-correlation-id}
```

### 4. 查找失败的命令
```
Service: order-api
Tags: catga.type=command AND catga.success=false
```

### 5. 查找慢查询（耗时 > 1秒）
```
Service: order-api
Min Duration: 1s
Tags: catga.type=command
```

---

## 🚀 快速开始

### 1. 启动 OrderSystem 示例

```bash
cd examples/OrderSystem.AppHost
dotnet run
```

**自动启动：**
- ✅ Aspire Dashboard: `http://localhost:15888`
- ✅ Jaeger UI: `http://localhost:16686`
- ✅ OrderSystem UI: `http://localhost:5000`
- ✅ Redis, NATS（自动配置）

### 2. 创建测试订单

访问 `http://localhost:5000` 并点击 **"演示成功订单"**

或使用 API：
```bash
curl -X POST http://localhost:5000/demo/order-success
```

### 3. 在 Jaeger UI 查看 Trace

1. 打开 `http://localhost:16686`
2. Service 选择：`order-api`
3. Tags 输入：`catga.type=command`
4. 点击 **Find Traces**
5. 点击任一 Trace 查看详情

---

## 📋 Catga 特定的 Tags

### Tag: `catga.type`
分类 Span 类型，可选值：
- `command` - 命令执行
- `event` - 事件发布/处理
- `catga` - 分布式事务（Saga）
- `aggregate` - 聚合根操作

### Tag: `catga.correlation_id`
关联同一业务流程的所有 Span，自动添加到 **Baggage** 以跨服务传播。

### Tag: `catga.success`
命令/事件是否成功，可选值：
- `true` - 成功
- `false` - 失败

### Tag: `catga.request.type`
请求类型名称，例如：`CreateOrderCommand`

### Tag: `catga.event.type`
事件类型名称，例如：`OrderCreatedEvent`

### Tag: `catga.message.id`
消息唯一ID，用于去重和追踪。

### Tag: `catga.duration`
执行耗时（毫秒），Jaeger 已自动记录，但我们也显式添加。

---

## 📍 Catga 特定的 Events（时间线标记）

### Event: `catga.event.published`
事件被发布时记录，包含：
- `event.type` - 事件类型名称

### Event: `catga.event.received`
事件被Handler接收时记录，包含：
- `event.type` - 事件类型名称
- `handler` - Handler类型名称

### Event: `catga.state.changed`
聚合根状态变更时记录（未来实现），包含：
- `aggregate.id` - 聚合根ID
- `aggregate.type` - 聚合根类型
- `event.type` - 触发的领域事件

---

## 🎨 Jaeger UI 使用技巧

### 1. 比较成功 vs 失败流程

**成功订单：**
```
Tags: catga.type=command AND catga.success=true
```

**失败订单：**
```
Tags: catga.type=command AND catga.success=false
```

点击两个 Trace，使用 **Compare** 功能对比差异。

### 2. 查看服务依赖图

Jaeger UI → **System Architecture**

自动生成服务调用关系图。

### 3. 分析性能瓶颈

1. 按 Duration 排序找最慢的Trace
2. 点击 Trace 查看火焰图
3. 找到最宽的 Span（耗时最长）
4. 检查其 Tags 和 Events

### 4. 查看错误详情

失败的 Span 会有：
- `error` tag = true
- `otel.status_code` = ERROR
- `otel.status_description` = 错误消息
- `exception.message`, `exception.type`, `exception.stacktrace`

---

## 🔧 高级配置

### 自定义 Correlation ID

在你的命令/事件中实现 `IMessage`：

```csharp
public record CreateOrderCommand : IRequest<OrderCreatedResult>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();

    // ... 其他属性
}
```

Catga 会自动提取并添加到 Baggage，确保跨服务传播。

### 调整采样率

在 `appsettings.json`：

```json
{
  "OpenTelemetry": {
    "Tracing": {
      "Sampler": "always_on",  // 生产环境用 "traceidratio"
      "SamplerArg": "1.0"      // 100% 采样，生产环境建议 0.01（1%）
    }
  }
}
```

### 导出到其他后端

除了 Jaeger，还可以导出到：

**Zipkin:**
```csharp
.AddZipkinExporter(options =>
{
    options.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
})
```

**Application Insights:**
```csharp
.AddAzureMonitorTraceExporter(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
})
```

---

## 🌐 生产环境最佳实践

### 1. 使用 Jaeger Collector（高可用）

不要直接用 `all-in-one`，而是部署：
- **Jaeger Collector** - 接收OTLP数据
- **Jaeger Query** - 查询服务
- **Elasticsearch/Cassandra** - 持久化存储

### 2. 启用适当的采样率

```csharp
// ServiceDefaults/Extensions.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.SetSampler(new TraceIdRatioBasedSampler(0.01)); // 1% 采样
    });
```

### 3. 设置 Span 限制

```csharp
.AddAspNetCoreInstrumentation(options =>
{
    options.RecordException = true;

    // 限制 Span 数量，防止内存溢出
    options.Filter = (httpContext) =>
    {
        return !httpContext.Request.Path.StartsWithSegments("/health");
    };
})
```

### 4. 配置保留策略

Jaeger 存储配置（Elasticsearch）：
```yaml
ES_SERVER_URLS: https://elasticsearch:9200
SPAN_STORAGE_TYPE: elasticsearch
ES_MAX_SPAN_AGE: 168h  # 保留 7 天
```

---

## 📊 监控仪表板

### Grafana 集成

使用 Jaeger + Grafana 组合：

1. Grafana 添加 Jaeger 数据源
2. 导入 Jaeger Dashboard（ID: 12021）
3. 创建自定义 Catga Dashboard：
   - 命令成功率：`count(catga.type=command AND catga.success=true) / count(catga.type=command)`
   - 事件发布量：`count(catga.type=event)`
   - P95 耗时：`histogram_quantile(0.95, duration)`

### 告警规则

在 Grafana Alerts 中配置：

**命令失败率过高：**
```promql
rate(traces{catga_type="command",catga_success="false"}[5m]) > 0.1
```

**慢查询告警：**
```promql
histogram_quantile(0.95, duration) > 1000  # P95 > 1秒
```

---

## 🆚 Catga.Debugger vs Jaeger

| 功能 | Catga.Debugger (已删除) | Jaeger (现在使用) |
|------|----------------------|-------------------|
| **时间旅行调试** | 自己实现 | ❌ Jaeger历史查询更强 |
| **性能分析** | 自己实现 | ✅ 火焰图+Grafana |
| **分布式追踪** | 不支持 | ✅ 完美支持 |
| **UI** | 自己的Vue UI | ✅ Jaeger UI（专业） |
| **事务流程** | 需手动拼接 | ✅ 自动Span树 |
| **搜索/过滤** | 基础功能 | ✅ 强大查询语言 |
| **告警** | 不支持 | ✅ Grafana Alerts |
| **生产就绪** | ⚠️ 实验性 | ✅ 业界标准 |

---

## 🎓 学习资源

- [Jaeger 官方文档](https://www.jaegertracing.io/docs/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [.NET Aspire Observability](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry)
- [Catga 文档主页](../README.md)

---

## ❓ 常见问题

### Q: 为什么删除了 Catga.Debugger？
**A:** Jaeger + OpenTelemetry 是行业标准，功能更强大、生态更完善、生产就绪。不需要重复造轮子。

### Q: 我能看到补偿逻辑（Compensation）吗？
**A:** 暂未实现，但计划中。未来会有 `catga.step.type=compensation` 标记。

### Q: 如何在多服务环境中使用？
**A:** Correlation ID 会自动通过 Baggage 跨服务传播，只要所有服务都配置了相同的 Jaeger Collector。

### Q: 性能开销如何？
**A:** OpenTelemetry 开销极低（<1%），生产环境建议1-5%采样率。

---

## 🚀 下一步

1. ✅ 启动 OrderSystem 示例
2. ✅ 创建测试订单
3. ✅ 在 Jaeger UI 查看完整Trace
4. ✅ 尝试不同的搜索条件
5. ✅ 集成到你的项目中

**开始探索 Jaeger + Catga 的强大组合！** 🎯

