# Catga 分布式长事务 - 全自动化方案

## 🎯 核心理念：利用 Catga 现有能力实现零编排

### 传统 Saga 的问题
- ❌ 需要中心化编排器（单点故障）
- ❌ 手动定义补偿逻辑（容易出错）
- ❌ 幂等性需要手动处理
- ❌ 事件溯源需要额外实现
- ❌ 重试逻辑需要手动编写

### Catga 全自动化方案
**关键发现：Catga 已经有了所有需要的能力！**

- ✅ **零编排器**：完全事件驱动，通过事件链自动触发
- ✅ **自动补偿**：失败事件自动触发补偿处理器
- ✅ **自动幂等**：Outbox/Inbox 天然支持
- ✅ **自动重试**：QoS.ExactlyOnce 自动重试
- ✅ **自动追踪**：CorrelationId + ActivitySource
- ✅ **事件溯源**：Event Handler 天然支持
- ✅ **AOT 兼容**：完全支持 Native AOT
- ✅ **高性能**：零分配设计

## 💡 设计思想

```
Command → Handler → Event → Next Handler → Event → ...
   ↓                  ↓                        ↓
Outbox            Inbox                    Inbox
   ↓                  ↓                        ↓
自动重试          自动幂等                  自动幂等
```

**不需要额外的事务框架！只需要正确使用 Catga 的 CQRS + Event Sourcing！**

## 📖 核心概念

### 事务上下文 (Transaction Context)
共享状态对象，在所有步骤间传递：

```csharp
public class OrderContext
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }

    // Populated during execution
    public string? ReservationId { get; set; }
    public string? PaymentId { get; set; }
}
```

### 事务定义 (Transaction Definition)
声明式定义事务流程：

```csharp
public class OrderTransaction : IDistributedTransaction<OrderContext>
{
    public string TransactionId => "order-transaction";
    public string Name => "Order Processing";

    public ITransactionBuilder<OrderContext> Define(ITransactionBuilder<OrderContext> builder)
    {
        return builder
            .Execute<ReserveInventoryCommand, InventoryReservedEvent>(
                ctx => new ReserveInventoryCommand { ... },
                (ctx, evt) => { ctx.ReservationId = evt.ReservationId; return ctx; })
            .CompensateWith<ReleaseInventoryCommand>(
                ctx => new ReleaseInventoryCommand { ReservationId = ctx.ReservationId })

            .Execute<ChargePaymentCommand, PaymentChargedEvent>(...)
            .CompensateWith<RefundPaymentCommand>(...);
    }
}
```

### 事务状态
- `Pending`: 等待执行
- `Running`: 正在执行
- `Completed`: 成功完成
- `Compensating`: 正在补偿
- `Compensated`: 已补偿（回滚）
- `Failed`: 失败
- `TimedOut`: 超时

## 🚀 快速开始

### 1. 注册服务

```csharp
services.AddCatga()
    .AddCatgaInMemoryTransport()
    .AddCatgaInMemoryPersistence();

// Register transaction infrastructure
services.AddSingleton<ITransactionStore, InMemoryTransactionStore>();
services.AddSingleton<ITransactionCoordinator, TransactionCoordinator>();

// Register your transaction
services.AddSingleton<OrderTransaction>();
```

### 2. 定义事务

```csharp
public class OrderTransaction : IDistributedTransaction<OrderContext>
{
    public string TransactionId => "order-tx";
    public string Name => "Order Processing";

    public ITransactionBuilder<OrderContext> Define(ITransactionBuilder<OrderContext> builder)
    {
        return builder
            // Step 1: Reserve Inventory
            .Execute<ReserveInventoryCommand, InventoryReservedEvent>(
                ctx => new ReserveInventoryCommand
                {
                    MessageId = Guid.NewGuid().ToString(),
                    ProductId = ctx.ProductId,
                    Quantity = ctx.Quantity
                },
                (ctx, evt) =>
                {
                    ctx.ReservationId = evt.ReservationId;
                    return ctx;
                })
            .CompensateWith<ReleaseInventoryCommand>(ctx => new ReleaseInventoryCommand
            {
                MessageId = Guid.NewGuid().ToString(),
                ReservationId = ctx.ReservationId!
            })

            // Step 2: Charge Payment
            .Execute<ChargePaymentCommand, PaymentChargedEvent>(
                ctx => new ChargePaymentCommand
                {
                    MessageId = Guid.NewGuid().ToString(),
                    CustomerId = ctx.CustomerId,
                    Amount = ctx.Amount
                },
                (ctx, evt) =>
                {
                    ctx.PaymentId = evt.PaymentId;
                    return ctx;
                })
            .CompensateWith<RefundPaymentCommand>(ctx => new RefundPaymentCommand
            {
                MessageId = Guid.NewGuid().ToString(),
                PaymentId = ctx.PaymentId!
            });
    }
}
```

### 3. 执行事务

```csharp
var coordinator = serviceProvider.GetRequiredService<ITransactionCoordinator>();
var transaction = serviceProvider.GetRequiredService<OrderTransaction>();

var context = new OrderContext
{
    OrderId = "ORDER-001",
    CustomerId = "CUST-123",
    ProductId = "PROD-456",
    Quantity = 2,
    Amount = 99.99m
};

var options = new TransactionOptions
{
    Timeout = TimeSpan.FromMinutes(5),
    AutoCompensate = true,
    MaxRetries = 3,
    EnableEventSourcing = true
};

var result = await coordinator.StartAsync(transaction, context, options);

if (result.IsSuccess)
{
    Console.WriteLine("✅ Transaction completed successfully");
}
else
{
    Console.WriteLine($"❌ Transaction failed: {result.Error}");
    Console.WriteLine($"Status: {result.Status}"); // Compensated, Failed, TimedOut
}
```

## 📊 执行流程

### 成功场景
```
Start → Step1 → Step2 → Step3 → Completed ✅
```

### 失败场景（自动补偿）
```
Start → Step1 ✅ → Step2 ✅ → Step3 ❌
      ↓
Compensate Step2 → Compensate Step1 → Compensated 🔄
```

## 🔧 高级特性

### 1. 事务选项

```csharp
var options = new TransactionOptions
{
    Timeout = TimeSpan.FromMinutes(5),        // 事务超时
    AutoCompensate = true,                     // 自动补偿
    MaxRetries = 3,                            // 最大重试次数
    RetryDelay = TimeSpan.FromSeconds(1),     // 重试延迟
    EnableEventSourcing = true                 // 启用事件溯源
};
```

### 2. Fire-and-Forget 步骤

```csharp
builder
    .Fire<SendNotificationCommand>(ctx => new SendNotificationCommand
    {
        MessageId = Guid.NewGuid().ToString(),
        OrderId = ctx.OrderId
    });
```

### 3. 条件分支（TODO）

```csharp
builder
    .When(
        ctx => ctx.Amount > 1000,
        trueBranch => trueBranch
            .Execute<RequireApprovalCommand, ApprovalGrantedEvent>(...),
        falseBranch => falseBranch
            .Execute<AutoApproveCommand, AutoApprovedEvent>(...)
    );
```

### 4. 并行执行（TODO）

```csharp
builder
    .Parallel(
        branch1 => branch1.Execute<SendEmailCommand, EmailSentEvent>(...),
        branch2 => branch2.Execute<SendSMSCommand, SMSSentEvent>(...)
    );
```

### 5. 查询事务状态

```csharp
var snapshot = await coordinator.GetSnapshotAsync("transaction-id");

Console.WriteLine($"Status: {snapshot.Status}");
Console.WriteLine($"Current Step: {snapshot.CurrentStep}/{snapshot.TotalSteps}");
Console.WriteLine($"Started: {snapshot.StartedAt}");

// Event sourcing - replay events
var events = await store.GetEventsAsync("transaction-id");
foreach (var evt in events)
{
    Console.WriteLine($"{evt.Timestamp}: {evt.EventType} - {evt.Data}");
}
```

### 6. 故障恢复

```csharp
// Get incomplete transactions
var incompleteTransactions = await coordinator.GetIncompleteTransactionsAsync();

foreach (var snapshot in incompleteTransactions)
{
    Console.WriteLine($"Incomplete: {snapshot.TransactionId} - {snapshot.Status}");
    // Implement recovery logic
}
```

## 📈 性能优化

### 1. 零分配设计
- `TransactionResult`: readonly struct
- `StepResult`: readonly struct
- 使用 `record` 减少样板代码

### 2. 自动重试
- 指数退避策略
- 可配置重试次数和延迟

### 3. 事件溯源
- 可选启用（`EnableEventSourcing = true`）
- 完整审计日志
- 支持事件重放

### 4. 超时处理
- 自动超时检测
- 优雅的超时处理
- 可配置超时时间

## 🎨 最佳实践

### 1. 幂等性
所有命令处理器应该是幂等的：

```csharp
public class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand>
{
    public async Task<CatgaResult> HandleAsync(ReserveInventoryCommand request, ...)
    {
        // Check if already processed (via MessageId)
        if (await _idempotencyStore.HasBeenProcessedAsync(request.MessageId))
            return CatgaResult.Success();

        // Execute
        var result = await _service.ReserveAsync(...);

        // Mark as processed
        await _idempotencyStore.MarkAsProcessedAsync(request.MessageId);

        return result;
    }
}
```

### 2. 错误处理

```csharp
.Execute<ChargePaymentCommand, PaymentChargedEvent>(
    ctx => new ChargePaymentCommand { ... },
    (ctx, evt) => { /* Success */ return ctx; },
    (ctx, ex) =>
    {
        // Handle failure
        _logger.LogError(ex, "Payment failed");
        return ctx;
    })
```

### 3. 补偿最佳实践

```csharp
.CompensateWith<RefundPaymentCommand>(ctx =>
{
    // Always check if compensation is needed
    if (string.IsNullOrEmpty(ctx.PaymentId))
        return null; // Skip compensation

    return new RefundPaymentCommand
    {
        MessageId = Guid.NewGuid().ToString(),
        PaymentId = ctx.PaymentId
    };
})
```

### 4. 超时处理

```csharp
var options = new TransactionOptions
{
    Timeout = TimeSpan.FromMinutes(5), // Adjust based on your needs
    MaxRetries = 3,
    RetryDelay = TimeSpan.FromSeconds(1)
};

var result = await coordinator.StartAsync(transaction, context, options);

if (result.Status == TransactionStatus.TimedOut)
{
    // Handle timeout
    _logger.LogWarning("Transaction timed out: {TransactionId}", transaction.TransactionId);
}
```

## 🔍 监控和可观测性

### 1. 日志
Catga 自动记录所有事务事件：

```
[INFO] Transaction started ORDER-TX [Name=Order Processing]
[DEBUG] Transaction step executing ORDER-TX [Step=0/3]
[DEBUG] Transaction step completed ORDER-TX [Step=0]
[DEBUG] Transaction step executing ORDER-TX [Step=1/3]
[ERROR] Transaction step failed ORDER-TX [Step=1, Error=Payment declined]
[WARN] Transaction compensating ORDER-TX [StepsToCompensate=1]
[DEBUG] Transaction step compensating ORDER-TX [Step=0]
[DEBUG] Transaction step compensated ORDER-TX [Step=0]
[INFO] Transaction compensated ORDER-TX
```

### 2. 分布式追踪
自动集成 ActivitySource：

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(CatgaDiagnostics.ActivitySourceName));
```

### 3. 事件溯源
完整的事件历史：

```csharp
var events = await store.GetEventsAsync("transaction-id");
foreach (var evt in events)
{
    Console.WriteLine($"{evt.Timestamp}: {evt.EventType}");
    Console.WriteLine($"  Step: {evt.StepIndex}");
    Console.WriteLine($"  Data: {evt.Data}");
}
```

## 🆚 Catga vs 传统 Saga

| 特性 | Catga 分布式事务 | 传统 Saga |
|------|------------------|-----------|
| 编排方式 | 事件驱动（去中心化） | 中心化编排器 |
| 补偿定义 | 声明式（自动） | 手动编写 |
| 幂等性 | 自动（Outbox/Inbox） | 手动处理 |
| 事件溯源 | 内置支持 | 需要额外实现 |
| 重试机制 | 自动（指数退避） | 手动实现 |
| 超时处理 | 自动 | 手动实现 |
| AOT 支持 | ✅ 完全支持 | ❌ 通常不支持 |
| 性能 | 🚀 零分配 | ⚠️ 堆分配 |
| 复杂度 | 📝 简单 | 🤯 复杂 |

## 💡 使用场景

### 适合使用 Catga 分布式事务的场景
- ✅ 跨多个服务的复杂业务流程
- ✅ 需要自动补偿的场景
- ✅ 需要完整审计日志
- ✅ 需要高性能和 AOT 支持
- ✅ 基于 CQRS 的架构

### 不适合的场景
- ❌ 简单的单服务事务（使用本地事务）
- ❌ 需要强一致性的场景（使用分布式事务协议如 2PC）
- ❌ 步骤无法补偿的场景

## 📚 相关资源

- [Catga Examples](../../examples/08-DistributedTransaction/)
- [Catga Architecture](../architecture/ARCHITECTURE.md)
- [Outbox/Inbox Pattern](./outbox-inbox.md)
- [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html)

## 🎯 总结

Catga 的分布式事务方案通过以下创新点超越了传统 Saga：

1. **事件驱动架构**：无需中心化编排器，更高可用性
2. **声明式定义**：简洁优雅的 API，减少出错
3. **自动化**：补偿、重试、幂等性全自动
4. **可观测性**：内置日志、追踪、事件溯源
5. **高性能**：零分配设计，AOT 友好

这使得 Catga 成为构建高可用、高性能分布式系统的理想选择！🚀

