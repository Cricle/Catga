# Catga 生产监控指南

这篇文档只回答一件事：Catga 在生产里怎么接入标准监控栈，并且哪些内容是当前仓库里真实存在的能力。

## 当前建议

Catga 的生产监控应该按 3 层接：

1. `AddHostedServices()`：让 transport、recovery、outbox processor 真正运行起来
2. `AddCatgaHealthChecks()`：暴露 transport / persistence / recovery 健康状态
3. OpenTelemetry：接 tracing、metrics、Prometheus / OTLP 导出

如果你还没统一看过托管和健康检查，先读 [托管服务配置指南](../guides/hosting-configuration.md)。

## 最小生产接法

```csharp
using Catga.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var redis = builder.Configuration.GetConnectionString("Redis")!;

var catga = builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redis)
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRedisTransport(redis);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(CatgaOpenTelemetryExtensions.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(CatgaOpenTelemetryExtensions.MeterName)
        .AddPrometheusExporter());

builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

app.MapPrometheusScrapingEndpoint();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.Run();
```

## 健康检查

`AddCatgaHealthChecks()` 当前会注册 3 个检查：

- `catga_transport`
- `catga_persistence`
- `catga_recovery`

标签约定：

- `ready`
- `live`

因此生产里通常至少要映射：

- `/health`
- `/health/ready`
- `/health/live`

如果你在 Kubernetes / Aspire 里做探针，优先把 `ready` 和 `live` 分开。

## OpenTelemetry 接入点

Catga 当前对外暴露的标准常量是：

- `CatgaOpenTelemetryExtensions.ActivitySourceName`
- `CatgaOpenTelemetryExtensions.MeterName`

不要在文档或业务代码里硬编码旧的 source / meter 名字。直接用这两个常量最稳。

更细的 tracing / Prometheus 说明请看：

- [OpenTelemetry 集成](../articles/opentelemetry-integration.md)
- [分布式追踪指南](../observability/DISTRIBUTED-TRACING-GUIDE.md)

## 当前可观测指标范围

当前代码里已经覆盖的指标族主要包括：

- command / event 执行计数与耗时
- pipeline 行为数量与耗时
- event store 读写计数与耗时
- inbox / outbox / dead letter 计数
- idempotency 命中与 miss
- resilience 重试与熔断
- flow / step 执行计数与耗时

Prometheus 命名会基于 OpenTelemetry 导出结果展开，因此应以实际 exporter 输出为准，而不是死记文档截图或历史指标名。

## Grafana Dashboard

仓库里当前自带的 Grafana 模板文件在：

- `src/Catga/Observability/GrafanaDashboard.json`

它是一个现成的 starter dashboard，重点覆盖 `Flow DSL` 指标，不应该被理解成“Catga 所有 transport / persistence / broker 维度都已内建完整 dashboard”。

导入方式：

1. 打开 Grafana
2. 进入 `Dashboards -> Import`
3. 上传 `src/Catga/Observability/GrafanaDashboard.json`
4. 选择你的 Prometheus 数据源

如果你跑的是 Redis / RabbitMQ / NATS 生产组合，通常还需要把 broker 自身 dashboard 一并导入，不能只看 Catga 这一层。

## 生产建议

- 默认先把 `/health`、Prometheus exporter、OTLP tracing 三件套打通
- 如果启用了 `UseOutbox()`，不要省略 `AddHostedServices()`
- 把 Catga 指标和 broker / Redis 指标放在同一套 Grafana 看板里
- 不要把 benchmark 文档里的数字当成监控阈值

## 相关文档

- [托管服务配置指南](../guides/hosting-configuration.md)
- [OpenTelemetry 集成](../articles/opentelemetry-integration.md)
- [可观测性索引](../observability/README.md)
