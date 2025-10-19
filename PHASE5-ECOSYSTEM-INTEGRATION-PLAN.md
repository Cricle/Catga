# Phase 5: 生态系统集成详细计划

**制定日期**: 2025-10-19
**预计总时间**: 11 小时
**优先级**: 🔵 Low (增值功能，非必需)

---

## 📋 概述

Phase 5 专注于将 Catga 与 .NET 生态系统的现代工具和框架深度集成，提升可观测性、开发体验和生产环境监控能力。

**核心目标**:
1. ✨ **OpenTelemetry 完整集成** - 提供生产级分布式追踪和指标
2. ✨ **.NET Aspire 集成** - 现代云原生开发体验
3. ✨ **Source Generator 增强** - 更强大的编译时代码分析

---

## 🎯 Task 5.1: OpenTelemetry 完整集成

**优先级**: 🔵 Low
**预计时间**: 4 小时
**价值**: ⭐⭐⭐⭐⭐ (生产环境必备)

### 背景

OpenTelemetry 是云原生可观测性的标准，提供：
- 🔍 **分布式追踪** - 跨服务的请求流追踪
- 📊 **指标收集** - 性能和业务指标
- 📝 **日志关联** - 自动关联 Traces、Metrics 和 Logs

### 当前状态

Catga 已有基础支持：
- ✅ 简单的 `Activity` 集成
- ✅ Jaeger 示例项目
- ❌ 缺少自动 Trace 传播
- ❌ 缺少 Metrics 导出
- ❌ 缺少 Exemplar 支持

### 实现计划

#### 1. ActivitySource 集成 (1.5 小时)

**创建文件**: `src/Catga/Observability/CatgaActivitySource.cs`

```csharp
using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Centralized ActivitySource for Catga framework
/// </summary>
public static class CatgaActivitySource
{
    public static readonly ActivitySource Source = new("Catga", "1.0.0");

    // Activity names
    public const string PublishActivity = "catga.transport.publish";
    public const string SendActivity = "catga.transport.send";
    public const string SubscribeActivity = "catga.transport.subscribe";
    public const string HandleActivity = "catga.mediator.handle";
    public const string OutboxProcessActivity = "catga.outbox.process";
    public const string InboxProcessActivity = "catga.inbox.process";
    public const string EventStoreAppendActivity = "catga.eventstore.append";

    // Tags (following OpenTelemetry semantic conventions)
    public const string MessageIdTag = "messaging.message.id";
    public const string MessageTypeTag = "messaging.message.type";
    public const string QoSTag = "messaging.qos";
    public const string DestinationTag = "messaging.destination.name";
    public const string SystemTag = "messaging.system";
}
```

**修改组件**:
- `CatgaMediator.cs` - 在 `SendAsync` 和 `PublishAsync` 中创建 Activity
- `InMemoryMessageTransport.cs` - 在 `PublishAsync` 和 `SendAsync` 中创建 Activity
- `RedisMessageTransport.cs` - 同上
- `NatsMessageTransport.cs` - 同上

**示例实现** (`CatgaMediator.cs`):
```csharp
public async Task<CatgaResult<TResponse>> SendAsync<TResponse>(
    IRequest<TResponse> request,
    CancellationToken cancellationToken = default)
{
    using var activity = CatgaActivitySource.Source.StartActivity(
        CatgaActivitySource.HandleActivity,
        ActivityKind.Internal);

    activity?.SetTag(CatgaActivitySource.MessageTypeTag, typeof(TRequest).Name);
    activity?.SetTag(CatgaActivitySource.MessageIdTag, request.MessageId);

    try
    {
        var result = await _pipeline.ExecuteAsync(request, cancellationToken);

        activity?.SetStatus(
            result.IsSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            result.Error?.Message);

        return result;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
}
```

#### 2. 自动 Trace 传播 (1 小时)

**创建文件**: `src/Catga/Observability/TraceContextPropagator.cs`

```csharp
using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Propagates trace context across message boundaries
/// </summary>
public static class TraceContextPropagator
{
    public const string TraceParentKey = "traceparent";
    public const string TraceStateKey = "tracestate";

    /// <summary>
    /// Inject current trace context into message context
    /// </summary>
    public static void Inject(TransportContext context)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        context.Headers[TraceParentKey] = activity.Id ?? string.Empty;
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            context.Headers[TraceStateKey] = activity.TraceStateString;
        }
    }

    /// <summary>
    /// Extract trace context from message and create linked activity
    /// </summary>
    public static Activity? Extract(TransportContext? context, string activityName)
    {
        if (context?.Headers == null) return null;

        if (context.Headers.TryGetValue(TraceParentKey, out var traceParent))
        {
            var activity = CatgaActivitySource.Source.StartActivity(
                activityName,
                ActivityKind.Consumer,
                traceParent);

            if (context.Headers.TryGetValue(TraceStateKey, out var traceState))
            {
                activity?.SetTag("tracestate", traceState);
            }

            return activity;
        }

        return null;
    }
}
```

**集成到 Transport 层**:
- 发送消息时自动注入 Trace Context
- 接收消息时自动提取并创建 Child Activity

#### 3. Metrics 导出 (1 小时)

**创建文件**: `src/Catga/Observability/CatgaMetrics.cs`

```csharp
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>
/// Centralized Metrics for Catga framework
/// </summary>
public static class CatgaMetrics
{
    private static readonly Meter Meter = new("Catga", "1.0.0");

    // Counters
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>(
        "catga.messages.published",
        "messages",
        "Total number of messages published");

    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        "catga.messages.sent",
        "messages",
        "Total number of messages sent");

    public static readonly Counter<long> MessagesReceived = Meter.CreateCounter<long>(
        "catga.messages.received",
        "messages",
        "Total number of messages received");

    public static readonly Counter<long> MessagesProcessed = Meter.CreateCounter<long>(
        "catga.messages.processed",
        "messages",
        "Total number of messages processed successfully");

    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>(
        "catga.messages.failed",
        "messages",
        "Total number of messages failed to process");

    // Histograms
    public static readonly Histogram<double> MessageProcessingDuration = Meter.CreateHistogram<double>(
        "catga.message.processing.duration",
        "ms",
        "Message processing duration");

    public static readonly Histogram<double> OutboxProcessingDuration = Meter.CreateHistogram<double>(
        "catga.outbox.processing.duration",
        "ms",
        "Outbox processing duration");

    // Gauges (UpDownCounter)
    public static readonly UpDownCounter<int> ActiveSubscriptions = Meter.CreateUpDownCounter<int>(
        "catga.subscriptions.active",
        "subscriptions",
        "Number of active subscriptions");

    public static readonly UpDownCounter<int> PendingOutboxMessages = Meter.CreateUpDownCounter<int>(
        "catga.outbox.pending",
        "messages",
        "Number of pending outbox messages");
}
```

**集成示例**:
```csharp
// In CatgaMediator
public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    where TEvent : IEvent
{
    var sw = Stopwatch.StartNew();
    try
    {
        await _transport.PublishAsync(@event, context: null, cancellationToken);

        CatgaMetrics.MessagesPublished.Add(1,
            new KeyValuePair<string, object?>("message_type", typeof(TEvent).Name));

        CatgaMetrics.MessageProcessingDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("message_type", typeof(TEvent).Name),
            new KeyValuePair<string, object?>("operation", "publish"));
    }
    catch
    {
        CatgaMetrics.MessagesFailed.Add(1,
            new KeyValuePair<string, object?>("message_type", typeof(TEvent).Name));
        throw;
    }
}
```

#### 4. Exemplar 支持 (0.5 小时)

Exemplar 将 Metrics 与 Traces 关联，允许从 Metric 跳转到对应的 Trace。

```csharp
// In Histogram recording
CatgaMetrics.MessageProcessingDuration.Record(
    duration,
    new KeyValuePair<string, object?>("message_type", messageType),
    new KeyValuePair<string, object?>("trace_id", Activity.Current?.TraceId.ToString()));
```

### 验收标准

- ✅ 所有 Transport 操作都有 Activity
- ✅ Trace Context 自动传播
- ✅ 导出关键 Metrics (吞吐、延迟、错误率)
- ✅ Exemplar 支持
- ✅ 集成到 Jaeger 示例
- ✅ 性能开销 < 5%

---

## 🎯 Task 5.2: .NET Aspire Dashboard 集成

**优先级**: 🔵 Low
**预计时间**: 3 小时
**价值**: ⭐⭐⭐⭐ (现代开发体验)

### 背景

.NET Aspire 是微软推出的云原生开发堆栈，提供：
- 🖥️ **统一仪表板** - 查看所有服务、资源、Traces、Metrics、Logs
- 🔌 **服务发现** - 自动配置和连接
- 📊 **实时监控** - 资源使用、健康状态

### 实现计划

#### 1. 自定义资源类型注册 (1 小时)

**创建文件**: `src/Catga.AspNetCore/Aspire/CatgaResource.cs`

```csharp
namespace Aspire.Hosting;

/// <summary>
/// Represents a Catga message broker resource in Aspire
/// </summary>
public class CatgaResource : Resource
{
    public CatgaResource(string name) : base(name)
    {
    }

    public string? TransportType { get; set; }
    public string? PersistenceType { get; set; }
}

/// <summary>
/// Extension methods for adding Catga resources to Aspire
/// </summary>
public static class CatgaResourceExtensions
{
    public static IResourceBuilder<CatgaResource> AddCatga(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        var resource = new CatgaResource(name);
        return builder.AddResource(resource)
            .WithAnnotation(new ManifestPublishingCallbackAnnotation(context =>
            {
                context.Writer.WriteString("type", "catga.v0");
            }));
    }

    public static IResourceBuilder<CatgaResource> WithInMemoryTransport(
        this IResourceBuilder<CatgaResource> builder)
    {
        builder.Resource.TransportType = "InMemory";
        return builder;
    }

    public static IResourceBuilder<CatgaResource> WithRedisTransport(
        this IResourceBuilder<CatgaResource> builder,
        IResourceBuilder<RedisResource> redis)
    {
        builder.Resource.TransportType = "Redis";
        builder.WithReference(redis);
        return builder;
    }

    public static IResourceBuilder<CatgaResource> WithNatsTransport(
        this IResourceBuilder<CatgaResource> builder,
        IResourceBuilder<ContainerResource> nats)
    {
        builder.Resource.TransportType = "NATS";
        builder.WithReference(nats);
        return builder;
    }
}
```

**使用示例** (`AppHost/Program.cs`):
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");
var nats = builder.AddContainer("nats", "nats", "latest")
    .WithEndpoint(4222, 4222, "client");

var catga = builder.AddCatga("catga")
    .WithRedisTransport(redis)
    .WithRedisPersistence(redis);

var api = builder.AddProject<Projects.OrderSystem_Api>("api")
    .WithReference(catga);

builder.Build().Run();
```

#### 2. 实时消息流可视化 (1 小时)

**创建文件**: `src/Catga.AspNetCore/Aspire/CatgaHealthCheck.cs`

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.AspNetCore;

/// <summary>
/// Health check for Catga message broker
/// </summary>
public class CatgaHealthCheck : IHealthCheck
{
    private readonly ICatgaMediator _mediator;
    private readonly IMessageTransport _transport;

    public CatgaHealthCheck(ICatgaMediator mediator, IMessageTransport transport)
    {
        _mediator = mediator;
        _transport = transport;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>
            {
                ["transport_type"] = _transport.GetType().Name,
                ["active_subscriptions"] = GetActiveSubscriptions(),
                ["messages_processed_total"] = GetMessagesProcessedCount()
            };

            return HealthCheckResult.Healthy("Catga is healthy", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Catga is unhealthy", ex);
        }
    }

    private int GetActiveSubscriptions()
    {
        // TODO: Implement subscription tracking
        return 0;
    }

    private long GetMessagesProcessedCount()
    {
        // TODO: Get from Metrics
        return 0;
    }
}
```

**注册**:
```csharp
services.AddHealthChecks()
    .AddCheck<CatgaHealthCheck>("catga", tags: new[] { "messaging" });
```

#### 3. 健康检查集成 (0.5 小时)

集成到 ASP.NET Core 健康检查端点：
- `/health` - 整体健康状态
- `/health/ready` - 就绪探针
- `/health/live` - 存活探针

#### 4. 分布式追踪可视化 (0.5 小时)

确保 Aspire Dashboard 可以正确显示 Catga 的 Traces：
- 配置 OTLP Exporter
- 添加 Span 属性
- 正确的 ActivityKind

### 验收标准

- ✅ Aspire Dashboard 显示 Catga 资源
- ✅ 健康检查正常工作
- ✅ 可以查看消息流统计
- ✅ Traces 正确显示

---

## 🎯 Task 5.3: Source Generator 增强

**优先级**: 🔵 Low
**预计时间**: 4 小时
**价值**: ⭐⭐⭐ (开发体验提升)

### 背景

当前 Catga 的 Source Generator 主要用于 AOT 兼容性。可以扩展以提供更多编译时检查和代码生成。

### 实现计划

#### 1. 检测未 await 的 Task (1 小时)

**新增分析器**: `AsyncTaskAnalyzer.cs`

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncTaskAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CATGA001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Task should be awaited",
        "Task-returning method '{0}' is not awaited",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if return type is Task
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        if (!IsTaskType(method.ReturnType))
            return;

        // Check if awaited
        if (IsAwaited(invocation))
            return;

        var diagnostic = Diagnostic.Create(Rule,
            invocation.GetLocation(),
            method.Name);
        context.ReportDiagnostic(diagnostic);
    }
}
```

#### 2. 检测缺失的 DI 注册 (1.5 小时)

**新增分析器**: `MissingDIRegistrationAnalyzer.cs`

检测常见的 DI 配置错误：
- 使用 `IMessageTransport` 但未调用 `AddInMemoryTransport()` 等
- 使用 `IEventStore` 但未注册持久化
- 使用 `IMessageSerializer` 但未注册序列化器

#### 3. 检测不支持 AOT 的代码模式 (1 小时)

**新增分析器**: `AotCompatibilityAnalyzer.cs`

检测：
- 直接使用 `JsonSerializer` 而非 `IMessageSerializer`
- 使用 `Type.GetType()` 而未标记 `RequiresUnreferencedCode`
- 使用反射创建实例

#### 4. 自动生成 Benchmark 代码 (0.5 小时)

**新增 Generator**: `BenchmarkGenerator.cs`

为标记了 `[GenerateBenchmark]` 的 Handler 自动生成 BenchmarkDotNet 测试：

```csharp
[GenerateBenchmark]
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Order>
{
    public Task<Order> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // ...
    }
}

// 自动生成:
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class CreateOrderCommandHandlerBenchmark
{
    private CreateOrderCommandHandler _handler;
    private CreateOrderCommand _command;

    [GlobalSetup]
    public void Setup()
    {
        _handler = new CreateOrderCommandHandler();
        _command = new CreateOrderCommand { /* ... */ };
    }

    [Benchmark]
    public Task<Order> Handle()
    {
        return _handler.Handle(_command, CancellationToken.None);
    }
}
```

### 验收标准

- ✅ 所有分析器正常工作
- ✅ 提供有用的警告和错误信息
- ✅ 不产生误报
- ✅ 性能开销可接受

---

## 📊 Phase 5 总览

| Task | 时间 | 优先级 | 价值 | 复杂度 |
|------|------|--------|------|--------|
| 5.1 OpenTelemetry | 4h | 🔵 Low | ⭐⭐⭐⭐⭐ | 中等 |
| 5.2 Aspire Dashboard | 3h | 🔵 Low | ⭐⭐⭐⭐ | 中等 |
| 5.3 Source Generator | 4h | 🔵 Low | ⭐⭐⭐ | 较高 |
| **总计** | **11h** | | | |

---

## 🎯 推荐执行顺序

### 优先级 1: OpenTelemetry (如果需要生产监控)
- 最高价值
- 生产环境必备
- 相对独立，不依赖其他 Phase

### 优先级 2: .NET Aspire (如果使用 Aspire 开发)
- 提升开发体验
- 适合现代云原生项目
- 依赖 OpenTelemetry 效果更好

### 优先级 3: Source Generator (如果团队较大)
- 减少常见错误
- 提升代码质量
- 最复杂，收益相对较小

---

## 💡 何时执行 Phase 5

**建议延后执行的场景**:
- ✅ 核心功能还未完全稳定
- ✅ 还未有生产环境部署
- ✅ 团队规模较小 (< 5 人)
- ✅ 不使用 .NET Aspire

**建议立即执行的场景**:
- ✅ 已有生产环境部署，需要监控
- ✅ 使用 .NET Aspire 进行开发
- ✅ 团队规模较大，需要更多编译时检查
- ✅ 已完成 Phase 2 (测试) 和 Phase 4 (文档)

---

## 📈 预期收益

### OpenTelemetry
- ✅ 快速定位生产问题 (平均诊断时间 -80%)
- ✅ 性能瓶颈可视化
- ✅ 错误率监控和告警

### .NET Aspire
- ✅ 开发体验提升 (配置时间 -60%)
- ✅ 统一的监控视图
- ✅ 更快的本地调试

### Source Generator
- ✅ 减少常见错误 (编译时捕获 -50% Bug)
- ✅ 代码质量提升
- ✅ 自动生成样板代码

---

## 🚀 当前建议

**鉴于您已完成 Phase 1 和 Phase 3**，我建议：

**选项 A**: 先完成 Phase 2 (测试增强) + Phase 4 (文档完善)
- 更全面的测试覆盖
- 完善的文档
- 然后再考虑 Phase 5

**选项 B**: 如果有生产监控需求，优先执行 Task 5.1 (OpenTelemetry)
- 4 小时投入
- 立即获得生产监控能力

**选项 C**: 提交当前进度，发布 v1.0.0-rc1
- Phase 5 可以在后续版本中逐步添加

**您倾向于哪个选项？** 🎯

