# Catga vs MassTransit：能力映射与代码对照

> 面向“怎么从 MassTransit 的概念、配置和代码迁到 Catga”的对照页。
> 如果你在找评分、取舍和 benchmark 结论，请看 [masstransit-scorecard.md](./masstransit-scorecard.md)。
> 如果你在找 RabbitMQ / Redis / NATS 的生产默认接法，请看 [broker-production-overview.md](../deployment/broker-production-overview.md)。

---

## 这页覆盖什么

这页主要回答：

- MassTransit 的概念在 Catga 里对应什么
- 同一个能力在两边分别怎么配置
- 迁移时常见代码模式应该怎么改写

---

## 核心概念对照

| MassTransit | Catga | 说明 |
|-------------|-------|------|
| `IBus` | `IMessageTransport` | 消息总线 |
| `IPublishEndpoint` | `ICatgaMediator.PublishAsync` | 发布事件 |
| `ISendEndpoint` | `ICatgaMediator.SendAsync` | 发送命令 |
| `IRequestClient<T>` | `IRequestClient<TReq, TRes>` | 跨服务 Request/Response |
| `IConsumer<T>` | `IEventHandler<T>` / `IRequestHandler<T,R>` | 消息消费者 |
| `Saga` | `StateMachineConfig<TState, TEnum>` | 状态机 |
| `MassTransitStateMachine<T>` | `StateMachineConfig<TState, TEnum>` | 状态机配置 |
| `Fault<T>` | `Fault<T>` | 错误消息 |
| `ICompetingConsumer` | `ICompetingConsumer<T>` | 竞争消费 |
| `InMemoryTestHarness` | `CatgaTestHarness` / `FlowTestContext<T>` | 测试工具 |

---

## 1. 基础配置

### MassTransit
```csharp
services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("rabbitmq://localhost");
        cfg.ConfigureEndpoints(ctx);
    });
});
```

### Catga
```csharp
services.AddCatga()
    .UseInMemory()           // 开发/测试
    // .UseRedis(connStr)    // Redis Streams
    // .UseNats(url)         // NATS JetStream
    .WithFaultPublishing()   // 自动 Fault<T>
    .WithCorrelationPropagation();

services.AddRabbitMqTransport("amqp://localhost"); // RabbitMQ
// 或
services.AddRedisTransport(connStr);
services.AddNatsTransport(url);
```

---

## 2. 消费者 / Handler

### MassTransit
```csharp
public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var order = context.Message;
        // ...
    }
}
```

### Catga
```csharp
// 事件 handler
public class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    public async ValueTask HandleAsync(OrderCreated @event, CancellationToken ct)
    {
        // ...
    }
}

// 命令 handler（有返回值）
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderDto>
{
    public async ValueTask<CatgaResult<OrderDto>> HandleAsync(CreateOrder cmd, CancellationToken ct)
        => CatgaResult<OrderDto>.Success(new OrderDto(...));
}
```

---

## 3. 发布 / 发送

### MassTransit
```csharp
await bus.Publish(new OrderCreated(orderId));
await sendEndpoint.Send(new ProcessPayment(orderId));
```

### Catga
```csharp
await mediator.PublishAsync(new OrderCreated(orderId));
await mediator.SendAsync<ProcessPayment, PaymentResult>(new ProcessPayment(orderId));
```

---

## 4. Request/Response (跨服务)

### MassTransit
```csharp
var client = bus.CreateRequestClient<GetOrder>();
var response = await client.GetResponse<OrderDto>(new GetOrder(orderId));
var order = response.Message;
```

### Catga
```csharp
services.AddCatga().UseRequestClient();

// 使用
var factory = sp.GetRequiredService<IRequestClientFactory>();
var client = factory.CreateClient<GetOrder, OrderDto>();
var result = await client.RequestAsync(new GetOrder(orderId));
if (result.IsSuccess) var order = result.Value;
```

---

## 5. Saga / 状态机

### MassTransit
```csharp
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Submitted { get; private set; }
    public State Accepted { get; private set; }
    public Event<OrderSubmitted> OrderSubmitted { get; private set; }

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);
        Initially()
            .When(OrderSubmitted)
            .TransitionTo(Submitted);
        During(Submitted)
            .When(OrderAccepted)
            .TransitionTo(Accepted);
    }
}
```

### Catga
```csharp
public enum OrderStatus { Initial, Submitted, Accepted, Cancelled }

public class OrderStateMachine : StateMachineConfig<OrderState, OrderStatus>
{
    protected override void Configure()
    {
        State(OrderStatus.Initial)
            .On<OrderSubmitted>()
                .Execute((s, e) => s.OrderId = e.OrderId)
                .TransitionTo(OrderStatus.Submitted);

        State(OrderStatus.Submitted)
            .On<OrderAccepted>()
                .TransitionTo(OrderStatus.Accepted)
            .And()
            .On<OrderCancelled>()
                .TransitionTo(OrderStatus.Cancelled);
    }
}

// 自动事件路由（等价于 MassTransit InitiatedBy/Orchestrates）
services.AddStateMachineWithRouter<OrderState, OrderStatus, OrderStateMachine>(
    r => r.For<OrderSubmitted>(e => e.OrderId)
          .For<OrderAccepted>(e => e.OrderId));
```

---

## 6. Fault 处理

### MassTransit
```csharp
public class OrderFaultConsumer : IConsumer<Fault<CreateOrder>>
{
    public async Task Consume(ConsumeContext<Fault<CreateOrder>> context)
    {
        var fault = context.Message;
        // fault.Message = original message
        // fault.Exceptions = exceptions
    }
}
```

### Catga
```csharp
services.AddCatga().WithFaultPublishing();

public class OrderFaultHandler : IEventHandler<Fault<CreateOrder>>
{
    public async ValueTask HandleAsync(Fault<CreateOrder> fault, CancellationToken ct)
    {
        // fault.Message = original message
        // fault.Exception = exception
        // fault.ErrorCode = error code
    }
}
```

---

## 7. Competing Consumers

### MassTransit
```csharp
x.UsingRabbitMq((ctx, cfg) =>
{
    cfg.ReceiveEndpoint("order-queue", e =>
    {
        e.ConfigureConsumer<OrderConsumer>(ctx);
        e.PrefetchCount = 10;
    });
});
```

### Catga
```csharp
// Redis Streams
services.AddRedisCompetingConsumer<OrderCreated>("stream:orders", opt =>
{
    opt.GroupName = "order-processors";
    opt.Concurrency = 10;
    opt.MaxDeliveryAttempts = 5;
});

// NATS JetStream
services.AddNatsCompetingConsumer<OrderCreated>("orders.created", configure: opt =>
{
    opt.GroupName = "order-processors";
    opt.Concurrency = 10;
    opt.MaxDeliveryAttempts = 5;
});

// RabbitMQ shared queue
services.AddRabbitMqCompetingConsumer<OrderCreated>("orders.queue", routingKey: "orders.created", configure: opt =>
{
    opt.GroupName = "order-processors";
    opt.Concurrency = 10;
    opt.MaxDeliveryAttempts = 5;
});

// 启动消费
var consumer = sp.GetRequiredService<ICompetingConsumer<OrderCreated>>();
await consumer.StartAsync(async (msg, ct) =>
{
    await handler.HandleAsync(msg, ct);
});
```

说明：
- `MaxDeliveryAttempts` 现在会在 Redis / NATS / RabbitMQ 上生效，超过次数后会终止重投递。
- 如果已注册 `IDeadLetterQueue`，失败消息会在达到上限后进入 DLQ。
- RabbitMQ 这里的最大投递次数统计是应用侧跟踪；如果你还需要 broker 级 DLX / policy，仍然建议同时配置 RabbitMQ 原生死信策略。

---

## 8. 消息调度 (SendLater)

### MassTransit
```csharp
await scheduler.ScheduleSend(
    new Uri("queue:order-timeout"),
    TimeSpan.FromMinutes(30),
    new OrderTimeout(orderId));
```

### Catga
```csharp
// 需要 Outbox 支持
services.AddCatga().UseInMemory().UseOutbox();

await mediator.SendLaterAsync(
    new OrderTimeout(orderId),
    delay: TimeSpan.FromMinutes(30));

// 或指定绝对时间
await mediator.SendAtAsync(
    new OrderTimeout(orderId),
    scheduledAt: DateTimeOffset.UtcNow.AddMinutes(30));
```

---

## 9. 消息版本化

### MassTransit
```csharp
// 使用 IVersionedMessage 接口
public interface IOrderCreated_v2 : IOrderCreated
{
    decimal Amount { get; }
}
```

### Catga
```csharp
[MessageVersion(2)]
public record OrderCreatedV2(string OrderId, string CustomerId, decimal Amount) : IEvent { ... }

services.AddCatga().WithMessageVersioning(b => b
    .MapType("MyApp.OrderCreated", typeof(OrderCreatedV2))  // 类型重命名
    .Upgrade<OrderCreatedV1, OrderCreatedV2>(               // 内容升级
        v1 => new OrderCreatedV2(v1.OrderId, v1.CustomerId, Amount: 0m)));
```

---

## 10. 授权

### MassTransit
```csharp
// 依赖 ASP.NET Core
cfg.UseAuthentication();
cfg.UseAuthorization();
```

### Catga（纯 .NET，不依赖 ASP.NET Core）
```csharp
services.AddCatga().WithAuthorization(reg =>
    reg.Register(new TenantPolicy()));

// 标记 handler
[Authorize("admin")]
public record DeleteOrder(string OrderId) : IRequest<bool> { ... }

// 设置当前用户（任何宿主）
var ctx = sp.GetRequiredService<ISecurityContext>();
ctx.SetUser(new ClaimsPrincipal(identity)); // 标准 .NET ClaimsPrincipal
```

---

## 11. 测试

### MassTransit
```csharp
var harness = new InMemoryTestHarness();
var consumer = harness.Consumer<OrderConsumer>();
await harness.Start();
await harness.InputQueueSendEndpoint.Send(new CreateOrder(...));
Assert.True(await consumer.Consumed.Any<CreateOrder>());
```

### Catga
```csharp
// 简单测试
await using var harness = new CatgaTestHarness();
harness.Start();
var mediator = harness.Mediator;
await mediator.SendAsync<CreateOrder, OrderDto>(new CreateOrder(...));

// Flow 测试
await using var ctx = new FlowTestContext<OrderState, OrderFlow>();
ctx.Mediator.OnSend<ReserveInventory, InventoryResult>(_ => new InventoryResult(true));
var result = await ctx.RunAsync(new OrderState { OrderId = "ORD-1" });
result.IsSuccess.Should().BeTrue();
```

---

## 接下来读什么

- 你要看选型判断：看 [评分、取舍与实测结论](./masstransit-scorecard.md)
- 你要看最新 benchmark：看 [Benchmark Results](../BENCHMARK-RESULTS.md)

---
