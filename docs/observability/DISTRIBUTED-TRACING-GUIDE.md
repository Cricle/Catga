# 分布式追踪完整指南

## 📖 概述

Catga 通过与 **OpenTelemetry** 和 **Jaeger** 的深度集成，提供了完整的分布式追踪能力。本指南将详细说明如何在跨服务场景下实现完整的链路追踪。

---

## 🔗 跨服务链路传播原理

### W3C Trace Context（自动传播）

OpenTelemetry 的 HTTP 客户端 instrumentation **自动处理** W3C Trace Context：

```http
# 自动注入的 HTTP 头
traceparent: 00-{trace-id}-{span-id}-01
tracestate: {vendor-data}
```

✅ **无需手动配置，开箱即用！**

### Correlation ID（Baggage 传播）

Catga 通过 `CorrelationIdDelegatingHandler` 自动传播业务关联ID：

```http
# 自动注入的自定义头
X-Correlation-ID: {correlation-id}
```

#### 传播流程

1. **服务 A**：接收到 HTTP 请求，从 `X-Correlation-ID` 头读取
2. **CatgaMediator**：将 Correlation ID 设置到 `Activity.Baggage`
3. **HttpClient**：通过 `CorrelationIdDelegatingHandler` 从 Baggage 读取并注入到下游请求头
4. **服务 B**：从请求头读取，重复流程

```
┌─────────────────────────────────────────────────────────────┐
│                        服务 A                               │
│                                                             │
│  HTTP Request                                               │
│  └─> Header: X-Correlation-ID: abc123                      │
│       │                                                     │
│       ├─> ASP.NET Core Middleware 提取                     │
│       │   └─> Activity.SetBaggage("catga.correlation_id")  │
│       │                                                     │
│       ├─> CatgaMediator.SendAsync()                        │
│       │   └─> Activity span 设置 correlation_id tag        │
│       │                                                     │
│       └─> HttpClient.SendAsync() → 服务 B                  │
│           └─> CorrelationIdDelegatingHandler 注入          │
│               └─> Header: X-Correlation-ID: abc123          │
└─────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                        服务 B                               │
│                                                             │
│  HTTP Request (from 服务 A)                                │
│  └─> Header: X-Correlation-ID: abc123                      │
│       └─> 重复上述流程...                                   │
└─────────────────────────────────────────────────────────────┘
```

---

## ⚙️ 配置步骤

### 1. ServiceDefaults 配置

在 `ServiceDefaults/Extensions.cs` 中：

```csharp
using Catga.Http;

public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                // ✅ ASP.NET Core 自动提取 traceparent + X-Correlation-ID
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        // 从 HTTP 头传播 Correlation ID 到 Baggage
                        if (request.HttpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
                        {
                            activity.SetTag("catga.correlation_id", correlationId.ToString());
                            activity.SetBaggage("catga.correlation_id", correlationId.ToString());
                        }
                    };
                })
                // ✅ HTTP Client 自动传播 traceparent (W3C Trace Context)
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                // ✅ Catga Activities
                .AddSource("Catga.Framework")
                .AddSource("Catga.*");
        });

    // ✅ 配置 HttpClient 默认行为：注入 X-Correlation-ID
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        // 自动从 Baggage 注入 X-Correlation-ID 到下游请求
        http.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();

        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });

    // 注册 Handler
    builder.Services.AddTransient<CorrelationIdDelegatingHandler>();

    return builder;
}
```

### 2. 使用 HttpClient 调用下游服务

```csharp
public class OrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrderCommandHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CatgaResult<CreateOrderResponse>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // ✅ HttpClient 会自动传播 traceparent 和 X-Correlation-ID
        var httpClient = _httpClientFactory.CreateClient();

        var response = await httpClient.PostAsJsonAsync(
            "http://payment-service/api/payments",
            new { Amount = request.TotalAmount },
            cancellationToken);

        // ... 处理响应
    }
}
```

### 3. 接收端自动提取

在下游服务（如 `payment-service`）中，只要使用相同的 `ServiceDefaults` 配置，就会自动提取：

- **W3C Trace Context**：通过 `AddAspNetCoreInstrumentation()` 自动提取
- **X-Correlation-ID**：通过 `EnrichWithHttpRequest` 提取并设置到 Baggage

---

## 🔍 在 Jaeger 中验证链路

### 1. 启动 Jaeger

```csharp
// AppHost/Program.cs
var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one", "latest")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "jaeger-ui")
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otlp-http");
```

访问：`http://localhost:16686`

### 2. 搜索完整链路

#### 按 Correlation ID 搜索

```
Tags: catga.correlation_id = {your-correlation-id}
```

**结果示例**：

```
Service A: HTTP POST /api/orders
  ├─ Command: CreateOrderCommand
  │   ├─ Event: OrderCreatedEvent
  │   │   └─ Handler: NotifyCustomerHandler
  │   │       └─ HTTP POST payment-service/api/payments  <-- 跨服务调用
  │   │
  │   └─ Service B: HTTP POST /api/payments (new span tree)
  │       ├─ Command: ProcessPaymentCommand
  │       │   └─ Event: PaymentProcessedEvent
  │       │       └─ Handler: UpdateAccountHandler
  │       │           └─ HTTP POST account-service/api/accounts  <-- 再次跨服务
  │       │
  │       └─ Service C: HTTP POST /api/accounts
  │           └─ ...
```

#### 按服务搜索

```
Service: order-api
Operation: Command: CreateOrderCommand
```

#### 验证 Baggage 传播

点击任意 span，查看 **Tags**：

```
catga.correlation_id = abc-123-def
catga.type = command
catga.request.type = CreateOrderCommand
```

点击下游服务的 span，**应该看到相同的 `catga.correlation_id`**。

---

## 🎯 最佳实践

### 1. 始终使用 `IHttpClientFactory`

```csharp
// ✅ 推荐：自动传播
var httpClient = _httpClientFactory.CreateClient();

// ❌ 不推荐：无法自动传播 instrumentation
var httpClient = new HttpClient();
```

### 2. 为每个微服务设置唯一的服务名

```csharp
// AppHost/Program.cs
var orderApi = builder.AddProject<Projects.OrderSystem_Api>("order-api")
    .WithEnvironment("OTEL_SERVICE_NAME", "order-api");

var paymentApi = builder.AddProject<Projects.PaymentSystem_Api>("payment-api")
    .WithEnvironment("OTEL_SERVICE_NAME", "payment-api");
```

### 3. 确保所有服务使用相同的 `ServiceDefaults`

```csharp
// 每个服务的 Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();  // ✅ 统一配置
```

### 4. 处理外部系统调用

如果调用不支持 OpenTelemetry 的外部系统：

```csharp
// 手动传播 Correlation ID
var correlationId = Activity.Current?.GetBaggageItem("catga.correlation_id");
if (!string.IsNullOrEmpty(correlationId))
{
    request.Headers.Add("X-Correlation-ID", correlationId);
}
```

---

## 🐛 常见问题

### 问题 1：链路在 HTTP 调用处断开

**原因**：未使用 `IHttpClientFactory` 或未注册 `CorrelationIdDelegatingHandler`

**解决**：

```csharp
// ServiceDefaults
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
});
builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
```

### 问题 2：看不到 Correlation ID

**原因**：未在 ASP.NET Core instrumentation 中提取

**解决**：

```csharp
.AddAspNetCoreInstrumentation(options =>
{
    options.EnrichWithHttpRequest = (activity, request) =>
    {
        if (request.HttpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            activity.SetBaggage("catga.correlation_id", correlationId.ToString());
        }
    };
})
```

### 问题 3：Jaeger 中看到多个独立的 Trace

**原因**：W3C Trace Context 未正确传播（通常是 `AddHttpClientInstrumentation` 未启用）

**解决**：

```csharp
.WithTracing(tracing =>
{
    tracing.AddHttpClientInstrumentation(options =>
    {
        options.RecordException = true;
    });
})
```

---

## 📊 性能考虑

### 1. Handler 开销

`CorrelationIdDelegatingHandler` 的开销：

- **每个请求**：`Activity.Current?.GetBaggageItem()` ≈ **<1μs**
- **内存**：零额外分配（读取现有 Baggage）

### 2. Baggage 大小限制

W3C Trace Context 建议 Baggage 总大小 < **512 bytes**

✅ Catga 的 `catga.correlation_id` 通常 < 50 bytes

### 3. 生产环境采样

```csharp
// 只采样 10% 的请求
.WithTracing(tracing =>
{
    tracing.SetSampler(new TraceIdRatioBasedSampler(0.1));
})
```

---

## 📚 相关文档

- [Jaeger 完整指南](./JAEGER-COMPLETE-GUIDE.md)
- [OpenTelemetry 集成](../articles/opentelemetry-integration.md)
- [W3C Trace Context 规范](https://www.w3.org/TR/trace-context/)

---

## ✅ 总结

- **W3C Trace Context**：由 OpenTelemetry HTTP 客户端自动处理
- **Correlation ID**：通过 `CorrelationIdDelegatingHandler` + Baggage 传播
- **配置简单**：只需在 `ServiceDefaults` 中一次性配置
- **生产就绪**：低开销，支持采样，完全兼容 Jaeger/Grafana

**Service A → HTTP → Service B → HTTP → Service C** 的完整链路，只要所有服务都使用 `AddServiceDefaults()`，就能在 Jaeger 中看到完整的调用链！🎉

