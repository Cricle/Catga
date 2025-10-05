# 📊 Catga 可观测性指南

Catga 提供完整的可观测性支持，基于 **OpenTelemetry** 标准，包括：

- **分布式追踪** (Distributed Tracing) - ActivitySource
- **指标收集** (Metrics) - Meter API
- **结构化日志** (Structured Logging) - ILogger + Source Generators
- **健康检查** (Health Checks) - ASP.NET Core Health Checks

---

## 🎯 快速开始

### 1. 基础配置

```csharp
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 及可观测性
builder.Services.AddCatga();
builder.Services.AddCatgaObservability();

var app = builder.Build();
app.Run();
```

### 2. 配置 OpenTelemetry

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Exporter.Console
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("my-service"))
    .WithTracing(tracing => tracing
        .AddSource("Catga")                    // Catga 追踪
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()                  // 开发环境
        .AddOtlpExporter())                    // 生产环境
    .WithMetrics(metrics => metrics
        .AddMeter("Catga")                     // Catga 指标
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()
        .AddOtlpExporter());
```

---

## 📈 分布式追踪 (Distributed Tracing)

### 自动追踪

Catga 自动为每个请求创建追踪 Span：

```csharp
// 自动追踪所有请求
var result = await mediator.SendAsync(new CreateOrderCommand(...));

// Span 包含:
// - messaging.system = "catga"
// - messaging.operation = "process"
// - messaging.message_id = "..."
// - catga.message_type = "CreateOrderCommand"
// - catga.duration_ms = 123.45
// - catga.success = true/false
```

### 标准 OpenTelemetry 标签

| 标签 | 说明 | 示例 |
|------|------|------|
| `messaging.system` | 消息系统 | `"catga"` |
| `messaging.operation` | 操作类型 | `"process"` |
| `messaging.message_id` | 消息 ID | `"abc123..."` |
| `messaging.correlation_id` | 关联 ID | `"xyz789..."` |
| `catga.message_type` | 消息类型 | `"CreateOrderCommand"` |
| `catga.duration_ms` | 执行时长 | `123.45` |
| `catga.success` | 是否成功 | `true` |
| `exception.type` | 异常类型 | `"InvalidOperationException"` |
| `exception.message` | 异常消息 | `"Order not found"` |

### 查看追踪

使用 Jaeger/Zipkin/Grafana Tempo 查看追踪：

```bash
# 启动 Jaeger (开发环境)
docker run -d -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one:latest

# 配置 OTLP 导出器
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));

# 访问 Jaeger UI: http://localhost:16686
```

---

## 📊 指标收集 (Metrics)

### 可用指标

#### 计数器 (Counters)

| 指标 | 类型 | 说明 |
|------|------|------|
| `catga.requests.total` | Counter | 请求总数 |
| `catga.requests.succeeded` | Counter | 成功请求数 |
| `catga.requests.failed` | Counter | 失败请求数 |
| `catga.events.published` | Counter | 发布事件数 |
| `catga.retry.attempts` | Counter | 重试尝试次数 |
| `catga.circuit_breaker.opened` | Counter | 熔断器打开次数 |
| `catga.idempotency.skipped` | Counter | 幂等性跳过数 |

#### 直方图 (Histograms)

| 指标 | 类型 | 单位 | 说明 |
|------|------|------|------|
| `catga.request.duration` | Histogram | ms | 请求处理时长 |
| `catga.event.handling_duration` | Histogram | ms | 事件处理时长 |
| `catga.saga.duration` | Histogram | ms | Saga 执行时长 |

#### 仪表盘 (Gauges)

| 指标 | 类型 | 说明 |
|------|------|------|
| `catga.requests.active` | ObservableGauge | 当前活跃请求数 |
| `catga.sagas.active` | ObservableGauge | 当前活跃 Saga 数 |
| `catga.messages.queued` | ObservableGauge | 队列中消息数 |

### 查询示例 (Prometheus)

```promql
# 请求成功率
rate(catga_requests_succeeded_total[5m]) / rate(catga_requests_total[5m])

# 平均请求时长
histogram_quantile(0.95, rate(catga_request_duration_bucket[5m]))

# 当前活跃请求
catga_requests_active

# 熔断器打开频率
rate(catga_circuit_breaker_opened_total[5m])
```

### 配置 Prometheus

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'catga-app'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
    scrape_interval: 15s
```

```csharp
// Program.cs
using OpenTelemetry.Exporter;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Catga")
        .AddPrometheusExporter());

app.MapPrometheusScrapingEndpoint(); // /metrics 端点
```

---

## 📝 结构化日志 (Structured Logging)

### 日志级别

| 级别 | EventId | 说明 | 示例 |
|------|---------|------|------|
| `Information` | 1001 | 请求开始 | 处理请求开始 CreateOrderCommand |
| `Information` | 1002 | 请求成功 | 请求成功 CreateOrderCommand in 123ms |
| `Warning` | 1003 | 请求失败 | 请求失败 CreateOrderCommand: Order not found |
| `Error` | 1004 | 请求异常 | 请求异常 CreateOrderCommand: NullReferenceException |

### 日志字段

每条日志自动包含：

- `RequestType` - 请求类型
- `MessageId` - 消息 ID
- `CorrelationId` - 关联 ID
- `DurationMs` - 执行时长
- `Error` - 错误消息（如果失败）
- `ErrorType` - 错误类型（如果失败）

### 配置日志

```csharp
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Catga": "Debug",
      "Catga.Pipeline": "Information"
    }
  }
}
```

```csharp
// Program.cs
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole(); // 结构化 JSON 输出

// 或使用 Serilog
builder.Host.UseSerilog((context, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Seq("http://localhost:5341"); // Seq 日志服务器
});
```

### 源生成日志（高性能）

Catga 使用 `LoggerMessage` 源生成器，实现：
- ✅ **零分配** - 无字符串插值
- ✅ **AOT 兼容** - 编译时生成
- ✅ **高性能** - 比传统日志快 2-3x

---

## 🏥 健康检查 (Health Checks)

### 添加健康检查

```csharp
builder.Services.AddCatgaHealthChecks(options =>
{
    options.CheckMediator = true;
    options.IncludeMetrics = true;
    options.CheckMemoryPressure = true;
    options.CheckGCPressure = true;
    options.TimeoutSeconds = 5;
});

// 映射健康检查端点
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

### 健康检查响应

```bash
$ curl http://localhost:5000/health
```

```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "catga": {
      "status": "Healthy",
      "description": "Catga 框架运行正常",
      "data": {
        "mediator": "healthy",
        "active_requests": 5,
        "active_sagas": 2,
        "queued_messages": 0,
        "memory_pressure": "12.34%",
        "gc_gen0": 10,
        "gc_gen1": 2,
        "gc_gen2": 0
      }
    }
  }
}
```

### Kubernetes 集成

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: catga-app
spec:
  template:
    spec:
      containers:
        - name: app
          image: catga-app:latest
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 3
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 5
```

---

## 🔧 完整示例

### Program.cs

```csharp
using Catga.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// 1. 添加 Catga 及可观测性
builder.Services.AddCatga();
builder.Services.AddCatgaObservability(options =>
{
    options.CheckMemoryPressure = true;
    options.CheckGCPressure = true;
});

// 2. 配置 OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("order-service")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName
        }))
    .WithTracing(tracing => tracing
        .AddSource("Catga")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://jaeger:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("Catga")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter());

// 3. 配置日志
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.JsonWriterOptions = new()
    {
        Indented = false
    };
});

var app = builder.Build();

// 4. 映射端点
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();
```

---

## 📊 可视化工具

### 推荐工具栈

| 功能 | 工具 | 端口 | UI |
|------|------|------|-----|
| **追踪** | Jaeger | 16686 | http://localhost:16686 |
| **指标** | Prometheus | 9090 | http://localhost:9090 |
| **可视化** | Grafana | 3000 | http://localhost:3000 |
| **日志** | Seq | 5341 | http://localhost:5341 |

### Docker Compose

```yaml
version: '3.8'

services:
  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"  # UI
      - "4317:4317"    # OTLP gRPC

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin

  seq:
    image: datalust/seq:latest
    ports:
      - "5341:80"
    environment:
      - ACCEPT_EULA=Y
```

### Grafana 仪表盘

导入 Catga 预定义仪表盘（计划中）：

```bash
# 下载仪表盘 JSON
wget https://github.com/Cricle/Catga/raw/master/dashboards/catga-overview.json

# 在 Grafana UI 中导入
```

---

## 🎯 最佳实践

### 1. 生产环境配置

```csharp
if (builder.Environment.IsProduction())
{
    // 生产环境：导出到 OTLP 后端
    builder.Services.AddOpenTelemetry()
        .WithTracing(t => t.AddOtlpExporter())
        .WithMetrics(m => m.AddOtlpExporter());
}
else
{
    // 开发环境：输出到控制台
    builder.Services.AddOpenTelemetry()
        .WithTracing(t => t.AddConsoleExporter())
        .WithMetrics(m => m.AddConsoleExporter());
}
```

### 2. 采样策略

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetSampler(new TraceIdRatioBasedSampler(0.1))); // 10% 采样
```

### 3. 资源限制

```csharp
builder.Services.AddCatgaHealthChecks(options =>
{
    options.CheckMemoryPressure = true; // 内存压力 > 90% 报警
    options.TimeoutSeconds = 5;         // 健康检查超时
});
```

### 4. 告警规则 (Prometheus)

```yaml
groups:
  - name: catga_alerts
    rules:
      - alert: HighErrorRate
        expr: rate(catga_requests_failed_total[5m]) > 0.1
        annotations:
          summary: "High error rate detected"

      - alert: HighLatency
        expr: histogram_quantile(0.95, rate(catga_request_duration_bucket[5m])) > 1000
        annotations:
          summary: "P95 latency > 1000ms"

      - alert: CircuitBreakerOpen
        expr: rate(catga_circuit_breaker_opened_total[5m]) > 0
        annotations:
          summary: "Circuit breaker opened"
```

---

## 📚 相关资源

- [OpenTelemetry 官方文档](https://opentelemetry.io/docs/)
- [Prometheus 查询语言](https://prometheus.io/docs/prometheus/latest/querying/basics/)
- [Jaeger 快速开始](https://www.jaegertracing.io/docs/latest/getting-started/)
- [Grafana 仪表盘](https://grafana.com/docs/grafana/latest/dashboards/)
- [ASP.NET Core 健康检查](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)

---

**Catga 提供生产级可观测性，开箱即用！** 📊✨

