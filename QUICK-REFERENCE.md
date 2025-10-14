# Catga API 速查

**快速查找常用 API 和模式**

---

## 📦 安装

```bash
# 最小安装 (本地开发)
dotnet add package Catga
dotnet add package Catga.InMemory
dotnet add package Catga.Serialization.MemoryPack
dotnet add package Catga.SourceGenerator

# 生产环境
dotnet add package Catga.Transport.Nats
dotnet add package Catga.Persistence.Redis
dotnet add package Catga.AspNetCore
```

---

## 🚀 配置

### 基础配置

```csharp
// Program.cs
using Catga;
using Catga.InMemory;
using Catga.Serialization.MemoryPack;

builder.Services
    .AddCatga()                  // 核心服务
    .AddInMemoryTransport()      // 传输层
    .UseMemoryPackSerializer();  // 序列化
```

### 生产配置

```csharp
builder.Services
    .AddCatga()
    .AddNatsTransport(options =>
    {
        options.Url = "nats://localhost:4222";
        options.SubjectPrefix = "myapp";
    })
    .UseMemoryPackSerializer()
    .AddRedisIdempotencyStore()
    .AddRedisDistributedCache()
    .AddObservability();  // ActivitySource + Meter + Logging
```

---

## 📨 消息定义

### Command (有返回值)

```csharp
using MemoryPack;
using Catga.Messages;
using Catga.Results;

[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : ICommand<CatgaResult<OrderCreated>>;

[MemoryPackable]
public partial record OrderCreated(string OrderId, DateTime CreatedAt);
```

### Query (查询)

```csharp
[MemoryPackable]
public partial record GetOrderById(string OrderId)
    : IQuery<CatgaResult<OrderDetail>>;

[MemoryPackable]
public partial record OrderDetail(string OrderId, decimal Amount, string Status);
```

### Event (无返回值)

```csharp
[MemoryPackable]
public partial record OrderCreatedEvent(string OrderId, DateTime OccurredAt)
    : IEvent;
```

### 指定 QoS

```csharp
[MemoryPackable]
public partial record ImportantCommand(string Data) : ICommand<CatgaResult<bool>>
{
    // AtMostOnce (QoS 0) - 默认，最快
    // AtLeastOnce (QoS 1) - 至少一次
    // ExactlyOnce (QoS 2) - 恰好一次
    public QualityOfService QoS => QualityOfService.ExactlyOnce;
}
```

---

## 🎯 Handler 实现

### Command Handler

```csharp
public class CreateOrderHandler
    : IRequestHandler<CreateOrder, CatgaResult<OrderCreated>>
{
    private readonly ILogger<CreateOrderHandler> _logger;
    private readonly IOrderRepository _repository;

    public CreateOrderHandler(
        ILogger<CreateOrderHandler> logger,
        IOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async ValueTask<CatgaResult<OrderCreated>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 业务逻辑
            await _repository.CreateAsync(request.OrderId, request.Amount);

            var result = new OrderCreated(request.OrderId, DateTime.UtcNow);
            return CatgaResult<OrderCreated>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建订单失败: {OrderId}", request.OrderId);
            return CatgaResult<OrderCreated>.Failure("创建失败", ex);
        }
    }
}
```

### Event Handler

```csharp
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public OrderCreatedEventHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async ValueTask HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        // Event handler 不返回值
        await _emailService.SendOrderConfirmationAsync(@event.OrderId);
    }
}
```

### 多个 Event Handler

```csharp
// Handler 1: 发送邮件
public class EmailNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        // 发送邮件
    }
}

// Handler 2: 更新统计
public class StatisticsHandler : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        // 更新统计
    }
}

// 两个 Handler 都会被调用
```

---

## 🔄 使用 Mediator

### 发送 Command/Query

```csharp
public class OrderController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrderController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var command = new CreateOrder(Guid.NewGuid().ToString(), request.Amount);

        // 发送 Command
        var result = await _mediator.SendAsync<CreateOrder, OrderCreated>(command);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BadRequest(new { error = result.Error });
    }

    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrder(string id)
    {
        var query = new GetOrderById(id);

        // 发送 Query
        var result = await _mediator.SendAsync<GetOrderById, OrderDetail>(query);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound();
    }
}
```

### 发布 Event

```csharp
// 发布单个 Event
var @event = new OrderCreatedEvent(orderId, DateTime.UtcNow);
await _mediator.PublishAsync(@event);

// 批量发布 Event
var events = new[]
{
    new OrderCreatedEvent("ORD-001", DateTime.UtcNow),
    new OrderCreatedEvent("ORD-002", DateTime.UtcNow)
};
await _mediator.PublishBatchAsync(events);
```

---

## 🛡️ CatgaResult 模式

### 创建结果

```csharp
// 成功
return CatgaResult<OrderCreated>.Success(orderCreated);

// 成功 + 元数据
var metadata = new ResultMetadata();
metadata.Add("source", "api");
return CatgaResult<OrderCreated>.Success(orderCreated, metadata);

// 失败
return CatgaResult<OrderCreated>.Failure("订单不存在");

// 失败 + 异常
return CatgaResult<OrderCreated>.Failure("创建失败", exception);
```

### 处理结果

```csharp
var result = await _mediator.SendAsync<CreateOrder, OrderCreated>(command);

// 方式 1: IsSuccess
if (result.IsSuccess)
{
    var order = result.Value;
    Console.WriteLine($"订单 {order.OrderId} 已创建");
}
else
{
    Console.WriteLine($"错误: {result.Error}");
    if (result.Exception != null)
    {
        _logger.LogError(result.Exception, "详细错误");
    }
}

// 方式 2: Pattern Matching
var message = result switch
{
    { IsSuccess: true } => $"成功: {result.Value.OrderId}",
    { Exception: not null } => $"异常: {result.Exception.Message}",
    _ => $"失败: {result.Error}"
};
```

---

## 🔧 Pipeline Behaviors

### 日志 Behavior

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("处理请求: {RequestType}", typeof(TRequest).Name);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        _logger.LogInformation("请求完成: {RequestType}, 耗时: {Elapsed}ms",
            typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);

        return response;
    }
}

// 注册
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

### 验证 Behavior

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = _validators
            .Select(v => v.Validate(request))
            .SelectMany(result => result.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Any())
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
```

### 重试 Behavior

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

// 在消息中配置重试
[MemoryPackable]
public partial record CreateOrder(...) : ICommand<CatgaResult<OrderCreated>>
{
    public int MaxRetries => 3;
    public TimeSpan RetryDelay => TimeSpan.FromSeconds(1);
}
```

---

## 🔑 分布式 ID

### Snowflake ID 生成器

```csharp
// 注入
private readonly ISnowflakeIdGenerator _idGenerator;

// 生成单个 ID
long id = _idGenerator.NextId();  // ~80ns, 零分配

// 批量生成
Span<long> ids = stackalloc long[100];
_idGenerator.NextIds(ids);

// 解析 ID
var (timestamp, workerId, sequence) = _idGenerator.ParseId(id);
```

### 配置

```csharp
services.AddCatga(options =>
{
    options.WorkerId = 1;  // 0-1023
    options.Epoch = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
});
```

---

## 🌐 分布式特性

### 幂等性

```csharp
// 自动启用 - Command 会自动去重
[MemoryPackable]
public partial record CreateOrder(...) : ICommand<CatgaResult<OrderCreated>>;

// 配置过期时间
services.AddRedisIdempotencyStore(options =>
{
    options.DefaultExpiration = TimeSpan.FromHours(24);
});
```

### Dead Letter Queue (DLQ)

```csharp
services.AddCatga(options =>
{
    options.EnableDeadLetterQueue = true;
    options.MaxRetryCount = 3;
});

// 处理失败的消息
public class DeadLetterHandler : IEventHandler<MessageFailedEvent>
{
    public async ValueTask HandleAsync(MessageFailedEvent @event, CancellationToken ct)
    {
        // 记录、报警、重试...
    }
}
```

---

## 🔍 可观测性

### ActivitySource (分布式追踪)

```csharp
using var activity = CatgaActivitySource.Start("OrderProcessing");
activity?.SetTag("order.id", orderId);
activity?.SetTag("order.amount", amount);

try
{
    // 业务逻辑
    activity?.SetTag("result", "success");
}
catch (Exception ex)
{
    activity?.SetTag("result", "error");
    activity?.SetTag("error.message", ex.Message);
    throw;
}
```

### Meter (指标监控)

```csharp
// Counter
CatgaMeter.CommandCounter.Add(1,
    new KeyValuePair<string, object?>("command", "CreateOrder"),
    new KeyValuePair<string, object?>("status", "success")
);

// Histogram
CatgaMeter.CommandDuration.Record(elapsed.TotalMilliseconds,
    new KeyValuePair<string, object?>("command", "CreateOrder")
);
```

### LoggerMessage (结构化日志)

```csharp
public partial class OrderService
{
    private readonly ILogger<OrderService> _logger;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "处理订单 {OrderId}, 金额: {Amount}")]
    partial void LogProcessingOrder(string orderId, decimal amount);

    public async Task ProcessOrderAsync(CreateOrder order)
    {
        LogProcessingOrder(order.OrderId, order.Amount);
        // 处理逻辑...
    }
}
```

---

## 🌐 ASP.NET Core

### 基础集成

```csharp
// Program.cs
builder.Services
    .AddCatga()
    .AddInMemoryTransport()
    .UseMemoryPackSerializer();

// 添加 HTTP 端点
builder.Services.AddCatgaHttpEndpoints();

var app = builder.Build();

// 映射端点
app.MapCatgaEndpoints();

app.Run();
```

### 自定义路由

```csharp
app.MapCatgaEndpoints(options =>
{
    options.RoutePrefix = "api";  // /api/commands/{CommandType}
    options.EnableSwagger = true;
    options.RequireAuthorization = true;
});
```

### 手动端点

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    ICatgaMediator mediator) =>
{
    var command = new CreateOrder(Guid.NewGuid().ToString(), request.Amount);
    var result = await mediator.SendAsync<CreateOrder, OrderCreated>(command);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error);
});
```

---

## 🧪 测试

### 单元测试

```csharp
using Catga;
using Catga.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class OrderTests
{
    [Fact]
    public async Task CreateOrder_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga()
                .AddInMemoryTransport()
                .UseMemoryPackSerializer();

        services.AddTransient<IRequestHandler<CreateOrder, CatgaResult<OrderCreated>>,
                                CreateOrderHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var command = new CreateOrder("ORD-001", 99.99m);

        // Act
        var result = await mediator.SendAsync<CreateOrder, OrderCreated>(command);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ORD-001", result.Value.OrderId);
    }
}
```

### 集成测试

```csharp
public class OrderIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrderIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOrder_Via_Http_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new { OrderId = "ORD-001", Amount = 99.99m };

        // Act
        var response = await client.PostAsJsonAsync("/commands/CreateOrder", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OrderCreated>();
        Assert.NotNull(result);
        Assert.Equal("ORD-001", result.OrderId);
    }
}
```

---

## 🚀 部署

### Native AOT 发布

```bash
# 发布为 Native AOT
dotnet publish -c Release -r linux-x64 --property:PublishAot=true

# 验证 AOT 警告
dotnet publish -c Release -r linux-x64 /p:PublishAot=true /p:TreatWarningsAsErrors=true
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -r linux-x64 --property:PublishAot=true -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["./MyApp"]
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: my-catga-app
spec:
  replicas: 3
  selector:
    matchLabels:
      app: my-catga-app
  template:
    metadata:
      labels:
        app: my-catga-app
    spec:
      containers:
      - name: app
        image: my-catga-app:latest
        env:
        - name: NATS__Url
          value: "nats://nats:4222"
        - name: Redis__Connection
          value: "redis:6379"
        resources:
          limits:
            memory: "128Mi"  # AOT 占用极小
            cpu: "500m"
```

---

## 🔗 常用链接

- [完整文档](./docs/README.md)
- [架构说明](./docs/architecture/ARCHITECTURE.md)
- [示例代码](./examples/)
- [性能测试](./benchmarks/README.md)
- [更新日志](./CHANGELOG.md)
- [贡献指南](./CONTRIBUTING.md)

---

## ❓ 常见问题

**Q: 为什么选择 MemoryPack 而不是 JSON?**
A: MemoryPack 是 100% AOT 兼容的，性能比 JSON 快 10x，且零分配。JSON 需要 `JsonSerializerContext` 才能 AOT 兼容。

**Q: 如何处理消息版本演进?**
A: 使用 MemoryPack 的 `[MemoryPackable(GenerateType.VersionTolerant)]` 和可选字段。

**Q: 支持 Saga 模式吗?**
A: v1.0 支持基于事件的编排，完整 Saga 将在 v1.1 提供。

**Q: 性能真的这么好吗?**
A: 是的！查看 [benchmarks/README.md](./benchmarks/README.md) 获取详细测试数据。

**Q: 适合什么场景?**
A: 微服务、事件驱动架构、高性能 API、Native AOT 应用、云原生部署。

---

<div align="center">

**📖 完整文档**: [docs/README.md](./docs/README.md)
**🚀 开始使用**: [examples/](./examples/)
**⭐ Star**: [GitHub](https://github.com/Cricle/Catga)

</div>
