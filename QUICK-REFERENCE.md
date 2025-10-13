# Catga 快速参考

> 5分钟从零到上手 Catga 🚀

[返回主文档](./README.md)

---

## 📦 安装

```bash
# Core packages (required)
dotnet add package Catga.InMemory
dotnet add package Catga.SourceGenerator

# Optional: ASP.NET Core integration
dotnet add package Catga.AspNetCore

# Optional: Distributed (choose one)
dotnet add package Catga.Transport.Nats
dotnet add package Catga.Distributed.Redis

# Optional: Persistence
dotnet add package Catga.Persistence.Redis

# Optional: Serialization (for AOT)
dotnet add package Catga.Serialization.MemoryPack
```

---

## 🎯 核心概念

### 消息类型

```csharp
// Command - Has return value, modifies state
public record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

// Query - Has return value, read-only
public record GetOrder(string OrderId) : IRequest<Order>;

// Event - No return value, notification
public record OrderCreated(string OrderId, DateTime CreatedAt) : IEvent;
```

### Handler 实现

```csharp
// Request Handler (Command/Query)
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        // Business logic
        var result = new OrderResult(request.OrderId, Success: true);
        return CatgaResult<OrderResult>.Success(result);
    }
}

// Event Handler
public class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated @event, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Order {@event.OrderId} created");
    }
}
```

---

## ⚙️ 配置

### 基础配置

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Catga services
builder.Services.AddCatga()
    .AddCatgaInMemoryTransport()      // In-memory message transport
    .AddCatgaInMemoryPersistence();   // In-memory persistence

var app = builder.Build();
app.Run();
```

### Pipeline Behaviors

```csharp
builder.Services.AddCatga()
    .AddCatgaInMemoryTransport()
    .AddCatgaInMemoryPersistence()
    .AddPipelineBehavior<LoggingBehavior<,>>()
    .AddPipelineBehavior<ValidationBehavior<,>>()
    .AddPipelineBehavior<TracingBehavior<,>>();
```

### 幂等性

```csharp
// Option 1: ShardedIdempotencyStore (high performance)
builder.Services.AddCatga()
    .UseShardedIdempotencyStore(options =>
    {
        options.ShardCount = 32;
        options.RetentionPeriod = TimeSpan.FromHours(24);
    });

// Option 2: Redis (distributed)
builder.Services.AddCatga()
    .UseRedisIdempotencyStore(options =>
    {
        options.ConnectionString = "localhost:6379";
        options.KeyPrefix = "idempotency:";
    });
```

---

## 🚀 使用

### 发送 Command/Query

```csharp
public class OrderService
{
    private readonly ICatgaMediator _mediator;

    public OrderService(ICatgaMediator mediator) => _mediator = mediator;

    public async Task<OrderResult> CreateOrderAsync(string orderId, decimal amount)
    {
        // Send Command
        var result = await _mediator.SendAsync<CreateOrder, OrderResult>(
            new CreateOrder(orderId, amount));

        if (!result.IsSuccess)
            throw new Exception(result.Error);

        return result.Value!;
    }

    public async Task<Order?> GetOrderAsync(string orderId)
    {
        // Send Query
        var result = await _mediator.SendAsync<GetOrder, Order>(
            new GetOrder(orderId));

        return result.IsSuccess ? result.Value : null;
    }
}
```

### 发布 Event

```csharp
public class OrderService
{
    private readonly ICatgaMediator _mediator;

    public async Task NotifyOrderCreatedAsync(string orderId)
    {
        // Publish Event (fire-and-forget)
        await _mediator.PublishAsync(new OrderCreated(orderId, DateTime.UtcNow));
    }
}
```

---

## 🌐 分布式

### NATS Transport

```csharp
builder.Services.AddCatga()
    .UseNatsTransport(options =>
    {
        options.Url = "nats://localhost:4222";
        options.SubjectPrefix = "catga.";
    });
```

### Redis Transport

```csharp
builder.Services.AddCatga()
    .UseRedisTransport(options =>
    {
        options.ConnectionString = "localhost:6379";
        options.StreamName = "catga-messages";
    });
```

### Node Discovery (NATS)

```csharp
builder.Services.AddCatga()
    .UseNatsNodeDiscovery(options =>
    {
        options.NodeName = "order-service-1";
        options.HeartbeatInterval = TimeSpan.FromSeconds(5);
        options.NodeMetadata = new Dictionary<string, string>
        {
            ["Version"] = "1.0.0",
            ["Region"] = "us-west"
        };
    });
```

---

## 📞 RPC

### Server

```csharp
builder.Services.AddCatgaRpcServer(options =>
{
    options.ServiceName = "order-service";
    options.Port = 5001;
});
```

### Client

```csharp
// Add client
builder.Services.AddCatgaRpcClient();

// Use client
public class OrderService
{
    private readonly IRpcClient _rpcClient;

    public async Task<UserDto> GetUserAsync(string userId)
    {
        var result = await _rpcClient.CallAsync<GetUser, UserDto>(
            serviceName: "user-service",
            request: new GetUser(userId));

        return result.IsSuccess ? result.Value! : throw new Exception(result.Error);
    }
}
```

---

## 🔥 Native AOT

### MemoryPack (推荐)

```csharp
// 1. Install packages
// dotnet add package Catga.Serialization.MemoryPack
// dotnet add package MemoryPack
// dotnet add package MemoryPack.Generator

// 2. Mark messages
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(string OrderId, bool Success);

// 3. Configure
builder.Services.AddCatga()
    .UseMemoryPackSerializer()
    .AddCatgaInMemoryTransport();

// 4. Publish
// dotnet publish -c Release -r win-x64 --property:PublishAot=true
```

### System.Text.Json Source Generation

```csharp
// 1. Define JsonSerializerContext
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(OrderResult))]
public partial class AppJsonContext : JsonSerializerContext { }

// 2. Configure
builder.Services.AddCatga()
    .UseJsonSerializer(options =>
    {
        options.JsonSerializerContext = AppJsonContext.Default;
    })
    .AddCatgaInMemoryTransport();
```

---

## 🎨 ASP.NET Core

### 集成

```csharp
builder.Services.AddCatgaAspNetCore(options =>
{
    options.EnableDashboard = true;
    options.DashboardPathPrefix = "/catga";
});

var app = builder.Build();

// Map endpoints
app.MapCatgaEndpoints();

app.Run();
```

### 生成的端点

- `POST /catga/command/{Name}` - Send Command
- `POST /catga/query/{Name}` - Send Query
- `POST /catga/event/{Name}` - Publish Event
- `GET /catga/health` - Health check
- `GET /catga/nodes` - Node list

### 自定义端点

```csharp
app.MapPost("/orders", async (CreateOrder command, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithCatgaCommandMetadata<CreateOrder, OrderResult>()
.WithOpenApi();
```

---

## 📊 可观测性

### OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(CatgaDiagnostics.ActivitySourceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(CatgaDiagnostics.MeterName)
        .AddConsoleExporter());
```

### 结构化日志

```csharp
// LoggerMessage is auto-generated, zero allocation
CatgaLog.CommandExecuting(logger, "CreateOrder", messageId, correlationId);
CatgaLog.CommandExecuted(logger, "CreateOrder", messageId, durationMs, isSuccess);
```

---

## 🛠️ Pipeline

### 自定义 Behavior

```csharp
public class ValidationBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest> _validator;

    public ValidationBehavior(IValidator<TRequest> validator, ILogger logger)
        : base(logger) => _validator = validator;

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Validate
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
            return CatgaResult<TResponse>.Failure(errors);
        }

        // Continue pipeline
        return await next();
    }
}

// Register
builder.Services.AddCatga()
    .AddPipelineBehavior<ValidationBehavior<,>>();
```

### 内置 Behaviors

Catga 提供以下内置 Behaviors：

- `LoggingBehavior<,>` - 结构化日志记录
- `TracingBehavior<,>` - 分布式追踪
- `IdempotencyBehavior<,>` - 幂等性保证
- `InboxBehavior<,>` - Inbox 模式
- `OutboxBehavior<,>` - Outbox 模式

---

## 🧪 测试

### 单元测试

```csharp
[Fact]
public async Task CreateOrder_Should_ReturnSuccess()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddCatga()
        .AddCatgaInMemoryTransport()
        .AddCatgaInMemoryPersistence();

    services.AddTransient<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<ICatgaMediator>();

    // Act
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(
        new CreateOrder("ORD-001", 99.99m));

    // Assert
    Assert.True(result.IsSuccess);
    Assert.Equal("ORD-001", result.Value!.OrderId);
}
```

---

## 📖 常用模式

### Result 处理

```csharp
// Success
return CatgaResult<OrderResult>.Success(result);
return CatgaResult<OrderResult>.Success(result, metadata);

// Failure
return CatgaResult<OrderResult>.Failure("Error message");
return CatgaResult<OrderResult>.Failure("Error", exception);

// Check result
if (result.IsSuccess)
{
    var value = result.Value;
}
else
{
    var error = result.Error;
    var exception = result.Exception;
}
```

### 消息 ID 和关联 ID

```csharp
// Command with MessageId
public record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
}

// Send with correlation ID
var command = new CreateOrder("ORD-001", 99.99m)
{
    CorrelationId = HttpContext.TraceIdentifier
};

var result = await mediator.SendAsync<CreateOrder, OrderResult>(command);
```

### QoS (Quality of Service)

```csharp
public record CreateOrder(...) : IRequest<OrderResult>, IMessage
{
    // QoS options
    public QualityOfService QoS { get; init; } = QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode { get; init; } = DeliveryMode.WaitForResult;
}
```

**QoS Levels:**
- `AtMostOnce` - Fire-and-forget
- `AtLeastOnce` - Retry until success (default)
- `ExactlyOnce` - Idempotent, only once

**Delivery Modes:**
- `WaitForResult` - Wait for completion
- `AsyncRetry` - Background with retry

---

## 🔍 调试

### 启用详细日志

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Catga", LogLevel.Trace);
```

### 查看 Activity

```csharp
using var activity = CatgaDiagnostics.ActivitySource.StartActivity("MyOperation");
activity?.SetTag("order_id", orderId);
activity?.SetTag("amount", amount);
```

### 查看 Metrics

```csharp
// Commands executed
CatgaDiagnostics.CommandsExecuted.Add(1, new("command_type", "CreateOrder"));

// Message duration
CatgaDiagnostics.MessageDuration.Record(durationMs, new("message_type", "CreateOrder"));
```

---

## 🚀 生产部署

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YourApp.dll"]
```

### Native AOT Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -r linux-x64 --property:PublishAot=true -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["./YourApp"]
```

### 健康检查

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("catga", () =>
    {
        // Check Catga health
        return HealthCheckResult.Healthy("Catga is running");
    });

app.MapHealthChecks("/health");
```

---

## 📚 更多资源

- [完整文档](./README.md#-文档)
- [示例项目](./examples/)
- [架构设计](./docs/architecture/ARCHITECTURE.md)
- [性能基准](./benchmarks/Catga.Benchmarks/)
- [贡献指南](./CONTRIBUTING.md)

---

<div align="center">

[返回主文档](./README.md) · [查看示例](./examples/) · [性能数据](./README.md#-性能基准)

**Happy coding with Catga!** 🚀

</div>
