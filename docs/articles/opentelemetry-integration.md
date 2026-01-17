# OpenTelemetry 集成指南

Catga 使用 .NET 原生的 `System.Diagnostics` API（`ActivitySource` 和 `Meter`），完全兼容 OpenTelemetry。

## 🎯 设计原则

- **零依赖**: Catga 核心库不依赖 OpenTelemetry，保持轻量
- **标准兼容**: 使用 .NET 标准的 `ActivitySource` 和 `Meter` API
- **灵活集成**: 用户可以在应用层自由选择 OpenTelemetry 或其他监控工具

## 📦 安装

在您的应用项目中安装 OpenTelemetry 包：

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.Console
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Extensions.Hosting
```

## ⚡ 快速开始

### 1. 配置 Tracing（分布式追踪）

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// 配置 OpenTelemetry Tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("YourServiceName"))
    .WithTracing(tracing => tracing
        .AddSource("Catga.Framework")  // 添加 Catga 的 ActivitySource
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()  // 控制台输出（开发环境）
        .AddOtlpExporter());   // OTLP 导出（生产环境）

var app = builder.Build();
```

### 2. 配置 Metrics（指标监控）

```csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Catga.Framework")  // 添加 Catga 的 Meter
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddConsoleExporter()
        .AddOtlpExporter());
```

### 3. 完整示例

```csharp
using Catga;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services.AddCatga(options =>
{
    // Catga 配置
});

// 添加 OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "MyService",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        .AddSource("Catga.Framework")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("Catga.Framework")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));

var app = builder.Build();
```

## 🔍 可观测性数据

### Tracing（追踪）

Catga 自动创建以下 Activities：

| Activity 名称 | 类型 | 描述 |
|--------------|------|------|
| `Command: {RequestType}` | Internal | 命令执行 |
| `Event: {EventType}` | Producer | 事件发布 |
| `Handle: {EventType}` | Consumer | 事件处理 |

**标签（Tags）**:
- `catga.type`: 操作类型（command/event/query）
- `catga.message.id`: 消息 ID
- `catga.message.type`: 消息类型
- `catga.correlation_id`: 关联 ID
- `catga.success`: 操作是否成功
- `catga.duration.ms`: 执行时长
- `catga.error`: 错误信息（如果失败）

### Metrics（指标）

Catga 导出以下 Metrics：

| Metric 名称 | 类型 | 描述 |
|-------------|------|------|
| `catga.messages.published` | Counter | 发布的消息总数 |
| `catga.messages.sent` | Counter | 发送的消息总数 |
| `catga.messages.received` | Counter | 接收的消息总数 |
| `catga.messages.processed` | Counter | 成功处理的消息总数 |
| `catga.messages.failed` | Counter | 失败的消息总数 |
| `catga.message.processing.duration` | Histogram | 消息处理时长（ms）|
| `catga.message.size` | Histogram | 消息大小（bytes）|
| `catga.handlers.active` | Gauge | 活跃的处理器数量 |

## 🎨 Jaeger 集成

### 启动 Jaeger（Docker）

```bash
docker run -d --name jaeger \
  -p 4317:4317 \
  -p 4318:4318 \
  -p 16686:16686 \
  jaegertracing/all-in-one:latest
```

### 配置 OTLP Exporter

```csharp
.AddOtlpExporter(options =>
{
    options.Endpoint = new Uri("http://localhost:4317");
    options.Protocol = OtlpExportProtocol.Grpc;
})
```

访问 Jaeger UI: http://localhost:16686

## 📊 Prometheus 集成

### 配置 Prometheus Exporter

```bash
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Catga.Framework")
        .AddPrometheusExporter());

var app = builder.Build();

// 暴露 Prometheus metrics 端点
app.MapPrometheusScrapingEndpoint();  // /metrics
```

### Prometheus 配置

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'myservice'
    static_configs:
      - targets: ['localhost:5000']
```

## 🌐 Grafana 集成

### 1. Grafana + Tempo（Tracing）

```yaml
# docker-compose.yml
services:
  tempo:
    image: grafana/tempo:latest
    command: [ "-config.file=/etc/tempo.yaml" ]
    ports:
      - "4317:4317"  # OTLP gRPC
      - "3200:3200"  # Tempo UI

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_AUTH_ANONYMOUS_ENABLED=true
      - GF_AUTH_ANONYMOUS_ORG_ROLE=Admin
```

### 2. Grafana Dashboard

Catga 提供了预置的 Grafana Dashboard 模板，可以从项目仓库的 `docs/observability/dashboards` 目录获取。

导入步骤：
1. 登录 Grafana
2. 导航到 Dashboards → Import
3. 上传 `catga-overview.json` 文件
4. 选择 Prometheus 数据源
5. 点击 Import

Dashboard 包含以下面板：
- 命令/查询执行速率和延迟
- 事件发布统计
- Pipeline 行为性能
- 批处理队列状态
- 错误率和失败分布

## 🔧 高级配置

### 采样策略

```csharp
.WithTracing(tracing => tracing
    .AddSource("Catga.Framework")
    .SetSampler(new TraceIdRatioBasedSampler(0.1))  // 10% 采样率
)
```

### 自定义标签

```csharp
using Catga.Observability;

// 在 Handler 中添加自定义标签
public class MyCommandHandler : IRequestHandler<MyCommand, MyResult>
{
    public async Task<CatgaResult<MyResult>> HandleAsync(
        MyCommand request,
        CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        activity?.SetTag("user.id", request.UserId);
        activity?.SetTag("tenant.id", request.TenantId);

        // ... 业务逻辑
    }
}
```

### Trace 传播

Catga 使用 W3C Trace Context 标准自动传播 Trace 信息：

```csharp
// 发送消息时自动注入 Trace Context
var context = new TransportContext { /* ... */ };
context = TraceContextPropagator.Inject(context);
await transport.PublishAsync(message, context);

// 接收消息时自动提取 Trace Context
var activity = TraceContextPropagator.Extract(context, "ProcessMessage");
using (activity)
{
    // ... 处理消息
}
```

## 📝 最佳实践

### 1. 生产环境配置

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Catga.Framework");

        if (builder.Environment.IsProduction())
        {
            // 生产环境：OTLP + 采样
            tracing.SetSampler(new TraceIdRatioBasedSampler(0.1))
                   .AddOtlpExporter();
        }
        else
        {
            // 开发环境：全量 + Console
            tracing.AddConsoleExporter();
        }
    });
```

### 2. 性能优化

```csharp
// 使用批处理导出器减少开销
.AddOtlpExporter(options =>
{
    options.BatchExportProcessorOptions = new()
    {
        MaxQueueSize = 2048,
        ScheduledDelayMilliseconds = 5000,
        ExporterTimeoutMilliseconds = 30000,
        MaxExportBatchSize = 512
    };
})
```

### 3. 错误处理

```csharp
// 记录异常到 Activity
try
{
    await handler.HandleAsync(request, cancellationToken);
}
catch (Exception ex)
{
    TraceContextPropagator.RecordException(Activity.Current, ex);
    throw;
}
```

## 🚀 .NET Aspire 集成

如果使用 .NET Aspire，OpenTelemetry 已内置：

```csharp
// AppHost 项目
var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.MyApi>("api")
    .WithEnvironment("OTEL_SERVICE_NAME", "MyApi");

builder.Build().Run();
```

Aspire Dashboard 自动显示 Catga 的 Traces 和 Metrics！

## 📚 参考资源

- [OpenTelemetry .NET 文档](https://opentelemetry.io/docs/languages/net/)
- [.NET Diagnostics API](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [Jaeger 文档](https://www.jaegertracing.io/docs/)
- [Prometheus 文档](https://prometheus.io/docs/)

## 💡 常见问题

### Q: 为什么 Catga 不直接依赖 OpenTelemetry？

A: 为了保持核心库的轻量和灵活性。.NET 的 `System.Diagnostics` API 是标准的，可以被任何监控工具使用（OpenTelemetry、Application Insights、Datadog 等）。

### Q: 如何禁用 Tracing/Metrics？

A: 只需不配置 OpenTelemetry，Catga 的 ActivitySource 和 Meter 会自动变为无操作（no-op），性能开销几乎为零。

### Q: 性能影响？

A:
- **未启用 OpenTelemetry**: 几乎零开销（~1-2ns per operation）
- **启用 + 采样**: 轻微开销（~100-500ns per sampled operation）
- **启用 + 100% 采样**: 中等开销（推荐生产环境使用 1-10% 采样率）

---

**下一步**: 查看 [配置指南](configuration.md) 了解更多 Catga 配置选项



