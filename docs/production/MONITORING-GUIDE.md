# Catga 生产环境监控指南

## 概述

Catga 完全集成标准可观测性技术栈，**不造轮子**：
- **OpenTelemetry** - 分布式追踪和指标
- **Prometheus** - 指标存储和查询
- **Grafana** - 可视化和告警
- **.NET Aspire** - 统一的遥测平台

---

## 🏗️ 架构

```
Catga Framework
    ├── OpenTelemetry (内置)
    │   ├── ActivitySource (分布式追踪)
    │   ├── Meter (指标)
    │   └── LoggerMessage (结构化日志)
    │
    ├── Prometheus Exporter
    │   └── /metrics 端点
    │
    └── Grafana Dashboard
        └── catga-dashboard.json
```

---

## 📊 关键指标

### 1. 命令执行指标

| 指标名称 | 类型 | 描述 | 标签 |
|---------|------|------|------|
| `catga_commands_executed_total` | Counter | 命令执行总数 | `request_type`, `success` |
| `catga_command_duration_seconds` | Histogram | 命令执行时长 | `request_type` |
| `catga_commands_active` | Gauge | 当前执行中的命令数 | - |

### 2. 事件发布指标

| 指标名称 | 类型 | 描述 | 标签 |
|---------|------|------|------|
| `catga_events_published_total` | Counter | 事件发布总数 | `event_type` |
| `catga_event_handlers_executed` | Counter | 事件处理器执行次数 | `handler_type`, `success` |

### 3. 系统健康指标

| 指标名称 | 类型 | 描述 |
|---------|------|------|
| `catga_active_flows` | Gauge | 活跃消息流数量 |
| `catga_event_store_size_bytes` | Gauge | 事件存储占用内存 |
| `catga_circuit_breaker_open` | Gauge | 熔断器状态 (0=关闭, 1=开启) |
| `catga_replay_sessions_active` | Gauge | 活跃回放会话数 |

---

## 🚀 快速开始

### 1. 配置生产环境

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 使用 .NET Aspire (推荐)
builder.AddServiceDefaults();

// 或手动配置 OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddPrometheusExporter()
        .AddMeter("Catga.*"))
    .WithTracing(tracing => tracing
        .AddSource("Catga.*")
        .AddAspNetCoreInstrumentation());

// 添加 Catga (生产优化模式)
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForProduction(); // 生产模式

var app = builder.Build();

// 映射 Prometheus metrics 端点
app.MapPrometheusScrapingEndpoint(); // .NET Aspire
// 或
app.MapGet("/metrics", async (IPrometheusMetricsExporter exporter) =>
{
    var metrics = await exporter.ExportAsync();
    return Results.Text(metrics, "text/plain; version=0.0.4");
});

app.Run();
```

### 2. 生产模式特点

生产模式会：
- ✅ **禁用时间旅行调试**（零开销）
- ✅ **禁用状态快照**（节省内存）
- ✅ **禁用变量捕获**（提升性能）
- ✅ **启用采样**（1% 异常采样）
- ✅ **使用 Ring Buffer**（固定内存）
- ✅ **启用自适应采样**（负载自适应）
- ✅ **2小时后自动禁用**（安全保护）

```csharp
// 生产模式建议：在生产环境使用采样与最小化捕获，避免额外调试开销
// 使用 OpenTelemetry 采样与导出替代自定义调试器
```

---

## 📦 Prometheus 配置

### prometheus.yml

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'catga-app'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
    scrape_interval: 10s
```

### 启动 Prometheus

```bash
# Docker
docker run -d \
  --name prometheus \
  -p 9090:9090 \
  -v ./prometheus.yml:/etc/prometheus/prometheus.yml \
  prom/prometheus

# 访问 Prometheus UI
open http://localhost:9090
```

---

## 📈 Grafana 配置

### 1. 添加数据源

```yaml
# grafana/provisioning/datasources/prometheus.yml
apiVersion: 1

datasources:
  - name: Prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
```

### 2. 导入 Dashboard

1. 打开 Grafana UI: `http://localhost:3000`
2. 导航到 **Dashboards → Import**
3. 上传 `grafana/catga-dashboard.json`
4. 或使用 Dashboard ID: `catga-cqrs`

### 3. 启动 Grafana

```bash
docker run -d \
  --name grafana \
  -p 3000:3000 \
  -v ./grafana:/etc/grafana/provisioning \
  grafana/grafana
```

### 4. 预置 Dashboard 面板

| 面板 | 描述 |
|------|------|
| Command Execution Rate | 命令执行速率（按类型） |
| Command Success Rate | 命令成功率（百分比） |
| Command Duration (p50/p95/p99) | 命令执行时长分位数 |
| Error Rate by Type | 错误率（按命令类型） |
| Active Message Flows | 活跃消息流数量 |
| Event Store Size | 事件存储内存占用 |
| Circuit Breaker Status | 熔断器状态 |
| Top 10 Handlers | 最常执行的处理器 |

---

## 🔔 告警规则

### Prometheus 告警

```yaml
# alerts/catga-rules.yml
groups:
  - name: catga
    interval: 30s
    rules:
      # 高错误率告警
      - alert: HighErrorRate
        expr: |
          (
            sum(rate(catga_commands_executed_total{success="false"}[5m]))
            /
            sum(rate(catga_commands_executed_total[5m]))
          ) > 0.05
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Catga 命令错误率超过 5%"
          description: "错误率: {{ $value | humanizePercentage }}"

      # 高延迟告警
      - alert: HighLatency
        expr: |
          histogram_quantile(0.99,
            rate(catga_command_duration_seconds_bucket[5m])
          ) > 1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Catga 命令 P99 延迟超过 1 秒"

      # 熔断器开启告警
      - alert: CircuitBreakerOpen
        expr: catga_circuit_breaker_open == 1
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Catga 熔断器已开启"

      # 内存使用过高告警
      - alert: HighMemoryUsage
        expr: catga_event_store_size_bytes > 100 * 1024 * 1024  # 100MB
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Catga 事件存储内存超过 100MB"
```

### Grafana 告警

在 Grafana 中配置告警：

1. **错误率告警**
   - 阈值：> 5%
   - 持续时间：5 分钟
   - 通知：Slack/Email

2. **延迟告警**
   - P99 > 1 秒
   - P95 > 500ms
   - 持续时间：5 分钟

3. **可用性告警**
   - 熔断器开启
   - 立即通知

---

## 🔍 分布式追踪

### OpenTelemetry 集成

Catga 自动创建追踪 Span：

```
HTTP Request (incoming)
  └─ Catga.CatgaMediator: SendAsync
      ├─ Catga.Behavior: Logging
      ├─ Catga.Handler: CreateOrderHandler
      │   ├─ Database: SaveOrder
      │   └─ Catga.CatgaMediator: PublishAsync
      │       ├─ Catga.Handler: OrderCreatedNotification
      │       └─ Catga.Handler: OrderCreatedAnalytics
      └─ Catga.Behavior: Performance
```

### 查看追踪

使用 .NET Aspire Dashboard:
```bash
# 启动应用
dotnet run --project examples/OrderSystem.AppHost

# 访问 Aspire Dashboard
open http://localhost:18888
```

或使用 Jaeger:
```bash
docker run -d \
  --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  jaegertracing/all-in-one:latest

# 配置 OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));
```

---

## 📝 日志查询

### 结构化日志示例

```json
{
  "timestamp": "2025-10-17T12:34:56.789Z",
  "level": "Information",
  "category": "Catga.CatgaMediator",
  "message": "Command executed",
  "properties": {
    "RequestType": "CreateOrderCommand",
    "MessageId": "abc-123",
    "Duration": 123.45,
    "Success": true,
    "CorrelationId": "xyz-789"
  }
}
```

### Loki 查询

```logql
# 查询所有错误
{app="catga"} |= "error" | json

# 查询慢请求
{app="catga"} | json | Duration > 1000

# 查询特定命令
{app="catga"} | json | RequestType="CreateOrderCommand"
```

---

## 🎯 性能优化建议

### 1. 采样率调整

```csharp
// 高流量系统
builder.Services.AddCatgaDebugger(options =>
{
    options.SamplingRate = 0.001;  // 0.1% 采样
    options.EnableAdaptiveSampling = true; // 自适应采样
});
```

### 2. 内存限制

```csharp
options.UseRingBuffer = true;
options.MaxMemoryMB = 50; // 限制 50MB
```

### 3. 禁用昂贵功能

```csharp
// 生产环境禁用
options.EnableReplay = false;           // 时间旅行
options.TrackStateSnapshots = false;    // 状态快照
options.CaptureVariables = false;       // 变量捕获
options.CaptureCallStacks = false;      // 调用栈
options.CaptureMemoryState = false;     // 内存状态
```

---

## 📊 Dashboard 示例查询

### PromQL 查询

```promql
# 成功率
sum(rate(catga_commands_executed_total{success="true"}[5m]))
/
sum(rate(catga_commands_executed_total[5m]))
* 100

# P99 延迟
histogram_quantile(0.99,
  rate(catga_command_duration_seconds_bucket[5m])
)

# 错误率
rate(catga_commands_executed_total{success="false"}[5m])

# 吞吐量
sum(rate(catga_commands_executed_total[1m]))

# Top 10 慢命令
topk(10,
  avg by (request_type) (
    rate(catga_command_duration_seconds_sum[5m])
    /
    rate(catga_command_duration_seconds_count[5m])
  )
)
```

---

## 🔐 安全最佳实践

### 1. 生产环境配置

```csharp
// ✅ 正确：使用生产模式（仅必要的监控与采样）
builder.Services.AddCatga().ForProduction();

// ❌ 错误：在生产中启用调试器式的全量捕获/完整调试 UI
// 请改用 OpenTelemetry 采样与导出
```

### 2. 保护调试端点

```csharp
// 生产环境仅暴露指标端点
app.MapPrometheusScrapingEndpoint(); // /metrics
```

### 3. 认证和授权

```csharp
// 保护 metrics 端点
app.MapGet("/metrics", async (IPrometheusMetricsExporter exporter) =>
{
    // 实现认证逻辑
    var metrics = await exporter.ExportAsync();
    return Results.Text(metrics, "text/plain");
}).RequireAuthorization("MetricsReader");
```

---

## 📦 完整 Docker Compose 示例

```yaml
# docker-compose.yml
version: '3.8'

services:
  # 你的应用
  catga-app:
    build: .
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    depends_on:
      - prometheus

  # Prometheus
  prometheus:
    image: prom/prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - ./alerts:/etc/prometheus/alerts
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'

  # Grafana
  grafana:
    image: grafana/grafana
    ports:
      - "3000:3000"
    volumes:
      - ./grafana:/etc/grafana/provisioning
      - grafana-storage:/var/lib/grafana
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin

  # Loki (日志)
  loki:
    image: grafana/loki
    ports:
      - "3100:3100"

volumes:
  grafana-storage:
```

---

## 🎓 总结

### ✅ 推荐做法

1. **使用标准工具**
   - OpenTelemetry（追踪 + 指标）
   - Prometheus（存储）
   - Grafana（可视化）

2. **生产环境**
   - 使用 `ForProduction()` 模式
   - 禁用时间旅行和状态捕获
   - 启用采样和内存限制

3. **监控**
   - 导入预置 Grafana Dashboard
   - 配置告警规则
   - 定期查看指标

### ❌ 避免做法

1. **不要在生产环境**
   - 使用开发模式
   - 启用完整的调试功能
   - 100% 采样率

2. **不要自己造轮子**
   - 使用 OpenTelemetry，不要自定义指标格式
   - 使用 Prometheus，不要自建指标存储
   - 使用 Grafana，不要自建 UI

---

## 📚 相关资源

- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Prometheus .NET Client](https://github.com/prometheus-net/prometheus-net)
- [Grafana Dashboards](https://grafana.com/grafana/dashboards/)
- [.NET Aspire Dashboard](https://learn.microsoft.com/aspire/fundamentals/dashboard)

---

**生产环境安全 ✅ | 标准工具集成 ✅ | Grafana 完全监控 ✅**

