# Saga 模式 (Saga Pattern)

Catga 提供了 Saga 模式的完整实现，用于管理分布式事务，确保数据一致性。

---

## 📦 安装

```bash
dotnet add package Catga
```

---

## 🚀 快速开始

### 基本示例

```csharp
using Catga.Saga;

// 注册服务
builder.Services.AddSagaExecutor();

// 创建并执行 Saga
public class CreateOrderSaga
{
    private readonly SagaExecutor _executor;
    private readonly ILogger<CreateOrderSaga> _logger;

    public CreateOrderSaga(SagaExecutor executor, ILogger<CreateOrderSaga> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task<SagaResult> ExecuteAsync(CreateOrderCommand command)
    {
        var saga = SagaBuilder.Create($"create-order-{command.OrderId}")
            .AddStep("ReserveInventory",
                execute: async ct => await ReserveInventoryAsync(command.Items, ct),
                compensate: async ct => await ReleaseInventoryAsync(command.Items, ct))
            .AddStep("ProcessPayment",
                execute: async ct => await ProcessPaymentAsync(command.PaymentInfo, ct),
                compensate: async ct => await RefundPaymentAsync(command.PaymentInfo, ct))
            .AddStep("CreateOrder",
                execute: async ct => await CreateOrderAsync(command, ct),
                compensate: async ct => await CancelOrderAsync(command.OrderId, ct))
            .Build();

        return await _executor.ExecuteAsync(saga);
    }
}
```

---

## 📖 核心概念

### Saga

Saga 是一系列本地事务的集合，每个事务都有对应的补偿事务。

### 执行流程

1. **正向执行**：按顺序执行所有步骤
2. **失败补偿**：如果某步失败，按逆序执行补偿事务
3. **最终一致性**：通过补偿确保数据最终一致

---

## 💡 使用示例

### 1. 电商订单创建

```csharp
var saga = SagaBuilder.Create()
    .AddStep("ReserveInventory",
        execute: async ct =>
        {
            await _inventory.ReserveAsync(items, ct);
        },
        compensate: async ct =>
        {
            await _inventory.ReleaseAsync(items, ct);
        })
    .AddStep("ProcessPayment",
        execute: async ct =>
        {
            await _payment.ChargeAsync(amount, ct);
        },
        compensate: async ct =>
        {
            await _payment.RefundAsync(amount, ct);
        })
    .AddStep("CreateShipment",
        execute: async ct =>
        {
            await _shipping.CreateAsync(address, ct);
        },
        compensate: async ct =>
        {
            await _shipping.CancelAsync(shipmentId, ct);
        })
    .Build();

var result = await _executor.ExecuteAsync(saga);

if (result.Status == SagaStatus.Succeeded)
{
    _logger.LogInformation("Order created successfully");
}
else
{
    _logger.LogWarning("Order creation failed and was compensated");
}
```

### 2. 旅游预订（携程式场景）

```csharp
var saga = SagaBuilder.Create($"book-trip-{tripId}")
    .AddStep("BookFlight",
        execute: async ct => await _flight.BookAsync(flightInfo, ct),
        compensate: async ct => await _flight.CancelAsync(flightId, ct))
    .AddStep("BookHotel",
        execute: async ct => await _hotel.BookAsync(hotelInfo, ct),
        compensate: async ct => await _hotel.CancelAsync(hotelId, ct))
    .AddStep("RentCar",
        execute: async ct => await _car.RentAsync(carInfo, ct),
        compensate: async ct => await _car.CancelAsync(carId, ct))
    .Build();

var result = await _executor.ExecuteAsync(saga);
```

### 3. 微服务间协调

```csharp
var saga = SagaBuilder.Create()
    .AddStep("CreateUser",
        execute: async ct => await _userService.CreateAsync(user, ct),
        compensate: async ct => await _userService.DeleteAsync(userId, ct))
    .AddStep("SendWelcomeEmail",
        execute: async ct => await _emailService.SendWelcomeAsync(email, ct),
        compensate: async ct => { /* Email sent, no compensation */ })
    .AddStep("CreateWallet",
        execute: async ct => await _walletService.CreateAsync(userId, ct),
        compensate: async ct => await _walletService.DeleteAsync(userId, ct))
    .Build();

await _executor.ExecuteAsync(saga);
```

---

## 🎯 最佳实践

### 1. 幂等性

确保每个步骤可以安全地重复执行：

```csharp
// ✅ 推荐：幂等设计
.AddStep("ProcessPayment",
    execute: async ct =>
    {
        var existingPayment = await _db.Payments
            .FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

        if (existingPayment != null)
        {
            return; // 已处理，跳过
        }

        await _payment.ProcessAsync(orderInfo, ct);
    },
    compensate: async ct =>
    {
        await _payment.RefundAsync(orderId, ct);
    })
```

### 2. 补偿逻辑的设计

```csharp
// ✅ 推荐：完整的补偿逻辑
.AddStep("ReserveInventory",
    execute: async ct =>
    {
        _reservationId = await _inventory.ReserveAsync(items, ct);
    },
    compensate: async ct =>
    {
        if (_reservationId != null)
        {
            await _inventory.ReleaseAsync(_reservationId, ct);
        }
    })

// ❌ 避免：无法撤销的操作
.AddStep("SendNotification",
    execute: async ct => await _sms.SendAsync(message, ct),
    compensate: async ct => { /* SMS already sent, cannot undo */ })
```

### 3. 错误处理

```csharp
var result = await _executor.ExecuteAsync(saga);

switch (result.Status)
{
    case SagaStatus.Succeeded:
        _logger.LogInformation(
            "Saga {SagaId} completed in {Duration}ms",
            result.SagaId,
            result.Duration.TotalMilliseconds);
        break;

    case SagaStatus.Compensated:
        _logger.LogWarning(
            "Saga {SagaId} failed and was compensated: {Error}",
            result.SagaId,
            result.ErrorMessage);
        break;

    case SagaStatus.Failed:
        _logger.LogError(
            "Saga {SagaId} failed and compensation also failed: {Error}",
            result.SagaId,
            result.ErrorMessage);
        break;
}
```

### 4. 使用有意义的步骤名称

```csharp
// ✅ 推荐：清晰的步骤名称
.AddStep("ReserveInventoryForOrder",
    execute: ...,
    compensate: ...)
.AddStep("ChargeCustomerPayment",
    execute: ...,
    compensate: ...)

// ❌ 避免：模糊的名称
.AddStep("Step1", execute: ..., compensate: ...)
.AddStep("DoSomething", execute: ..., compensate: ...)
```

---

## 🔧 高级用法

### 带数据的 Saga

```csharp
public class OrderData
{
    public long OrderId { get; set; }
    public long ReservationId { get; set; }
    public string TransactionId { get; set; } = "";
}

var saga = SagaBuilder.Create()
    .AddStep<OrderData>("ReserveInventory",
        execute: async (data, ct) =>
        {
            var reservationId = await _inventory.ReserveAsync(items, ct);
            data.ReservationId = reservationId;
            return data;
        },
        compensate: async (data, ct) =>
        {
            await _inventory.ReleaseAsync(data.ReservationId, ct);
        },
        initialData: new OrderData { OrderId = orderId })
    .AddStep<OrderData>("ProcessPayment",
        execute: async (data, ct) =>
        {
            var txId = await _payment.ChargeAsync(amount, ct);
            data.TransactionId = txId;
            return data;
        },
        compensate: async (data, ct) =>
        {
            await _payment.RefundAsync(data.TransactionId, ct);
        },
        initialData: new OrderData())
    .Build();
```

### 自定义 Saga 步骤

```csharp
public class ReserveInventoryStep : ISagaStep
{
    private readonly IInventoryService _inventory;
    private long _reservationId;

    public string Name => "ReserveInventory";

    public ReserveInventoryStep(IInventoryService inventory)
    {
        _inventory = inventory;
    }

    public async ValueTask ExecuteAsync(CancellationToken ct)
    {
        _reservationId = await _inventory.ReserveAsync(items, ct);
    }

    public async ValueTask CompensateAsync(CancellationToken ct)
    {
        await _inventory.ReleaseAsync(_reservationId, ct);
    }
}

// 使用
var saga = SagaBuilder.Create()
    .AddStep(new ReserveInventoryStep(_inventory))
    .AddStep(new ProcessPaymentStep(_payment))
    .Build();
```

---

## 📊 Saga 状态

| 状态 | 描述 | 后续操作 |
|------|------|----------|
| `Succeeded` | 所有步骤成功 | 无需操作 |
| `Compensated` | 失败并已补偿 | 记录日志 |
| `Failed` | 失败且补偿失败 | 人工介入 |

---

## 🐛 故障排查

### Saga 总是失败

**问题**：Saga 执行总是返回 `Compensated`

**可能原因**：
1. 某个步骤抛出异常
2. 外部服务不可用
3. 网络超时

**解决方案**：
- 检查日志查看具体失败步骤
- 添加重试机制
- 使用断路器保护

### 补偿失败

**问题**：补偿事务也失败

**可能原因**：
1. 补偿逻辑有 Bug
2. 外部服务状态不一致
3. 资源已被删除

**解决方案**：
- 设计健壮的补偿逻辑
- 添加幂等性检查
- 实现人工补偿机制

---

## 📚 Saga vs 传统事务

| 特性 | 传统事务 (ACID) | Saga |
|------|----------------|------|
| **一致性** | 强一致性 | 最终一致性 |
| **隔离性** | 完全隔离 | 无隔离 |
| **适用场景** | 单数据库 | 分布式系统 |
| **性能** | 可能较慢 | 更快 |
| **复杂度** | 简单 | 较复杂 |

---

## 🎯 性能特征

- **轻量级** - 最小化内存分配
- **异步执行** - 全异步设计
- **详细日志** - 完整的执行追踪
- **补偿机制** - 自动反向补偿

---

## 📚 相关文档

- [分布式锁](distributed-lock.md)
- [Outbox/Inbox 模式](outbox-inbox.md)
- [事件溯源](event-sourcing.md)

---

**需要帮助？** 查看 [Catga 文档](../README.md) 或提交 issue。

