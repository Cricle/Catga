# Catga + Jaeger 完整集成指南

**在 Jaeger 中看到完整的消息、事件和执行链路 - 不造轮子！**

---

## 🎯 目标

在 Jaeger UI 中完整展示：
- ✅ 完整的消息流（Command → Event → Handler）
- ✅ 每个步骤的请求/响应 Payload
- ✅ 事件发布和处理的完整链路
- ✅ 错误和异常的详细信息
- ✅ 性能指标（每个步骤的耗时）
- ✅ 跨服务的分布式追踪

---

## 📦 架构

```
HTTP Request
  └─ Catga.Handle.CreateOrderCommand
      ├─ Catga.Behavior.Validation
      ├─ Catga.Behavior.Logging
      ├─ Catga.Handler.CreateOrderHandler
      │   ├─ Database.SaveOrder
      │   └─ Catga.Event.OrderCreatedEvent
      │       ├─ Catga.Handler.OrderCreatedNotification
      │       └─ Catga.Handler.OrderCreatedAnalytics
      └─ Catga.Behavior.Performance
```

**在 Jaeger 中每个 Span 都包含：**
- Request/Response Payload (JSON)
- CorrelationId (跨所有 Span)
- Success/Failure 状态
- 错误详情
- 执行时长

---

## 🚀 快速开始

### 1. 配置应用

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加 OpenTelemetry + Jaeger
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Catga.Framework")  // 关键：添加 Catga 的 ActivitySource
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317"); // Jaeger OTLP 端点
        }));

// 添加 Catga - 启用追踪
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .WithTracing()  // 🔑 关键：启用分布式追踪
    .ForProduction();

var app = builder.Build();
app.Run();
```

### 2. 启动 Jaeger

```bash
# Docker 方式（推荐）
docker run -d \
  --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:latest

# 访问 Jaeger UI
open http://localhost:16686
```

### 3. 发送请求并查看追踪

```bash
# 发送测试请求
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P001","quantity":2}]}'

# 在 Jaeger UI 中搜索
# Service: OrderSystem.Api
# Operation: Catga.Handle.CreateOrderCommand
```

---

## 📊 Jaeger UI 中的显示效果

### Trace 视图

```
OrderSystem.Api: POST /api/orders [200ms]
│
├─ Catga.Handle.CreateOrderCommand [180ms]
│  │
│  ├─ Tags:
│  │  ├─ catga.request.type: CreateOrderCommand
│  │  ├─ catga.correlation_id: d4f06a9d-3da1-420e-8a8f-2bfebe5e9c62
│  │  ├─ catga.success: true
│  │  ├─ catga.duration.ms: 180.45
│  │  ├─ catga.request.payload: {"customerId":"C001",...}
│  │  └─ catga.response.payload: {"orderId":"ORD-123",...}
│  │
│  ├─ Events:
│  │  ├─ Message.Received (0ms)
│  │  │  └─ MessageId: msg-001
│  │  ├─ Command.Succeeded (180ms)
│  │  │  └─ Duration: 180.45ms
│  │
│  └─ Child Spans:
│      ├─ Catga.Event.OrderCreatedEvent [50ms]
│      │  ├─ catga.event.type: OrderCreatedEvent
│      │  ├─ catga.event.payload: {"orderId":"ORD-123",...}
│      │  │
│      │  └─ Child Spans:
│      │      ├─ Catga.HandleEvent.OrderCreatedNotification [20ms]
│      │      └─ Catga.HandleEvent.OrderCreatedAnalytics [30ms]
│      │
│      └─ Database.SaveOrder [100ms]
```

### 失败场景

```
OrderSystem.Api: POST /api/orders [150ms] ❌ ERROR
│
├─ Catga.Handle.CreateOrderCommand [140ms] ❌
│  │
│  ├─ Tags:
│  │  ├─ catga.success: false
│  │  ├─ catga.error: Insufficient inventory
│  │  ├─ catga.error.type: InsufficientInventoryException
│  │  ├─ error: true
│  │  └─ otel.status_code: ERROR
│  │
│  └─ Events:
│      ├─ Command.Failed (140ms)
│      │  ├─ Error: Insufficient inventory
│      │  └─ Duration: 140.23ms
│      │
│      └─ Command.Exception
│          ├─ ExceptionType: InsufficientInventoryException
│          ├─ Message: Product P001 has only 1 unit available
│          └─ StackTrace: at OrderSystem...
```

---

## 🎨 Span 标签详解

### 标准标签（所有 Span）

| 标签 | 示例值 | 说明 |
|------|--------|------|
| `catga.correlation_id` | `d4f06a9d...` | 全局关联 ID |
| `catga.message.type` | `CreateOrderCommand` | 消息类型 |
| `catga.success` | `true` / `false` | 执行结果 |
| `catga.duration.ms` | `180.45` | 执行时长 |

### Command/Query 专用标签

| 标签 | 示例值 | 说明 |
|------|--------|------|
| `catga.request.type` | `CreateOrderCommand` | 请求类型 |
| `catga.request.payload` | `{"customerId":"C001"}` | 请求 JSON |
| `catga.response.payload` | `{"orderId":"ORD-123"}` | 响应 JSON |
| `catga.command.result` | `Success` | 命令结果 |

### Event 专用标签

| 标签 | 示例值 | 说明 |
|------|--------|------|
| `catga.event.type` | `OrderCreatedEvent` | 事件类型 |
| `catga.event.id` | `evt-001` | 事件 ID |
| `catga.event.payload` | `{"orderId":"ORD-123"}` | 事件 JSON |
| `catga.handler.type` | `OrderCreatedNotification` | 处理器类型 |

### 错误标签

| 标签 | 示例值 | 说明 |
|------|--------|------|
| `catga.error` | `Insufficient inventory` | 错误消息 |
| `catga.error.type` | `InsufficientInventoryException` | 异常类型 |
| `error` | `true` | OpenTelemetry 标准错误标记 |
| `otel.status_code` | `ERROR` | OpenTelemetry 状态码 |

---

## 🔍 Span Events

### Command Events

| Event 名称 | 时机 | 附加数据 |
|-----------|------|---------|
| `Message.Received` | 命令接收时 | MessageId, CorrelationId, Timestamp |
| `Command.Succeeded` | 命令成功 | Duration |
| `Command.Failed` | 命令失败 | Error, Duration |
| `Command.Exception` | 发生异常 | ExceptionType, Message, Duration |

### Event Events

| Event 名称 | 时机 | 附加数据 |
|-----------|------|---------|
| `Event.Received` | 事件接收时 | EventId, CorrelationId, Timestamp |
| `Event.Processed` | 事件处理完成 | Duration |
| `Event.Exception` | 处理异常 | ExceptionType, Message, Duration |

---

## 🎯 使用场景

### 场景 1：调试复杂业务流程

**问题**：订单创建成功，但库存未扣减

**Jaeger 查询**：
1. 搜索 `catga.request.type=CreateOrderCommand`
2. 找到对应的 Trace
3. 展开查看所有 Child Spans
4. 查找 `InventoryReservedEvent` 是否被发布
5. 查看该事件的处理器是否执行成功

### 场景 2：性能分析

**问题**：订单创建很慢

**Jaeger 查询**：
1. 按 Duration 排序 Traces
2. 找到最慢的 Trace
3. 查看 Span 树，找出最耗时的步骤
4. 检查是否是数据库查询、外部 API 调用等

### 场景 3：错误排查

**问题**：偶发性的订单创建失败

**Jaeger 查询**：
1. 过滤 `error=true`
2. 查看错误的 Trace
3. 检查 `catga.error` 和 `catga.error.type` 标签
4. 查看 Exception Event 的详细堆栈

### 场景 4：跨服务追踪

**场景**：订单服务 → 库存服务 → 支付服务

```
OrderService: CreateOrder
  └─ HTTP → InventoryService: ReserveInventory
      └─ HTTP → PaymentService: ProcessPayment
```

**所有 Span 共享相同的 `catga.correlation_id`，可在 Jaeger 中完整追踪**

---

## 📋 Jaeger 查询技巧

### 基础查询

```
# 按服务查询
Service: OrderSystem.Api

# 按操作查询
Operation: Catga.Handle.CreateOrderCommand

# 按最小时长
Min Duration: 100ms

# 按标签查询
Tags: catga.success=false
```

### 高级查询

```
# 查找失败的命令
Service: OrderSystem.Api
Tags: catga.success=false AND catga.request.type=CreateOrderCommand

# 查找慢请求
Service: OrderSystem.Api
Min Duration: 500ms
Tags: catga.request.type=CreateOrderCommand

# 查找特定关联 ID
Tags: catga.correlation_id=d4f06a9d-3da1-420e-8a8f-2bfebe5e9c62

# 查找特定订单
Tags: catga.request.payload contains "ORD-123"
```

---

## 🎓 最佳实践

### 1. 关联 ID 传播

```csharp
// 使用 CorrelationIdMiddleware 确保全局 CorrelationId
app.UseCorrelationId();

// 所有 Span 自动包含相同的 CorrelationId
```

### 2. Payload 大小限制

```csharp
// 默认限制 4KB，超过则显示 "<too large>"
// 修改限制（如果需要）：
// 在 DistributedTracingBehavior 中修改 4096 常量
```

### 3. 采样策略

```csharp
// 开发环境：100% 采样
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetSampler(new AlwaysOnSampler()));

// 生产环境：基于概率采样（例如 10%）
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetSampler(new TraceIdRatioBasedSampler(0.1)));
```

### 4. 敏感数据过滤

```csharp
// 自定义 DistributedTracingBehavior 过滤敏感字段
var requestJson = JsonSerializer.Serialize(request);
requestJson = Regex.Replace(requestJson, @"""password""\s*:\s*""[^""]*""", 
    @"""password"":""***""");
```

---

## 🔧 故障排查

### 问题 1：Jaeger 中看不到 Trace

**检查清单**：
1. ✅ 确认 Jaeger 正在运行：`curl http://localhost:16686`
2. ✅ 确认 OTLP 端口可访问：`curl http://localhost:4317`
3. ✅ 检查 OpenTelemetry 配置：`AddSource("Catga.Framework")`
4. ✅ 检查 Catga 追踪已启用：`.WithTracing()`
5. ✅ 检查采样器配置：不要使用 `AlwaysOffSampler`

### 问题 2：Trace 不完整（缺少 Child Spans）

**原因**：Context 传播失败

**解决方案**：
```csharp
// 确保使用 AsyncLocal 或 Activity.Current 传播
// Catga 自动处理，无需手动配置
```

### 问题 3：Payload 显示 "<too large>"

**解决方案**：
```csharp
// 修改 DistributedTracingBehavior.cs
if (requestJson.Length < 8192) // 增加到 8KB
{
    activity.SetTag("catga.request.payload", requestJson);
}
```

---

## 📊 与 Prometheus 配合使用

### 指标 + 追踪联动

1. **Grafana 中发现高错误率**
   - 在 Prometheus 看到 `catga_commands_executed{success="false"}` 升高

2. **跳转到 Jaeger 追踪**
   - 复制时间范围
   - 在 Jaeger 中查询 `catga.success=false`
   - 查看详细错误信息

3. **根因分析**
   - 展开 Trace 树
   - 查看哪个 Span 失败
   - 检查 Exception Event

---

## 🎉 总结

### ✅ 实现效果

1. **完整的消息流可见** ✅
   - Command → Event → Handler 完整链路
   - 每个步骤的 Payload
   - 成功/失败状态

2. **不造轮子** ✅
   - 使用标准 OpenTelemetry
   - 集成 Jaeger（业界标准）
   - 无自定义 UI

3. **生产可用** ✅
   - 支持采样
   - Payload 大小限制
   - 敏感数据过滤

### 🚀 使用步骤

1. **配置**：`.WithTracing()` + OpenTelemetry
2. **启动**：Jaeger via Docker
3. **查看**：Jaeger UI `http://localhost:16686`

### 📖 核心组件

- `CatgaActivitySource` - 统一的 ActivitySource
- `DistributedTracingBehavior` - Command/Query 追踪
- `EventTracingBehavior` - Event 追踪
- OpenTelemetry - 标准导出器

---

**在 Jaeger 中看到完整的 Catga 执行流程 - 完全不造轮子！** 🔍✨

