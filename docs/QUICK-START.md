# Catga 5 分钟快速入门

**从零到生产级 CQRS 应用，只需 5 分钟！**

---

## 📦 第 1 步：安装包（30 秒）

```bash
# 创建新项目
dotnet new webapi -n MyApp
cd MyApp

# 安装 Catga 核心包
dotnet add package Catga
dotnet add package Catga.InMemory
dotnet add package Catga.Serialization.MemoryPack
dotnet add package Catga.SourceGenerator
dotnet add package Catga.AspNetCore

# 可选：开发时调试
dotnet add package Catga.Debugger
dotnet add package Catga.Debugger.AspNetCore
```

---

## 🔧 第 2 步：配置 Catga（1 分钟）

在 `Program.cs` 中添加：

```csharp
using Catga;
using Catga.AspNetCore;
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ===== 配置 Catga（只需 3 行）=====
builder.Services.AddCatga()           // 1. 添加核心服务
    .UseMemoryPack()                  // 2. 配置序列化器（100% AOT）
    .ForDevelopment();                // 3. 开发环境配置

builder.Services.AddInMemoryTransport(); // 内存传输（生产环境用 NATS）

// 自动注册所有 Handler 和 Service（Source Generator）
builder.Services.AddGeneratedHandlers();
builder.Services.AddGeneratedServices();

// 可选：启用时间旅行调试器
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore();
}

var app = builder.Build();

// 可选：映射调试器 UI
if (app.Environment.IsDevelopment())
{
    app.MapCatgaDebugger("/debug");  // http://localhost:5000/debug
}

app.Run();
```

---

## 📝 第 3 步：定义消息（1 分钟）

创建 `Messages.cs`：

```csharp
using Catga.Messages;
using Catga.Results;
using MemoryPack;

// 命令：创建订单
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId,
    decimal Amount
) : IRequest<OrderResult>;

// 查询：获取订单
[MemoryPackable]
public partial record GetOrderQuery(
    string OrderId
) : IRequest<Order?>;

// 事件：订单已创建（通知其他服务）
[MemoryPackable]
public partial record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal Amount
) : INotification;

// 返回结果
[MemoryPackable]
public partial record OrderResult(string OrderId);

[MemoryPackable]
public partial record Order(
    string OrderId,
    string CustomerId,
    decimal Amount,
    DateTime CreatedAt
);
```

---

## ⚡ 第 4 步：实现 Handler（2 分钟）

创建 `Handlers.cs`：

```csharp
using Catga;
using Catga.Handlers;
using Catga.Results;

// ===== 命令 Handler：创建订单 =====
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly ILogger<CreateOrderHandler> _logger;
    private readonly ICatgaMediator _mediator;

    public CreateOrderHandler(
        ILogger<CreateOrderHandler> logger,
        ICatgaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    protected override async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 创建订单
        var orderId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("Creating order {OrderId} for {CustomerId}", orderId, request.CustomerId);

        // 2. 发布事件（异步通知其他服务）
        await _mediator.PublishAsync(new OrderCreatedEvent(
            orderId,
            request.CustomerId,
            request.Amount
        ), cancellationToken);

        // 3. 返回结果
        return CatgaResult<OrderResult>.Success(new OrderResult(orderId));
    }
}

// ===== 查询 Handler：获取订单 =====
public class GetOrderHandler : SafeRequestHandler<GetOrderQuery, Order?>
{
    protected override Task<CatgaResult<Order?>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken)
    {
        // 模拟从数据库查询
        var order = new Order(
            request.OrderId,
            "CUST-001",
            99.99m,
            DateTime.UtcNow
        );

        return Task.FromResult(CatgaResult<Order?>.Success(order));
    }
}

// ===== 事件 Handler：发送通知 =====
public class SendOrderNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<SendOrderNotificationHandler> _logger;

    public SendOrderNotificationHandler(ILogger<SendOrderNotificationHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📧 Sending notification for order {@Event}", @event);
        // 发送邮件、短信等
        return Task.CompletedTask;
    }
}

// ===== 事件 Handler：记录审计日志 =====
public class AuditOrderHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<AuditOrderHandler> _logger;

    public AuditOrderHandler(ILogger<AuditOrderHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📝 Auditing order creation: {@Event}", @event);
        // 写入审计表
        return Task.CompletedTask;
    }
}
```

---

## 🚀 第 5 步：添加 API 端点（1 分钟）

在 `Program.cs` 的 `app.Run()` 之前添加：

```csharp
// ===== 映射 Catga 端点 =====

// POST /api/orders - 创建订单
app.MapCatgaRequest<CreateOrderCommand, OrderResult>("/api/orders")
    .WithName("CreateOrder")
    .WithTags("Orders");

// GET /api/orders/{orderId} - 查询订单
app.MapCatgaQuery<GetOrderQuery, Order?>("/api/orders/{orderId}")
    .WithName("GetOrder")
    .WithTags("Orders");
```

---

## ✅ 第 6 步：运行和测试（30 秒）

```bash
# 运行应用
dotnet run

# 测试创建订单
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "CUST-001", "amount": 99.99}'

# 响应：
# {"orderId": "abc123..."}

# 测试查询订单
curl http://localhost:5000/api/orders/abc123

# 响应：
# {"orderId": "abc123", "customerId": "CUST-001", "amount": 99.99, "createdAt": "2024-01-01T10:00:00Z"}
```

---

## 🎉 完成！你已经拥有：

✅ **CQRS 架构** - 清晰的命令/查询分离  
✅ **事件驱动** - 多个 Handler 响应同一事件  
✅ **零反射** - 100% Source Generator 自动注册  
✅ **类型安全** - 编译时检查，运行时零错误  
✅ **高性能** - < 1μs 命令处理，零内存分配  
✅ **可观测性** - 内置日志、追踪  
✅ **调试友好** - 时间旅行调试器（开发环境）  

---

## 🌟 体验时间旅行调试器

访问 `http://localhost:5000/debug`，你将看到：

- 📊 **实时流程图** - 每个请求的完整执行流程
- 🔍 **变量查看器** - 捕获所有变量快照
- ⏪ **时间旅行** - 回到任意时刻，重放执行过程
- 📈 **性能监控** - CPU、内存、延迟统计
- 🌐 **系统拓扑** - 服务间调用关系

**零配置，开箱即用！**

---

## 📚 下一步

### 进阶功能

1. **添加持久化**：
   ```csharp
   builder.Services.AddRedisEventStore(options => 
       options.ConnectionString = "localhost:6379");
   ```

2. **切换到 NATS 传输**：
   ```csharp
   builder.Services.AddNatsTransport(options => 
       options.Url = "nats://localhost:4222");
   ```

3. **启用分布式追踪**：
   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithTracing(tracing => tracing.AddCatgaInstrumentation());
   ```

4. **生产环境配置**：
   ```csharp
   builder.Services.AddCatga()
       .UseMemoryPack()
       .ForProduction()  // 生产环境优化
       .UseGracefulLifecycle();  // 优雅关闭
   ```

### 学习资源

- 📖 **[完整示例：OrderSystem](../examples/README-ORDERSYSTEM.md)** - 420+ 行详细指南
- 📚 **[文档索引](INDEX.md)** - 85+ 篇文档
- 🎯 **[API 速查](QUICK-REFERENCE.md)** - 常用 API 速查表
- 🌟 **[Debugger 完整指南](DEBUGGER.md)** - 时间旅行调试详解
- 🏗️ **[架构概览](architecture/ARCHITECTURE.md)** - 系统设计详解

### 常见场景

- **[分布式事务](patterns/DISTRIBUTED-TRANSACTION-V2.md)** - Catga Transaction 模式
- **[事件溯源](guides/event-sourcing.md)** - Event Store 集成
- **[读模型投影](guides/read-model-projection.md)** - CQRS 读写分离
- **[批量操作](../src/Catga/Core/BatchOperationExtensions.cs)** - 高性能批处理
- **[Native AOT 部署](deployment/native-aot-publishing.md)** - 毫秒级启动

---

## 💡 核心设计理念

### 1. 零配置（Zero Config）
- ✅ Source Generator 自动发现和注册
- ✅ 约定优于配置
- ✅ 开箱即用的默认值

### 2. 类型安全（Type Safety）
- ✅ 编译时检查（Roslyn Analyzer）
- ✅ 强类型消息契约
- ✅ 泛型约束保证正确性

### 3. 高性能（High Performance）
- ✅ < 1μs 命令处理
- ✅ 零内存分配设计
- ✅ 100% AOT 编译支持

### 4. 可观测性（Observability）
- ✅ OpenTelemetry 原生支持
- ✅ 结构化日志
- ✅ 时间旅行调试器

### 5. 生产就绪（Production Ready）
- ✅ 优雅关闭和恢复
- ✅ 健康检查
- ✅ 弹性和重试策略

---

## 🤝 需要帮助？

- 💬 [讨论区](https://github.com/catga/catga/discussions) - 提问和分享
- 🐛 [Issue Tracker](https://github.com/catga/catga/issues) - 报告 Bug
- 📧 [Email](mailto:support@catga.dev) - 商业支持
- 📚 [完整文档](INDEX.md) - 详细指南

---

<div align="center">

**🎉 恭喜！你已经掌握了 Catga 的基础用法！**

[完整示例](../examples/README-ORDERSYSTEM.md) · [文档索引](INDEX.md) · [API 速查](QUICK-REFERENCE.md)

</div>

