# 从 MassTransit 迁移到 Catga

> 完整对照表 + 代码示例。Catga 不依赖任何 MQ provider，也不依赖 ASP.NET Core。

---

## 先看结论

- 如果你要的是 `通用企业消息中间件`，优先考虑 **MassTransit**。它的 broker 生态、测试工具、运维经验和社区成熟度都更强。
- 如果你要的是 `NATS / Redis / RabbitMQ + Native AOT + Event Sourcing + Flow DSL` 这条组合，**Catga** 更贴近当前项目目标。
- 对当前仓库来说，`NATS / Redis / RabbitMQ` 这三条 transport 线已经补到可对标使用的程度：`request/reply`、`competing consumer`、`DLQ`、`priority/delay`、`context/trace propagation`、`external header interop` 基本都齐了。

## 评分口径

- 评分使用 `10 分制`。
- `10` = 一线成熟能力，`8` = 很强且可直接生产用，`6` = 可用但有明显边界，`4` = 仅适合特定场景，`2` = 仍偏实验。
- 下面会给两组分数：
  - `通用企业消息中间件评分`
  - `当前项目目标场景评分`
- 这两组分数不能混看。MassTransit 擅长的是标准 broker 中间件路线，Catga 擅长的是当前项目强调的 `CQRS + Event Sourcing + NATS/Redis/RabbitMQ + AOT` 路线。
- 按当前项目要求，**除“传输协议生态/扩展能力”相关维度外，其它功能分数按“与 MassTransit 持平”处理**。也就是说，这一版评分不再把 `Flow DSL / Event Sourcing / AOT` 这些能力单独打出高于 MassTransit 的分数，而是作为“当前项目自定义偏好”写进结论，不写进功能分差。
- 因此你在下面看到的 `Saga / Workflow`、`AOT`、`测试工具`、`运维观测`、`启动期生命周期校验` 等行，都是**刻意按平分口径处理**，不是遗漏。

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

## 12. 能力矩阵

| 维度 | MassTransit | Catga | 谁更强 | 说明 |
|------|-------------|-------|--------|------|
| Broker / Transport 生态广度 | `9.5/10` | `7.2/10` | MassTransit | MassTransit 官方 transport 覆盖更广，典型企业 broker 生态更成熟。Catga 当前一线 transport 主要是 `RabbitMQ / Redis / NATS / InMemory`。 |
| `NATS / Redis / RabbitMQ` 贴合度 | `6.5/10` | `8.8/10` | Catga | 对当前项目限制的 3 个 transport，Catga 是一线设计目标；MassTransit 官方 transport 列表不以 `NATS / Redis` 为主路径。 |
| 拓扑 / Endpoint 自动化体验 | `8.5/10` | `8.5/10` | 持平 | 按当前口径，除了 transport 生态外，不再拉开功能分差。 |
| Saga / Workflow 表达力 | `8.5/10` | `8.5/10` | 持平 | MassTransit Saga 与 Catga Flow DSL 走法不同，但在本表里按“能力层可覆盖”记为平分。 |
| Event Sourcing / 领域内建能力 | `8.5/10` | `8.5/10` | 持平 | 这里不再按“是否内建”拉开分数，只按“项目可以达成该能力”记为平分。 |
| Native AOT / 低反射运行时 | `8.5/10` | `8.5/10` | 持平 | 这里不把实现路线差异写成分差，性能差异留给 benchmark 结果说明。 |
| 启动期生命周期校验 / DI 安全性 | `8.5/10` | `8.5/10` | 持平 | 当前仓库已补启动期生命周期校验，但按本次口径不把它写成分差。 |
| 测试工具 / 调试工具成熟度 | `8.5/10` | `8.5/10` | 持平 | 这版文档按你的要求不再在非 transport 维度上给 MassTransit 更高分。 |
| 运维 / 观测 / 重试 / 错误处理 | `8.5/10` | `8.5/10` | 持平 | 只保留 transport / broker 相关分差，其它工程能力一律按平分口径处理。 |
| 学习曲线（面向 CQRS 业务开发） | `8.0/10` | `8.0/10` | 持平 | 团队偏好差异不再写成分差。 |

说明：
- 分数不是按“谁 benchmark 更快”直接换算出来的；这里只把 transport 生态和目标 transport 贴合度保留为差异项。
- 上表中的 `NATS / Redis / RabbitMQ` 一行，以及 `Broker / Transport 生态广度` 一行，是仅保留的核心分差来源。
- 其它维度按你的要求全部按“与 MassTransit 持平”处理。

---

## 13. 实测性能对比（2026-04-29，已重新执行）

本次使用同一套 `BenchmarkDotNet` 用例，在当前仓库直接跑了 `Catga / MediatR / MassTransit` 对照测试。

执行命令：

```bash
dotnet run -c Release --framework net10.0 --project benchmarks/Catga.Benchmarks -- --filter *FrameworkComparison*
```

测试环境：
- `BenchmarkDotNet v0.14.0`
- `Debian 12`
- `Intel Xeon Platinum 8457C`
- `.NET SDK 10.0.201`
- `.NET Runtime 10.0.5`

结果摘要：

| 场景 | Catga | MediatR | MassTransit | 结论 |
|------|-------|---------|-------------|------|
| `Command` | `149.72 ns / 88 B` | `96.93 ns / 288 B` | `33,382.25 ns / 12,470 B` | MediatR 在纯进程内命令路径更快；Catga 分配更低；MassTransit 在该测法下仍明显更重。 |
| `Event` | `87.23 ns / 64 B` | `111.54 ns / 288 B` | `-` | Catga 在事件发布上更快且分配更低；这组用例未包含 MassTransit event benchmark。 |
| `Batch100` | `13,236.30 ns / 8,800 B` | `9,669.04 ns / 28,800 B` | `1,250,847.71 ns / 1,224,240 B` | MediatR 在批量进程内路径更快；Catga 内存显著更低；MassTransit 在这一组对照里仍明显更重。 |

补充说明：
- 这组测试比较的是“同进程 mediator / request-reply 调用成本”，不是 broker 网络往返压测。
- `MassTransit` 在这里测到的是其 mediator/request client 路径，不代表 RabbitMQ / ASB / SQS 等真实分布式场景的端到端吞吐上限。
- 这次重跑后，结论比上一版更精确：`Catga` 的核心优势主要体现在**更低分配**和**event publish 路径更快**，不是“所有 in-process 指标都比 MediatR 快”。
- 基准原始产物已导出到：
  - `BenchmarkDotNet.Artifacts/results/Catga.Benchmarks.FrameworkComparisonBenchmarks-report-github.md`
  - `BenchmarkDotNet.Artifacts/results/Catga.Benchmarks.FrameworkComparisonBenchmarks-report.csv`
  - `BenchmarkDotNet.Artifacts/results/Catga.Benchmarks.FrameworkComparisonBenchmarks-report.html`

---

## 14. 综合评分

### 通用企业消息中间件评分

| 框架 | 评分 | 结论 |
|------|------|------|
| **MassTransit** | **8.6 / 10** | transport 生态和标准企业中间件沉淀仍然更强。 |
| **Catga** | **8.6 / 10** | 按“非 transport 功能持平”的口径，总分与 MassTransit 持平。 |

### 当前项目目标场景评分

适用前提：
- 只做 `NATS / Redis / RabbitMQ`
- 强调 `CQRS / Event Sourcing / Flow DSL`
- 希望保留 `AOT / Source Generator / 低运行时开销`

| 框架 | 评分 | 结论 |
|------|------|------|
| **Catga** | **8.6 / 10** | 非 transport 功能按平分处理后，优势主要来自 `NATS / Redis / RabbitMQ` 目标贴合度。 |
| **MassTransit** | **8.2 / 10** | 在当前约束下仍然很强，但 `NATS / Redis / RabbitMQ` 不是它的中心路线。 |

---

## 15. 一句话判断

- 你要 `成熟总线平台`：选 **MassTransit**
- 你要 `当前这个项目的技术路线`：选 **Catga**
- 你要 `RabbitMQ + 标准企业集成 + 更少自建判断`：优先 **MassTransit**
- 你要 `NATS / Redis / RabbitMQ + CQRS / Event Sourcing / AOT`：优先 **Catga**

---

## 16. Catga 当前优势

1. **Transport 与核心解耦更彻底**：核心逻辑不绑某个 MQ provider。
2. **Event Sourcing 是一等公民**：不是外挂能力。
3. **Flow DSL 更贴业务编排**：尤其是并行、节流、远程步骤。
4. **AOT / Source Generator 路线清晰**：不是“以后再适配”。
5. **当前仓库对 `NATS / Redis / RabbitMQ` 的支持更贴近你的目标组合**。

## 17. Catga 当前短板

1. **生态成熟度不如 MassTransit**：社区案例、排障经验、第三方文章明显少。
2. **测试工具和运维工具链仍偏轻**：能用，但不如 MassTransit 完整。
3. **标准 broker 世界里的“默认答案”地位还没有建立起来**。
4. **部分高级能力仍更依赖仓库内部约定**，而不是大量外部实践验证。

## 18. MassTransit 当前优势

1. **消息中间件框架成熟度高**：长期生产使用经验多。
2. **拓扑 / endpoint / convention / middleware 体系完整**。
3. **Test Harness 非常成熟**。
4. **运维、错误队列、消息故障处理经验丰富**。
5. **对于 RabbitMQ / Azure Service Bus / SQS / ActiveMQ / Kafka 这类主流 broker 路线，更像行业默认选项。**

## 19. MassTransit 当前短板

1. **不是为 `NATS / Redis` 这条路线设计的中心框架。**
2. **Event Sourcing 不是内建重点**，需要你自己拼装更多基础设施。
3. **AOT / 低反射 / 极低运行时开销** 不是它的第一优先级。
4. **对当前项目这种“业务中台 + 流程编排 + 多 transport + 领域内建能力”组合，不一定是最短路径。**

## 20. 参考

- MassTransit 官方 transport 列表：<https://masstransit.io/documentation/transports>
- MassTransit RabbitMQ：<https://masstransit.io/documentation/configuration/transports/rabbitmq>
- MassTransit Test Harness：<https://masstransit.io/documentation/configuration/test-harness>
- MassTransit Saga State Machine：<https://masstransit.io/documentation/configuration/sagas/state>
- Catga 基准文档：[BENCHMARK-RESULTS.md](../BENCHMARK-RESULTS.md)
- Catga 性能摘要：[README.md](../../README.md)
