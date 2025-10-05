# 🚀 5分钟快速开始

> 从零到第一个 Catga CQRS 应用，只需 5 分钟！

## 📦 第一步：安装包

选择你需要的组件：

```bash
# ✅ 必需：核心框架
dotnet add package Catga

# 🌐 可选：NATS 分布式消息传递
dotnet add package Catga.Nats

# 🗄️ 可选：Redis 状态存储
dotnet add package Catga.Redis
```

## 🎯 第二步：定义消息

创建你的第一个 CQRS 消息：

```csharp
using Catga.Messages;

// 📝 命令：创建订单
public record CreateOrderCommand : MessageBase, ICommand<OrderResult>
{
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

// 🔍 查询：获取订单
public record GetOrderQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

// 📢 事件：订单已创建
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

// 📊 响应模型
public record OrderResult
{
    public string OrderId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record OrderDto
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
```

## 🔧 第三步：实现处理器

编写业务逻辑处理器：

```csharp
using Catga.Handlers;
using Catga.Results;
using Microsoft.Extensions.Logging;

// 📝 命令处理器
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
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

    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("创建订单: {CustomerId} - {ProductId} x{Quantity}",
            request.CustomerId, request.ProductId, request.Quantity);

        // 🔄 模拟业务逻辑
        await Task.Delay(50, cancellationToken);

        var orderId = Guid.NewGuid().ToString("N")[..8];
        var amount = request.Quantity * 99.99m; // 简单计算

        // 📢 发布事件
        await _mediator.PublishAsync(new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            Amount = amount
        }, cancellationToken);

        return CatgaResult<OrderResult>.Success(new OrderResult
        {
            OrderId = orderId,
            Status = "已创建",
            CreatedAt = DateTime.UtcNow
        });
    }
}

// 🔍 查询处理器
public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly ILogger<GetOrderHandler> _logger;

    public GetOrderHandler(ILogger<GetOrderHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<OrderDto>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("查询订单: {OrderId}", request.OrderId);

        // 🔄 模拟数据库查询
        await Task.Delay(20, cancellationToken);

        // 简单演示：生成模拟数据
        return CatgaResult<OrderDto>.Success(new OrderDto
        {
            OrderId = request.OrderId,
            CustomerId = "CUST001",
            ProductId = "PROD001",
            Quantity = 2,
            Amount = 199.98m,
            Status = "已创建",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
    }
}

// 📢 事件处理器
public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedNotificationHandler> _logger;

    public OrderCreatedNotificationHandler(ILogger<OrderCreatedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎉 订单创建通知: {OrderId} - 金额: ¥{Amount:F2}",
            @event.OrderId, @event.Amount);

        // 这里可以发送邮件、短信等通知
        return Task.CompletedTask;
    }
}
```

## ⚙️ 第四步：配置服务

在 `Program.cs` 中配置 Catga：

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 🚀 添加 Catga 服务
builder.Services.AddCatga(options =>
{
    // 开发环境：启用详细日志和追踪
    options.EnableLogging = true;
    options.EnableTracing = true;
    options.EnableValidation = true;

    // 性能优化：适度并发
    options.MaxConcurrentRequests = 1000;
});

// 📝 注册处理器
builder.Services.AddRequestHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>();
builder.Services.AddRequestHandler<GetOrderQuery, OrderDto, GetOrderHandler>();
builder.Services.AddEventHandler<OrderCreatedEvent, OrderCreatedNotificationHandler>();

// 🌐 添加 Web API 支持
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 🔧 配置管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
```

## 🌐 第五步：创建 API 控制器

创建 REST API 端点：

```csharp
using Microsoft.AspNetCore.Mvc;
using Catga;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ICatgaMediator mediator,
        ILogger<OrdersController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// 创建新订单
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResult), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var command = new CreateOrderCommand
        {
            CustomerId = request.CustomerId,
            ProductId = request.ProductId,
            Quantity = request.Quantity
        };

        var result = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new ErrorResponse { Error = result.Error });
    }

    /// <summary>
    /// 获取订单详情
    /// </summary>
    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(OrderDto), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<IActionResult> GetOrder(string orderId)
    {
        var query = new GetOrderQuery { OrderId = orderId };
        var result = await _mediator.SendAsync<GetOrderQuery, OrderDto>(query);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new ErrorResponse { Error = result.Error });
    }

    /// <summary>
    /// 健康检查端点
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }
}

// 📝 请求模型
public record CreateOrderRequest
{
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

public record ErrorResponse
{
    public string Error { get; init; } = string.Empty;
}
```

## 🎮 第六步：运行和测试

启动你的应用：

```bash
# 🚀 启动应用
dotnet run

# 🌐 访问 Swagger UI
# https://localhost:7xxx/swagger
```

测试 API：

```bash
# 📝 创建订单
curl -X POST "https://localhost:7xxx/api/orders" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "CUST001",
    "productId": "PROD001",
    "quantity": 2
  }'

# 🔍 查询订单
curl -X GET "https://localhost:7xxx/api/orders/12345678"
```

## 🎯 进阶配置

### 💪 高性能配置

```csharp
builder.Services.AddCatga(options =>
{
    // 🚀 极简配置：最大性能
    options.Minimal();

    // 或者自定义高性能配置
    options.EnableLogging = false;           // 关闭日志以提升性能
    options.EnableTracing = false;           // 关闭追踪
    options.EnableIdempotency = false;       // 关闭幂等性检查
    options.MaxConcurrentRequests = 0;       // 无并发限制
});
```

### 🛡️ 生产环境配置

```csharp
builder.Services.AddCatga(options =>
{
    // 🏢 生产环境：平衡性能和可靠性
    options.EnableLogging = true;
    options.EnableTracing = true;
    options.EnableIdempotency = true;
    options.EnableValidation = true;
    options.EnableRetry = true;
    options.EnableCircuitBreaker = true;
    options.EnableRateLimiting = true;

    // 性能参数
    options.MaxConcurrentRequests = 2000;
    options.IdempotencyShardCount = 64;
    options.RateLimitRequestsPerSecond = 1000;
});
```

### 🌐 分布式配置

```csharp
// 添加 NATS 支持
builder.Services.AddNatsCatga(options =>
{
    options.Url = "nats://localhost:4222";
    options.ServiceId = "order-service";
});

// 发布跨服务事件
await _mediator.PublishAsync(new OrderCreatedEvent
{
    OrderId = orderId,
    CustomerId = customerId,
    Amount = amount
});
```

## ✅ 验证安装

运行这个简单测试来验证一切正常：

```csharp
// 在控制器或服务中测试
public async Task<bool> TestCatga()
{
    try
    {
        var command = new CreateOrderCommand
        {
            CustomerId = "TEST001",
            ProductId = "TEST001",
            Quantity = 1
        };

        var result = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        return result.IsSuccess;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Catga 测试失败");
        return false;
    }
}
```

## 🎉 恭喜！

你已经成功创建了第一个 Catga CQRS 应用！

### 📚 下一步学习

| 🎯 目标 | 📖 推荐阅读 | ⏱️ 时间 |
|---------|-------------|--------|
| **理解架构** | [CQRS 模式详解](../architecture/cqrs.md) | 15分钟 |
| **管道行为** | [Pipeline 行为](../architecture/pipeline-behaviors.md) | 20分钟 |
| **分布式事务** | [CatGa 分布式事务](distributed-transactions.md) | 30分钟 |
| **完整示例** | [OrderApi 示例](../../examples/OrderApi/README.md) | 30分钟 |
| **微服务架构** | [分布式示例](../../examples/NatsDistributed/README.md) | 1小时 |

### 🆘 遇到问题？

- 🔍 **搜索文档**: [文档中心](../README.md)
- 💬 **社区讨论**: [GitHub Discussions](https://github.com/your-org/Catga/discussions)
- 🐛 **报告问题**: [GitHub Issues](https://github.com/your-org/Catga/issues)
- 📧 **技术支持**: support@catga.dev

### 🌟 更多资源

- 📺 [视频教程](https://youtube.com/@catga-framework)
- 💬 [Discord 社区](https://discord.gg/catga)
- 📱 [Twitter](https://twitter.com/CatgaFramework)

---

<div align="center">

**🚀 开始构建更好的分布式系统！**

*用 Catga，让 CQRS 变得简单而强大* ✨

</div>

