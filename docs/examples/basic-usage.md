# 基本使用示例

这篇示例只展示当前最短主路径：

1. 定义消息
2. 实现 handler
3. 注册 Catga
4. 通过 `ICatgaMediator` 调用

## 1. 定义消息

```csharp
using Catga.Abstractions;
using MemoryPack;

[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;

[MemoryPackable]
public partial record GetOrder(string OrderId)
    : IRequest<OrderDto>;

[MemoryPackable]
public partial record OrderCreated(string OrderId, decimal Amount)
    : IEvent;

[MemoryPackable]
public partial record OrderResult(string OrderId, bool Success);

[MemoryPackable]
public partial record OrderDto(string OrderId, decimal Amount, string Status);
```

## 2. 实现 handler

### Command handler

```csharp
using Catga;
using Catga.Abstractions;
using Catga.Core;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    private readonly ICatgaMediator _mediator;

    public CreateOrderHandler(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        var created = new OrderResult(request.OrderId, Success: true);

        await _mediator.PublishAsync(
            new OrderCreated(request.OrderId, request.Amount),
            cancellationToken);

        return CatgaResult<OrderResult>.Success(created);
    }
}
```

### Query handler

```csharp
using Catga.Abstractions;
using Catga.Core;

public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderDto>
{
    public ValueTask<CatgaResult<OrderDto>> HandleAsync(
        GetOrder request,
        CancellationToken cancellationToken = default)
    {
        var dto = new OrderDto(request.OrderId, 99.5m, "Created");
        return ValueTask.FromResult(CatgaResult<OrderDto>.Success(dto));
    }
}
```

### Event handler

```csharp
using Catga.Abstractions;

public sealed class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    public ValueTask HandleAsync(
        OrderCreated @event,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
```

## 3. 注册服务

开发 / 测试最小接法：

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

builder.Services.AddInMemoryTransport();

// 如果接入了源生成器，优先使用生成的注册扩展
builder.Services.AddCatgaServices();
```

如果你还没接源生成器，也可以先手动注册：

```csharp
builder.Services.AddScoped<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetOrder, OrderDto>, GetOrderHandler>();
builder.Services.AddScoped<IEventHandler<OrderCreated>, OrderCreatedHandler>();
```

## 4. 调用 mediator

```csharp
using Catga;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrdersController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("{orderId}")]
    public async Task<IResult> Create(string orderId, CancellationToken cancellationToken)
    {
        var result = await _mediator.SendAsync<CreateOrder, OrderResult>(
            new CreateOrder(orderId, 99.5m),
            cancellationToken);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.BadRequest(result.Error);
    }

    [HttpGet("{orderId}")]
    public async Task<IResult> Get(string orderId, CancellationToken cancellationToken)
    {
        var result = await _mediator.SendAsync<GetOrder, OrderDto>(
            new GetOrder(orderId),
            cancellationToken);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.NotFound(result.Error);
    }
}
```

## 5. 常见扩展

如果你要继续往生产配置走，常见会补这些：

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .UseRedis(redisConnectionString)
    .UseResilience()
    .WithTracing()
    .WithValidation()
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

builder.Services.AddRedisTransport(redisConnectionString);
builder.Services.AddCatgaServices();
```

## 下一步看什么

- 想看配置细节：看 [配置指南](../articles/configuration.md)
- 想看自动注册：看 [自动 DI / 自动注册](../guides/auto-di-registration.md)
- 想看序列化：看 [序列化指南](../guides/serialization.md)
- 想看完整场景：看 [端到端场景](./e2e-scenarios.md)
