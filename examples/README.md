# Catga 示例项目

> 通过实际示例学习 Catga

[返回主文档](../README.md)

---

## 🎯 示例概览

| 示例 | 描述 | 难度 | 技术栈 |
|------|------|------|--------|
| [OrderSystem](#-ordersystem) | 完整的电商订单系统 | ⭐⭐⭐ | CQRS, Event Sourcing, Distributed Tracing |
| [OrderSystem.AppHost](#-ordersystemapphost) | .NET Aspire 编排示例 | ⭐⭐ | .NET Aspire, Service Discovery |

---

## 📦 OrderSystem

**完整的生产级电商订单系统示例**

### 功能特性

✅ **CQRS 模式**
- Command: `CreateOrder`, `UpdateOrder`, `CancelOrder`
- Query: `GetOrder`, `GetOrdersByUser`, `GetOrderStats`
- Event: `OrderCreated`, `OrderUpdated`, `OrderCancelled`

✅ **事件溯源**
- 完整的事件存储
- 事件重放
- 快照机制

✅ **分布式追踪**
- OpenTelemetry 集成
- 完整的调用链追踪
- 性能指标收集

✅ **幂等性保证**
- ShardedIdempotencyStore
- 消息去重
- 重试安全

✅ **可观测性**
- 结构化日志
- 指标收集
- 健康检查

### 项目结构

```
OrderSystem/
├── Program.cs              # Application entry point
├── OrderMessages.cs        # Commands, Queries, Events
├── OrderHandlers.cs        # Request & Event handlers
├── OrderDbContext.cs       # EF Core DbContext
├── appsettings.json        # Configuration
└── README.md               # Documentation
```

### 核心代码

**消息定义：**

```csharp
// Commands
public record CreateOrder(string OrderId, string UserId, List<OrderItem> Items) : IRequest<OrderDto>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

// Queries
public record GetOrder(string OrderId) : IRequest<OrderDto>;

public record GetOrderStats() : IRequest<OrderStatsDto>;

// Events
public record OrderCreated(string OrderId, string UserId, decimal TotalAmount, DateTime CreatedAt) : IEvent;
```

**Handler 实现：**

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderDto>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public async ValueTask<CatgaResult<OrderDto>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        // Validate
        if (request.Items.Count == 0)
            return CatgaResult<OrderDto>.Failure("Order must have at least one item");

        // Create order
        var order = new Order
        {
            Id = request.OrderId,
            UserId = request.UserId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Price * i.Quantity),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        // Publish event
        await _mediator.PublishAsync(new OrderCreated(
            order.Id,
            order.UserId,
            order.TotalAmount,
            order.CreatedAt));

        _logger.LogInformation("Order {OrderId} created for user {UserId}", order.Id, order.UserId);

        return CatgaResult<OrderDto>.Success(MapToDto(order));
    }
}
```

### 运行示例

```bash
cd examples/OrderSystem

# 启动应用
dotnet run

# 测试 API
curl -X POST http://localhost:5000/api/orders/create \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-001",
    "userId": "user-123",
    "items": [
      { "productId": "prod-1", "quantity": 2, "price": 29.99 }
    ]
  }'

# 查询订单
curl http://localhost:5000/api/orders/ORD-001

# 查看统计
curl http://localhost:5000/api/orders/stats
```

### API 端点

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/orders/create` | 创建订单 |
| POST | `/api/orders/update` | 更新订单 |
| POST | `/api/orders/cancel` | 取消订单 |
| GET | `/api/orders/{id}` | 查询订单 |
| GET | `/api/orders/user/{userId}` | 查询用户订单 |
| GET | `/api/orders/stats` | 订单统计 |
| GET | `/health` | 健康检查 |
| GET | `/catga/nodes` | 节点信息 |

### 配置选项

```json
{
  "Catga": {
    "Transport": {
      "Type": "InMemory",
      "Nats": {
        "Url": "nats://localhost:4222",
        "SubjectPrefix": "orders."
      }
    },
    "Idempotency": {
      "ShardCount": 32,
      "RetentionPeriod": "24:00:00"
    },
    "Observability": {
      "EnableTracing": true,
      "EnableMetrics": true,
      "EnableLogging": true
    }
  },
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=orders.db"
  }
}
```

### 性能基准

| Operation | Latency (p50) | Latency (p99) | Throughput |
|-----------|---------------|---------------|------------|
| CreateOrder | 2ms | 5ms | 5000 req/s |
| GetOrder | 0.5ms | 1ms | 20000 req/s |
| GetOrderStats | 1ms | 3ms | 10000 req/s |

---

## 🎨 OrderSystem.AppHost

**.NET Aspire 编排示例**

### 功能特性

✅ **服务编排**
- OrderSystem 服务
- NATS 消息队列
- Redis 缓存
- PostgreSQL 数据库

✅ **服务发现**
- 自动服务注册
- 健康检查
- 负载均衡

✅ **可观测性**
- 集中式日志
- 分布式追踪
- 性能指标

### 项目结构

```
OrderSystem.AppHost/
├── Program.cs              # Aspire orchestration
├── appsettings.json        # Configuration
└── README.md               # Documentation

OrderSystem.ServiceDefaults/
└── Extensions.cs           # Shared service configurations
```

### 核心代码

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add infrastructure
var nats = builder.AddNats("nats")
    .WithDataVolume();

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("ordersdb");

// Add Order Service
var orderService = builder.AddProject<Projects.OrderSystem>("order-service")
    .WithReference(nats)
    .WithReference(redis)
    .WithReference(postgres)
    .WithReplicas(3);  // 3 instances for load balancing

builder.Build().Run();
```

### 运行示例

```bash
cd examples/OrderSystem.AppHost

# 启动 Aspire
dotnet run

# 访问 Aspire Dashboard
# http://localhost:15888
```

### Aspire Dashboard 功能

- 📊 **服务视图** - 查看所有服务状态
- 📈 **指标监控** - 实时性能指标
- 🔍 **追踪查看** - 分布式调用链
- 📝 **日志聚合** - 集中式日志查询
- 💚 **健康检查** - 服务健康状态

---

## 🎓 学习路径

### 初级

1. **阅读主 README** - 了解基本概念
2. **运行 OrderSystem** - 理解 CQRS 模式
3. **修改 Handler** - 添加自己的业务逻辑

### 中级

4. **添加新的 Command** - 实现自定义命令
5. **集成 NATS** - 配置分布式消息
6. **添加 Pipeline Behavior** - 实现自定义中间件

### 高级

7. **Event Sourcing** - 实现事件溯源
8. **RPC 调用** - 跨服务通信
9. **Native AOT** - 发布 AOT 应用

---

## 📚 相关文档

- [快速开始](../QUICK-REFERENCE.md)
- [架构设计](../docs/architecture/ARCHITECTURE.md)
- [CQRS 模式](../docs/architecture/cqrs.md)
- [API 文档](../docs/api/README.md)
- [性能基准](../benchmarks/Catga.Benchmarks/)

---

## 🤝 贡献

欢迎贡献更多示例！

**示例要求：**
- ✅ 完整的 README
- ✅ 清晰的代码注释
- ✅ 可运行的测试
- ✅ 实际的业务场景

请查看 [CONTRIBUTING.md](../CONTRIBUTING.md) 了解详情。

---

<div align="center">

[返回主文档](../README.md) · [快速开始](../QUICK-REFERENCE.md) · [架构设计](../docs/architecture/ARCHITECTURE.md)

**通过示例学习 Catga！** 🚀

</div>
