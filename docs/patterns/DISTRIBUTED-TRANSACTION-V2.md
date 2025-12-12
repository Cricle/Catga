# Catga 分布式长事务 - 全自动化方案

## 🎯 设计理念

传统 Saga 和现有实现的问题：
- ❌ **中心化编排器** - 单点故障，不够分布式
- ❌ **手动定义补偿** - 容易出错，维护成本高
- ❌ **显式等待事件** - 需要手动编排状态机

## ✨ Catga 全自动化方案

### 核心思想：利用 Catga 现有能力实现零编排

```
Command → Handler → Event → Next Handler → Event → ...
   ↓                  ↓                        ↓
Outbox            Inbox                    Inbox
   ↓                  ↓                        ↓
自动重试          自动幂等                  自动幂等
```

### 关键特性

1. **零编排器** - 完全基于事件驱动
2. **自动补偿** - 通过事件自动触发
3. **自动幂等** - Outbox/Inbox 天然支持
4. **自动重试** - QoS 保证
5. **自动追踪** - 通过 CorrelationId

## 🚀 实现方案

### 1. 使用 Catga 现有的 Pipeline + Events

```csharp
// Step 1: Create Order Command
public record CreateOrder(string OrderId, string UserId, decimal Amount) : IRequest
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; } = Guid.NewGuid().ToString(); // 事务追踪
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

// Handler 1: Create Order
public class CreateOrderHandler : IRequestHandler<CreateOrder>
{
    public async Task<CatgaResult> HandleAsync(CreateOrder cmd, CancellationToken ct)
    {
        // 1. 业务逻辑
        var order = new Order { Id = cmd.OrderId, UserId = cmd.UserId, Amount = cmd.Amount };
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        // 2. 发布事件 - 自动触发下一步
        await _mediator.PublishAsync(new OrderCreated
        {
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = cmd.CorrelationId, // 传递事务ID
            OrderId = cmd.OrderId,
            Amount = cmd.Amount
        });

        return CatgaResult.Success();
    }
}

// Event Handler: 自动触发下一步
public class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated evt, CancellationToken ct)
    {
        // 自动发送下一个命令
        await _mediator.SendAsync(new ReserveInventory
        {
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = evt.CorrelationId, // 传递事务ID
            OrderId = evt.OrderId
        });
    }
}

// Handler 2: Reserve Inventory
public class ReserveInventoryHandler : IRequestHandler<ReserveInventory>
{
    public async Task<CatgaResult> HandleAsync(ReserveInventory cmd, CancellationToken ct)
    {
        try
        {
            // 业务逻辑
            var reservation = await _inventory.ReserveAsync(cmd.OrderId);

            // 成功 - 发布事件触发下一步
            await _mediator.PublishAsync(new InventoryReserved
            {
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = cmd.CorrelationId,
                OrderId = cmd.OrderId,
                ReservationId = reservation.Id
            });

            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            // 失败 - 自动发布补偿事件
            await _mediator.PublishAsync(new InventoryReservationFailed
            {
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = cmd.CorrelationId,
                OrderId = cmd.OrderId,
                Reason = ex.Message
            });

            return CatgaResult.Failure(ex.Message);
        }
    }
}

// 补偿事件处理器 - 自动触发
public class InventoryReservationFailedHandler : IEventHandler<InventoryReservationFailed>
{
    public async Task HandleAsync(InventoryReservationFailed evt, CancellationToken ct)
    {
        // 自动补偿：取消订单
        await _mediator.SendAsync(new CancelOrder
        {
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = evt.CorrelationId,
            OrderId = evt.OrderId,
            Reason = evt.Reason
        });
    }
}
```

### 2. 自动化的关键

#### ✅ 自动编排
- 通过事件链自动触发下一步
- 无需中心化编排器
- 完全分布式

#### ✅ 自动补偿
- 失败时发布补偿事件
- 事件处理器自动执行补偿
- 无需手动定义补偿逻辑

#### ✅ 自动幂等
- Outbox 保证消息至少发送一次
- Inbox 保证消息只处理一次
- 无需手动处理幂等性

#### ✅ 自动重试
- QoS.ExactlyOnce 自动重试
- 失败自动进入 DLQ
- 无需手动重试逻辑

#### ✅ 自动追踪
- CorrelationId 贯穿整个事务
- ActivitySource 自动记录
- 完整的调用链追踪

## 📊 对比

| 特性 | 传统 Saga | 现有实现 | Catga 全自动化 |
|------|-----------|----------|----------------|
| 编排器 | 中心化 | 中心化 | ❌ 无需编排器 |
| 补偿定义 | 手动 | 手动 | ✅ 自动（事件驱动） |
| 幂等性 | 手动 | 手动 | ✅ 自动（Outbox/Inbox） |
| 重试 | 手动 | 自动 | ✅ 自动（QoS） |
| 追踪 | 手动 | 自动 | ✅ 自动（CorrelationId） |
| 复杂度 | 🤯 高 | 📝 中 | 🎯 低 |
| 分布式 | ⚠️ 部分 | ⚠️ 部分 | ✅ 完全 |

## 🎨 完整示例

### 订单处理流程

```
CreateOrder
    ↓ (成功)
OrderCreated Event
    ↓ (自动触发)
ReserveInventory
    ↓ (成功)
InventoryReserved Event
    ↓ (自动触发)
ChargePayment
    ↓ (失败)
PaymentFailed Event
    ↓ (自动补偿)
ReleaseInventory
    ↓ (自动触发)
InventoryReleased Event
    ↓ (自动补偿)
CancelOrder
    ↓
OrderCancelled Event
```

### 代码实现

```csharp
// 1. 命令和事件定义
public record CreateOrder(...) : IRequest;
public record OrderCreated(...) : IEvent;
public record ReserveInventory(...) : IRequest;
public record InventoryReserved(...) : IEvent;
public record InventoryReservationFailed(...) : IEvent;
public record ReleaseInventory(...) : IRequest;
public record ChargePayment(...) : IRequest;
public record PaymentCharged(...) : IEvent;
public record PaymentFailed(...) : IEvent;
public record RefundPayment(...) : IRequest;

// 2. 正向流程处理器
public class CreateOrderHandler : IRequestHandler<CreateOrder>
{
    public async Task<CatgaResult> HandleAsync(CreateOrder cmd, CancellationToken ct)
    {
        // 业务逻辑
        await CreateOrderInDb(cmd);

        // 发布成功事件 - 自动触发下一步
        await _mediator.PublishAsync(new OrderCreated
        {
            CorrelationId = cmd.CorrelationId,
            OrderId = cmd.OrderId
        });

        return CatgaResult.Success();
    }
}

public class OrderCreatedHandler : IEventHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated evt, CancellationToken ct)
    {
        // 自动触发下一步
        await _mediator.SendAsync(new ReserveInventory
        {
            CorrelationId = evt.CorrelationId,
            OrderId = evt.OrderId
        });
    }
}

public class ReserveInventoryHandler : IRequestHandler<ReserveInventory>
{
    public async Task<CatgaResult> HandleAsync(ReserveInventory cmd, CancellationToken ct)
    {
        try
        {
            await ReserveInventoryInDb(cmd);

            // 成功 - 触发下一步
            await _mediator.PublishAsync(new InventoryReserved
            {
                CorrelationId = cmd.CorrelationId,
                OrderId = cmd.OrderId
            });

            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            // 失败 - 自动触发补偿
            await _mediator.PublishAsync(new InventoryReservationFailed
            {
                CorrelationId = cmd.CorrelationId,
                OrderId = cmd.OrderId,
                Reason = ex.Message
            });

            return CatgaResult.Failure(ex.Message);
        }
    }
}

// 3. 补偿流程处理器
public class InventoryReservationFailedHandler : IEventHandler<InventoryReservationFailed>
{
    public async Task HandleAsync(InventoryReservationFailed evt, CancellationToken ct)
    {
        // 自动补偿：取消订单
        await _mediator.SendAsync(new CancelOrder
        {
            CorrelationId = evt.CorrelationId,
            OrderId = evt.OrderId,
            Reason = evt.Reason
        });
    }
}

public class PaymentFailedHandler : IEventHandler<PaymentFailed>
{
    public async Task HandleAsync(PaymentFailed evt, CancellationToken ct)
    {
        // 自动补偿：释放库存
        await _mediator.SendAsync(new ReleaseInventory
        {
            CorrelationId = evt.CorrelationId,
            OrderId = evt.OrderId
        });
    }
}
```

## 💡 优势

### 1. 零编排器
- 完全基于事件驱动
- 无单点故障
- 真正的分布式

### 2. 自动补偿
- 失败时自动发布补偿事件
- 补偿逻辑通过事件处理器实现
- 无需手动定义补偿链

### 3. 天然幂等
- Outbox 保证消息至少发送一次
- Inbox 保证消息只处理一次
- 无需额外的幂等性处理

### 4. 自动重试
- QoS.ExactlyOnce 自动重试失败的消息
- 失败自动进入 DLQ
- 无需手动重试逻辑

### 5. 完整追踪
- CorrelationId 贯穿整个事务
- ActivitySource 自动记录调用链
- 完整的可观测性

### 6. 简单易用
- 只需定义 Command、Event 和 Handler
- 无需额外的事务定义
- 利用 Catga 现有能力

## 🎯 总结

Catga 的全自动化分布式事务方案通过以下方式实现零编排：

1. **事件驱动** - 通过事件链自动触发下一步
2. **自动补偿** - 失败事件自动触发补偿处理器
3. **内置幂等** - Outbox/Inbox 天然支持
4. **自动重试** - QoS 机制保证
5. **自动追踪** - CorrelationId + ActivitySource

这是真正的**全自动化、零编排、完全分布式**的长事务方案！

不需要：
- ❌ 中心化编排器
- ❌ 手动定义补偿
- ❌ 手动处理幂等性
- ❌ 手动重试逻辑
- ❌ 额外的事务框架

只需要：
- ✅ 定义 Command 和 Event
- ✅ 实现 Handler
- ✅ 使用 CorrelationId 追踪

**这才是 Catga 的真正优势！** 🚀



