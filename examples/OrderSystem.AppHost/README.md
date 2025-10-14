# OrderSystem - Catga 完整示例

这是一个完整的订单系统示例，演示 Catga 框架的所有核心功能，使用 .NET Aspire 进行编排。

---

## 🎯 功能演示

- ✅ **CQRS 模式** - Command/Query/Event 完整实现
- ✅ **.NET Aspire** - 服务编排和可观测性
- ✅ **NATS JetStream** - 分布式消息传输
- ✅ **Redis** - 分布式缓存和持久化
- ✅ **MemoryPack** - 高性能序列化
- ✅ **ASP.NET Core** - HTTP API 集成
- ✅ **分布式 ID** - Snowflake ID 生成
- ✅ **幂等性** - 消息去重保证
- ✅ **可观测性** - OpenTelemetry 集成

---

## 🚀 快速运行

### 前置条件

- .NET 9 SDK
- Docker Desktop (用于 NATS 和 Redis)
- Visual Studio 2022 17.8+ 或 JetBrains Rider 2024.1+

### 运行步骤

```bash
# 1. 克隆项目
git clone https://github.com/Cricle/Catga.git
cd Catga/examples/OrderSystem.AppHost

# 2. 启动 (Aspire 会自动启动 NATS 和 Redis)
dotnet run

# 3. 打开浏览器
# - Aspire Dashboard: http://localhost:15888
# - API: http://localhost:5000
# - Swagger: http://localhost:5000/swagger
```

---

## 📊 项目结构

```
OrderSystem/
├── OrderSystem.AppHost/        # .NET Aspire 编排主机
│   ├── Program.cs              # Aspire 配置
│   └── appsettings.json
│
├── OrderSystem.Api/            # ASP.NET Core API
│   ├── Program.cs              # API 配置
│   ├── Controllers/
│   │   └── OrdersController.cs # 订单 API
│   └── appsettings.json
│
└── OrderSystem.Application/    # 业务逻辑
    ├── Commands/               # 命令
    │   ├── CreateOrder.cs
    │   ├── CancelOrder.cs
    │   └── Handlers/
    ├── Queries/                # 查询
    │   ├── GetOrderById.cs
    │   └── Handlers/
    ├── Events/                 # 事件
    │   ├── OrderCreated.cs
    │   ├── OrderCancelled.cs
    │   └── Handlers/
    └── Models/                 # 领域模型
        └── Order.cs
```

---

## 💡 核心代码示例

### 1. 定义消息

```csharp
// Commands/CreateOrder.cs
using MemoryPack;
using Catga.Messages;
using Catga.Results;

[MemoryPackable]
public partial record CreateOrder(
    string OrderId,
    string ProductName,
    int Quantity,
    decimal UnitPrice
) : ICommand<CatgaResult<OrderCreated>>;

// Events/OrderCreated.cs
[MemoryPackable]
public partial record OrderCreated(
    string OrderId,
    decimal TotalAmount,
    DateTime CreatedAt
) : IEvent;
```

### 2. 实现 Handler

```csharp
// Commands/Handlers/CreateOrderHandler.cs
public class CreateOrderHandler
    : IRequestHandler<CreateOrder, CatgaResult<OrderCreated>>
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

    public async ValueTask<CatgaResult<OrderCreated>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "创建订单: {OrderId}, 产品: {Product}, 数量: {Quantity}",
            request.OrderId, request.ProductName, request.Quantity);

        // 计算总金额
        var totalAmount = request.Quantity * request.UnitPrice;

        // 创建订单 (这里简化为直接成功)
        var orderCreated = new OrderCreated(
            request.OrderId,
            totalAmount,
            DateTime.UtcNow
        );

        // 发布事件
        await _mediator.PublishAsync(orderCreated, cancellationToken);

        return CatgaResult<OrderCreated>.Success(orderCreated);
    }
}
```

### 3. 配置服务 (Aspire)

```csharp
// OrderSystem.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// 添加基础设施
var nats = builder.AddNats("nats");
var redis = builder.AddRedis("redis");

// 添加 API 服务
builder.AddProject<Projects.OrderSystem_Api>("api")
       .WithReference(nats)
       .WithReference(redis);

builder.Build().Run();
```

### 4. 配置 Catga (API)

```csharp
// OrderSystem.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services
    .AddCatga()
    .AddNatsTransport(builder.Configuration.GetConnectionString("nats")!)
    .UseMemoryPackSerializer()
    .AddRedisIdempotencyStore()
    .AddRedisDistributedCache();

// 添加 Catga HTTP 端点
builder.Services.AddCatgaHttpEndpoints();

var app = builder.Build();

// 映射 Catga 端点
app.MapCatgaEndpoints();

app.Run();
```

---

## 🧪 测试 API

### 使用 Swagger

1. 打开 http://localhost:5000/swagger
2. 尝试 `POST /commands/CreateOrder`

### 使用 curl

```bash
# 创建订单
curl -X POST http://localhost:5000/commands/CreateOrder \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-001",
    "productName": "Laptop",
    "quantity": 2,
    "unitPrice": 999.99
  }'

# 查询订单
curl http://localhost:5000/queries/GetOrderById?orderId=ORD-001

# 取消订单
curl -X POST http://localhost:5000/commands/CancelOrder \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-001",
    "reason": "Customer requested"
  }'
```

### 使用 PowerShell

```powershell
# 创建订单
$body = @{
    orderId = "ORD-001"
    productName = "Laptop"
    quantity = 2
    unitPrice = 999.99
} | ConvertTo-Json

Invoke-RestMethod -Uri http://localhost:5000/commands/CreateOrder `
    -Method Post `
    -Body $body `
    -ContentType "application/json"
```

---

## 📊 Aspire Dashboard

Aspire Dashboard 提供完整的可观测性：

- **URL**: http://localhost:15888
- **功能**:
  - 📈 实时指标 (Metrics)
  - 🔍 分布式追踪 (Traces)
  - 📝 结构化日志 (Logs)
  - 🏥 健康检查 (Health Checks)

### 查看追踪

1. 打开 Aspire Dashboard
2. 点击 "Traces" 标签
3. 查看 "OrderProcessing" 追踪
4. 查看完整的调用链和性能数据

---

## 🔍 关键学习点

### 1. CQRS 分离

```csharp
// Command - 有副作用，修改状态
public record CreateOrder(...) : ICommand<CatgaResult<OrderCreated>>;

// Query - 无副作用，只读取
public record GetOrderById(...) : IQuery<CatgaResult<OrderDetail>>;

// Event - 已发生的事实
public record OrderCreated(...) : IEvent;
```

### 2. 幂等性

```csharp
// Catga 自动处理幂等性
// 重复发送相同的 CreateOrder，只会创建一次
var command = new CreateOrder("ORD-001", "Laptop", 2, 999.99m);
await mediator.SendAsync<CreateOrder, OrderCreated>(command);
await mediator.SendAsync<CreateOrder, OrderCreated>(command); // 幂等
```

### 3. 事件驱动

```csharp
// Handler 1: 发送邮件
public class EmailNotificationHandler : IEventHandler<OrderCreated>
{
    public async ValueTask HandleAsync(OrderCreated @event, CancellationToken ct)
    {
        // 发送确认邮件
    }
}

// Handler 2: 更新统计
public class StatisticsHandler : IEventHandler<OrderCreated>
{
    public async ValueTask HandleAsync(OrderCreated @event, CancellationToken ct)
    {
        // 更新订单统计
    }
}

// 两个 Handler 会并行执行
```

---

## 🚀 生产部署

### Docker 部署

```bash
# 构建镜像
docker build -t orderystem-api:latest -f OrderSystem.Api/Dockerfile .

# 运行容器
docker run -d \
  -p 5000:8080 \
  -e NATS__Url=nats://nats:4222 \
  -e Redis__Connection=redis:6379 \
  orderystem-api:latest
```

### Kubernetes 部署

参见 [Kubernetes 部署指南](../../docs/deployment/kubernetes.md)

---

## 📚 延伸阅读

- [CQRS 模式详解](../../docs/architecture/cqrs.md)
- [分布式架构](../../docs/distributed/ARCHITECTURE.md)
- [序列化指南](../../docs/guides/serialization.md)
- [.NET Aspire 文档](https://learn.microsoft.com/dotnet/aspire/)

---

## 🤝 反馈

有问题或建议？请在 [GitHub Issues](https://github.com/Cricle/Catga/issues) 中反馈。

---

<div align="center">

**🎉 Enjoy Building with Catga!**

[返回文档](../../docs/README.md) · [API 速查](../../QUICK-REFERENCE.md)

</div>
