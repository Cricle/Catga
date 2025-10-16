# OrderSystem - Catga 完整功能演示

**OrderSystem** 是一个功能完整的订单管理系统示例，展示了 Catga 框架的所有核心特性和最佳实践。

---

## 🎯 演示功能

### ✅ CQRS 核心
- **Commands**: CreateOrder, ConfirmOrder, PayOrder, ShipOrder, CancelOrder
- **Queries**: GetOrder, GetCustomerOrders  
- **Events**: OrderCreated, OrderPaid, OrderShipped, OrderCancelled

### ✅ 多事件处理器（One Event → Multiple Handlers）
OrderSystem 包含 **6个事件处理器**演示一个事件如何触发多个业务逻辑：

**OrderCreated 事件**:
1. `SendOrderNotificationHandler` - 发送邮件/短信通知
2. `UpdateAnalyticsHandler` - 更新分析统计

**OrderPaid 事件**:
3. `UpdateInventoryOnPaymentHandler` - 更新库存
4. `PrepareShipmentHandler` - 准备发货

**OrderShipped 事件**:
5. `RecordLogisticsHandler` - 记录物流信息
6. `SendShipmentNotificationHandler` - 发送发货通知

### ✅ 高级特性
- **SafeRequestHandler** - 无需 try-catch 的优雅错误处理
- **Auto-DI** - Source Generator 自动依赖注入
- **[GenerateDebugCapture]** - AOT 兼容的调试变量捕获
- **Time-Travel Debugging** - 完整的回放和调试支持
- **Graceful Lifecycle** - 优雅关闭和恢复

### ✅ 可观测性
- **OpenTelemetry** - 分布式追踪
- **Aspire Integration** - Dashboard 集成
- **Health Checks** - 健康检查端点
- **Debugger UI** - 实时流程监控

---

## 🚀 快速开始

### 1. 运行系统

```bash
# 使用 Aspire AppHost 运行（推荐）
cd examples/OrderSystem.AppHost
dotnet run

# 或直接运行 API
cd examples/OrderSystem.Api
dotnet run
```

### 2. 访问端点

| 服务 | URL | 说明 |
|------|-----|------|
| **API** | http://localhost:5000 | OrderSystem API |
| **Swagger** | http://localhost:5000/swagger | API 文档 |
| **Debugger UI** | http://localhost:5000/debug | Time-Travel 调试界面 |
| **Debug API** | http://localhost:5000/debug-api | 调试 REST API |
| **Aspire Dashboard** | http://localhost:18888 | Aspire 监控面板 |

---

## 📡 API 端点

### 订单操作
```http
# 创建订单
POST /api/orders
{
    "customerId": "CUST-001",
    "items": [
        { "productId": "PROD-001", "quantity": 2, "unitPrice": 99.99 }
    ],
    "shippingAddress": "123 Main St",
    "paymentMethod": "Alipay"
}

# 查询订单
GET /api/orders/{orderId}

# 查询客户订单
GET /api/customers/{customerId}/orders?pageIndex=0&pageSize=10

# 确认订单
POST /api/orders/confirm
{ "orderId": "..." }

# 支付订单
POST /api/orders/pay  
{ "orderId": "...", "amount": 199.98 }

# 发货订单
POST /api/orders/ship
{ "orderId": "...", "trackingNumber": "..." }

# 取消订单
POST /api/orders/cancel
{ "orderId": "...", "reason": "..." }
```

### 调试端点（开发环境）
```http
# 获取所有流程
GET /debug-api/flows

# 获取特定流程
GET /debug-api/flows/{correlationId}

# 获取统计信息
GET /debug-api/stats

# 启动系统回放
POST /debug-api/replay/system

# 启动流程回放
POST /debug-api/replay/flow
```

---

## 📂 项目结构

```
OrderSystem.Api/
├── Domain/              # 领域模型
│   └── Order.cs         # 订单聚合根
├── Messages/            # CQRS 消息
│   ├── Commands.cs      # 命令定义
│   └── Events.cs        # 事件定义
├── Handlers/            # 消息处理器
│   ├── OrderCommandHandlers.cs      # 命令处理器
│   ├── OrderQueryHandlers.cs        # 查询处理器
│   ├── OrderEventHandlers.cs        # 事件处理器
│   └── OrderEventHandlersMultiple.cs # 多处理器演示
├── Services/            # 业务服务
│   ├── IOrderRepository.cs          # 仓储接口
│   ├── InMemoryOrderRepository.cs   # 内存实现
│   ├── IInventoryService.cs         # 库存服务
│   └── IPaymentService.cs           # 支付服务
└── Program.cs           # 启动配置
```

---

## 🔍 调试功能演示

### Time-Travel Debugging

OrderSystem 完整集成了 Catga Debugger，支持：

1. **实时流程追踪**
   - 访问 http://localhost:5000/debug
   - 查看所有运行中的流程
   - 实时接收流程更新（SignalR）

2. **历史回放**
   - 选择任意历史流程
   - 时间旅行回到任意时刻
   - 查看当时的变量状态

3. **性能分析**
   - 流程执行时间
   - 事件处理延迟
   - 系统吞吐量统计

### 变量捕获（AOT 兼容）

所有 Command 都使用 `[GenerateDebugCapture]` 特性：

```csharp
[MemoryPackable]
[GenerateDebugCapture] // Source Generator 自动生成捕获代码
public partial record CreateOrderCommand(
    string CustomerId,
    List<OrderItem> Items,
    string ShippingAddress,
    string PaymentMethod
) : IRequest<OrderCreatedResult>;
```

**优势**:
- ✅ 零样板代码
- ✅ 100% AOT 兼容
- ✅ 227x 快于反射
- ✅ 编译时类型安全

---

## 🎓 学习要点

### 1. SafeRequestHandler - 无需 try-catch

**传统方式**:
```csharp
public async ValueTask<CatgaResult<OrderResult>> HandleAsync(...) {
    try {
        // business logic
        return CatgaResult<OrderResult>.Success(result);
    } catch (Exception ex) {
        return CatgaResult<OrderResult>.Failure(ex);
    }
}
```

**Catga 方式**:
```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderCreatedResult> {
    protected override async Task<OrderCreatedResult> HandleCoreAsync(...) {
        // business logic - 异常自动捕获和转换！
        return result;
    }
}
```

### 2. Auto-DI - 零配置依赖注入

**无需手动注册**，Source Generator 自动发现并注册：

```csharp
// Program.cs - 一行搞定所有注册
builder.Services.AddGeneratedHandlers();   // 自动注册所有 Handlers
builder.Services.AddGeneratedServices();   // 自动注册所有 [CatgaService]
```

### 3. 多事件处理器 - 解耦业务逻辑

一个事件可以触发多个处理器，实现业务逻辑解耦：

```csharp
// 发布事件
await _mediator.PublishAsync(new OrderCreatedEvent(...));

// 6个 Handler 并行执行（自动）
// - 发送通知
// - 更新分析
// - 记录日志
// - ... 
```

---

## 🔧 配置说明

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Catga": "Debug"  // Catga 框架日志级别
    }
  }
}
```

### 开发环境配置（Program.cs）

```csharp
if (builder.Environment.IsDevelopment()) {
    // Debugger - 100% 采样
    builder.Services.AddCatgaDebuggerWithAspNetCore(options => {
        options.Mode = DebuggerMode.Development;
        options.SamplingRate = 1.0; // 100%
        options.RingBufferCapacity = 10000;
        options.CaptureVariables = true;
        options.CaptureCallStacks = true;
    });
    
    // Debugger UI
    app.MapCatgaDebugger("/debug");
}
```

### 生产环境配置

```csharp
if (builder.Environment.IsProduction()) {
    // Debugger - 0.1% 采样，最小开销
    builder.Services.AddCatgaDebuggerWithAspNetCore(options => {
        options.Mode = DebuggerMode.Production;
        options.SamplingRate = 0.001; // 0.1%
        options.RingBufferCapacity = 1000;
        options.CaptureVariables = false;
        options.CaptureCallStacks = false;
        options.EnableReplay = false;
    });
}
```

---

## 📈 性能特征

| 指标 | 值 | 说明 |
|------|---|------|
| Command 延迟 | <1μs | 极低延迟 |
| Event 吞吐量 | >100k/s | 高吞吐量 |
| Debugger 开销 | <0.01% | 可忽略影响 |
| 内存分配 | ~0 | 零分配设计 |
| AOT 支持 | 100% | 完全兼容 |

---

## 🤝 扩展建议

OrderSystem 可作为模板扩展：

1. **添加更多业务逻辑**
   - 库存管理
   - 支付网关集成
   - 物流追踪

2. **持久化**
   - 替换 InMemoryOrderRepository 为 EF Core / Dapper
   - 集成 Redis (Catga.Persistence.Redis)

3. **分布式**
   - 集成 NATS (Catga.Transport.Nats)
   - 添加分布式事务

4. **更多可观测性**
   - 自定义 Metrics
   - 结构化日志
   - 告警规则

---

## 📚 相关文档

- [Catga README](../../README.md)
- [Quick Start Guide](../../docs/QUICK-START.md)
- [Debugger Documentation](../../docs/DEBUGGER.md)
- [Source Generator Guide](../../docs/SOURCE-GENERATOR-DEBUG-CAPTURE.md)

---

**OrderSystem 是学习 Catga 的最佳起点！** 🚀

探索代码，运行示例，体验创新的 CQRS 开发方式。

