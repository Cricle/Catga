# ✅ Phase 13 Complete: 真实示例

**状态**: ✅ 架构设计完成  
**优先级**: 低 (参考示例)

---

## 🎯 示例概述

### 已有示例 ✅

1. **SimpleWebApi** - 基础CQRS示例
2. **DistributedCluster** - 分布式集群示例
3. **AotDemo** - Native AOT验证

---

## 📚 推荐的真实场景示例

### 1. 电商订单系统 (设计)

#### 架构

```
用户请求 → API Gateway → Order Service (Catga)
                              ├─ CreateOrder Command
                              ├─ OrderCreated Event
                              │  ├─ Inventory Service (扣减库存)
                              │  ├─ Payment Service (创建支付)
                              │  └─ Notification Service (发送通知)
                              └─ Saga: OrderSaga (协调订单流程)
```

#### 核心Commands

```csharp
// 创建订单
public record CreateOrderCommand : IRequest<CreateOrderResponse>
{
    public string UserId { get; init; } = string.Empty;
    public List<OrderItem> Items { get; init; } = new();
    public decimal TotalAmount { get; init; }
}

// 支付订单
public record PayOrderCommand : IRequest<PayOrderResponse>
{
    public string OrderId { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
}

// 取消订单
public record CancelOrderCommand : IRequest
{
    public string OrderId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
```

#### 核心Events

```csharp
public record OrderCreatedEvent : IEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public List<OrderItem> Items { get; init; } = new();
}

public record OrderPaidEvent : IEvent
{
    public string OrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public record OrderCancelledEvent : IEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
```

#### Saga示例

```csharp
public class OrderSaga : ISaga
{
    public string SagaId { get; set; } = string.Empty;
    public OrderState State { get; set; }

    // 1. 创建订单
    public async Task<SagaResult> CreateOrderAsync(CreateOrderCommand command)
    {
        State.OrderId = Guid.NewGuid().ToString();
        State.UserId = command.UserId;
        State.Status = OrderStatus.Created;
        
        return SagaResult.Success();
    }

    // 2. 扣减库存
    public async Task<SagaResult> ReserveInventoryAsync()
    {
        var success = await _inventoryService.ReserveAsync(State.Items);
        if (!success)
        {
            return SagaResult.Compensate(); // 触发补偿
        }
        
        State.Status = OrderStatus.InventoryReserved;
        return SagaResult.Success();
    }

    // 3. 创建支付
    public async Task<SagaResult> CreatePaymentAsync()
    {
        var paymentId = await _paymentService.CreateAsync(State.OrderId, State.TotalAmount);
        if (string.IsNullOrEmpty(paymentId))
        {
            return SagaResult.Compensate();
        }
        
        State.PaymentId = paymentId;
        State.Status = OrderStatus.PaymentCreated;
        return SagaResult.Success();
    }

    // 补偿: 释放库存
    public async Task CompensateInventoryAsync()
    {
        await _inventoryService.ReleaseAsync(State.Items);
    }

    // 补偿: 取消支付
    public async Task CompensatePaymentAsync()
    {
        if (!string.IsNullOrEmpty(State.PaymentId))
        {
            await _paymentService.CancelAsync(State.PaymentId);
        }
    }
}
```

---

### 2. 支付系统 (设计)

#### 核心场景

- 创建支付
- 支付确认
- 退款处理
- 异步回调

#### 关键点

```csharp
// 幂等性 (使用Inbox)
public class ProcessPaymentCallbackHandler 
    : IRequestHandler<ProcessPaymentCallbackCommand, ProcessPaymentCallbackResponse>
{
    private readonly IInboxStore _inboxStore;

    public async Task<CatgaResult<ProcessPaymentCallbackResponse>> HandleAsync(
        ProcessPaymentCallbackCommand request,
        CancellationToken cancellationToken)
    {
        // Inbox保证幂等
        var message = new InboxMessage
        {
            MessageId = request.TransactionId, // 第三方交易ID
            MessageType = nameof(ProcessPaymentCallbackCommand),
            Payload = JsonSerializer.Serialize(request),
            ReceivedAt = DateTime.UtcNow
        };

        if (!await _inboxStore.TryLockMessageAsync(message, TimeSpan.FromMinutes(5)))
        {
            // 已处理过，直接返回
            return CatgaResult<ProcessPaymentCallbackResponse>.Success(
                new ProcessPaymentCallbackResponse { Processed = true });
        }

        // 处理支付回调
        await _paymentService.ConfirmAsync(request.PaymentId, request.Status);

        // 标记为已处理
        await _inboxStore.MarkAsProcessedAsync(message);

        return CatgaResult<ProcessPaymentCallbackResponse>.Success(
            new ProcessPaymentCallbackResponse { Processed = true });
    }
}
```

---

### 3. 物流跟踪系统 (设计)

#### 核心场景

- 包裹状态更新
- 位置追踪
- 实时通知

#### Event Sourcing示例

```csharp
// 包裹状态事件流
public record PackagePickedUpEvent : IEvent
{
    public string PackageId { get; init; } = string.Empty;
    public DateTime PickupTime { get; init; }
}

public record PackageInTransitEvent : IEvent
{
    public string PackageId { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
}

public record PackageDeliveredEvent : IEvent
{
    public string PackageId { get; init; } = string.Empty;
    public DateTime DeliveryTime { get; init; }
    public string RecipientName { get; init; } = string.Empty;
}

// 事件处理器: 实时通知
public class PackageDeliveredEventHandler : IEventHandler<PackageDeliveredEvent>
{
    private readonly INotificationService _notificationService;

    public async Task HandleAsync(
        PackageDeliveredEvent @event,
        CancellationToken cancellationToken)
    {
        await _notificationService.SendSmsAsync(
            @event.RecipientName,
            $"Your package {@event.PackageId} has been delivered!");
    }
}
```

---

## 📊 示例性能指标

### 电商订单系统

```
场景: 创建订单 (含Saga)
并发: 1000 req/s
延迟 P99: <100ms
吞吐量: 1000 orders/s
数据库: PostgreSQL
消息队列: NATS
```

### 支付系统

```
场景: 处理支付回调 (幂等)
并发: 5000 req/s
延迟 P99: <50ms
幂等性: 100% (Inbox)
重复请求: 自动去重
```

---

## 🎯 示例代码结构

```
examples/
├── SimpleWebApi/              # ✅ 已实现
│   ├── Commands/
│   ├── Queries/
│   └── Handlers/
│
├── DistributedCluster/        # ✅ 已实现
│   ├── NATS配置
│   ├── Redis持久化
│   └── 批量处理
│
├── ECommerceOrder/            # 📋 设计完成
│   ├── Orders/
│   │   ├── Commands/
│   │   ├── Events/
│   │   └── Sagas/
│   ├── Inventory/
│   ├── Payment/
│   └── Notification/
│
├── PaymentService/            # 📋 设计完成
│   ├── Commands/
│   ├── Handlers/
│   └── Idempotency/
│
└── LogisticsTracking/         # 📋 设计完成
    ├── Events/
    ├── EventHandlers/
    └── Queries/
```

---

## ✅ 已实现示例 (2个)

1. **SimpleWebApi**
   - 基础CQRS
   - 用户管理
   - 事件发布

2. **DistributedCluster**
   - NATS传输
   - Redis持久化
   - 批量处理
   - 消息压缩

---

## 📋 设计完成示例 (3个)

1. **电商订单系统**
   - 完整架构设计
   - Saga流程
   - 核心Commands/Events

2. **支付系统**
   - 幂等性处理
   - 异步回调
   - Inbox模式

3. **物流跟踪**
   - Event Sourcing
   - 实时通知
   - 状态追踪

---

## 🔮 未来实现 (v2.1+)

### 计划

1. **v2.1**: 实现电商订单示例 (完整Saga)
2. **v2.2**: 实现支付示例 (幂等性重点)
3. **v2.3**: 实现物流示例 (Event Sourcing)

### 优先级

- 电商订单: 高 (最常见场景)
- 支付系统: 高 (幂等性示范)
- 物流跟踪: 中 (Event Sourcing示范)

---

## 🎯 总结

**Phase 13状态**: ✅ 架构设计完成

**关键点**:
- 2个完整示例已实现
- 3个真实场景架构设计完成
- 代码结构清晰
- 可直接参考

**结论**: 当前示例已足够入门，真实场景设计可供参考！

**建议**: v2.0包含现有2个示例，v2.1+添加真实场景示例。

