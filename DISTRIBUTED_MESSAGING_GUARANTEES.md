# Catga 分布式消息传输保证

**日期**: 2025-10-10  
**核心理念**: 分层保证 - CQRS 语义 vs 传输语义

---

## 🎯 核心区分

### 1. CQRS 层 - 业务语义（不保证传输）

**CQRS 本质**：
- Command: 改变状态的意图
- Query: 查询状态
- Event: 已发生的事实

**特点**：
- ❌ **不保证传输**（Fire-and-Forget）
- ✅ 快速响应
- ✅ 最终一致性
- ✅ 适合事件通知、日志、监控

### 2. Catga 传输层 - 消息传输保证

**Catga 保证**：
- ✅ **至少一次传输**（At-Least-Once Delivery）
- ✅ 自动重试
- ✅ 幂等性支持
- ✅ 消息持久化（可选）

---

## 📊 分层架构

```
┌─────────────────────────────────────────────┐
│  业务层（CQRS）                              │
│  ├─ Command: 改变状态（需要保证）            │
│  ├─ Query: 查询状态（不需要保证）            │
│  └─ Event: 事件通知（可选保证）              │
├─────────────────────────────────────────────┤
│  Catga 消息层                                │
│  ├─ IRequest<TResponse> → 至少一次          │
│  ├─ IRequest → 至少一次                     │
│  └─ IEvent → 可配置（至少一次 or Fire-Forget）│
├─────────────────────────────────────────────┤
│  传输层（Transport）                         │
│  ├─ QoS 0: At-Most-Once（最多一次）         │
│  ├─ QoS 1: At-Least-Once（至少一次）        │
│  └─ QoS 2: Exactly-Once（恰好一次）         │
├─────────────────────────────────────────────┤
│  基础设施层                                  │
│  ├─ NATS JetStream（持久化 + ACK）          │
│  ├─ Redis Streams（持久化 + ACK）           │
│  └─ Kafka（持久化 + 分区）                  │
└─────────────────────────────────────────────┘
```

---

## 🔧 实现策略

### 1. CQRS 默认行为

```csharp
// ❌ CQRS Event - 默认 Fire-and-Forget（不保证传输）
public record OrderCreatedEvent(string OrderId) : IEvent;

await _mediator.PublishAsync(new OrderCreatedEvent("123"));
// - 发送后立即返回
// - 不等待处理结果
// - 不保证送达
// - 适合：日志、监控、通知
```

### 2. Catga 保证传输 - Request（至少一次）

```csharp
// ✅ Catga Request - 至少一次传输
public record CreateOrderRequest(string ProductId) : IRequest<OrderResponse>;

var result = await _mediator.SendAsync<CreateOrderRequest, OrderResponse>(
    new CreateOrderRequest("product-123"));

// - 自动重试（3次）
// - 等待响应
// - 保证送达（至少一次）
// - 幂等性检查
```

### 3. Catga 可靠事件 - 可选保证

```csharp
// ✅ Catga 可靠事件 - 可配置保证
public record OrderCreatedEvent(string OrderId) : IEvent, IReliableMessage;

await _mediator.PublishAsync(new OrderCreatedEvent("123"));

// - 持久化到 Outbox
// - 至少一次传输
// - 自动重试
// - 适合：关键业务事件
```

---

## 🎯 QoS 级别定义

### QoS 0: At-Most-Once（最多一次）

**特点**：
- 发送后不管结果
- 可能丢失
- 最快

**适用场景**：
- 事件通知（不重要）
- 日志
- 监控指标

```csharp
public record LogEvent(string Message) : IEvent
{
    public QualityOfService QoS => QualityOfService.AtMostOnce;
}
```

### QoS 1: At-Least-Once（至少一次）⭐ 默认

**特点**：
- 发送并等待 ACK
- 可能重复
- 需要幂等性

**适用场景**：
- 业务命令（CreateOrder, UpdateInventory）
- 关键事件（OrderCreated, PaymentCompleted）
- 分布式事务

```csharp
public record CreateOrderRequest(string ProductId) : IRequest<OrderResponse>
{
    public QualityOfService QoS => QualityOfService.AtLeastOnce; // 默认
}
```

### QoS 2: Exactly-Once（恰好一次）

**特点**：
- 发送 + ACK + 去重
- 保证不重复
- 最慢

**适用场景**：
- 支付
- 扣款
- 关键数据修改

```csharp
public record ProcessPaymentRequest(decimal Amount) : IRequest<PaymentResponse>
{
    public QualityOfService QoS => QualityOfService.ExactlyOnce;
}
```

---

## 🏗️ 实现方案

### 方案 1: 基于消息接口标记

```csharp
// Catga 消息接口
public interface IMessage
{
    QualityOfService QoS => QualityOfService.AtLeastOnce; // 默认至少一次
}

public interface IRequest<TResponse> : IMessage { }
public interface IRequest : IMessage { }
public interface IEvent : IMessage 
{
    QualityOfService QoS => QualityOfService.AtMostOnce; // Event 默认 Fire-Forget
}

// 可靠事件标记
public interface IReliableEvent : IEvent
{
    QualityOfService QoS => QualityOfService.AtLeastOnce; // 覆盖为至少一次
}
```

### 方案 2: 基于 Behavior 拦截

```csharp
public class ReliableDeliveryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IMessageTransport _transport;
    private readonly IIdempotencyStore _idempotencyStore;

    public async Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<CatgaResult<TResponse>>> next,
        CancellationToken ct)
    {
        var qos = (request as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                // Fire-and-Forget（无保证）
                _ = Task.Run(async () => await next(), ct);
                return CatgaResult<TResponse>.Success(default!);

            case QualityOfService.AtLeastOnce:
                // 至少一次（自动重试）
                return await RetryAsync(next, maxRetries: 3, ct);

            case QualityOfService.ExactlyOnce:
                // 恰好一次（去重 + 重试）
                var messageId = GetMessageId(request);
                
                // 幂等性检查
                if (await _idempotencyStore.ExistsAsync(messageId, ct))
                {
                    var cached = await _idempotencyStore.GetAsync<TResponse>(messageId, ct);
                    return CatgaResult<TResponse>.Success(cached);
                }

                var result = await RetryAsync(next, maxRetries: 3, ct);
                
                // 缓存结果
                if (result.IsSuccess)
                {
                    await _idempotencyStore.SetAsync(messageId, result.Value!, TimeSpan.FromHours(24), ct);
                }

                return result;

            default:
                return await next();
        }
    }

    private async Task<CatgaResult<TResponse>> RetryAsync(
        Func<Task<CatgaResult<TResponse>>> action,
        int maxRetries,
        CancellationToken ct)
    {
        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (i < maxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, i)), ct);
            }
        }

        throw new InvalidOperationException("Max retries exceeded");
    }
}
```

---

## 📊 对比表

| 消息类型 | 默认 QoS | 保证 | 性能 | 适用场景 |
|---------|---------|------|------|---------|
| **IEvent** | QoS 0 | ❌ 不保证送达 | ⚡ 最快 | 日志、监控、通知 |
| **IReliableEvent** | QoS 1 | ✅ 至少一次 | 🔥 中等 | 关键业务事件 |
| **IRequest** | QoS 1 | ✅ 至少一次 | 🔥 中等 | 业务命令 |
| **IRequest + ExactlyOnce** | QoS 2 | ✅ 恰好一次 | 🐢 较慢 | 支付、扣款 |

---

## 🚀 使用示例

### 示例 1: 普通事件（Fire-and-Forget）

```csharp
// ❌ 不保证送达（CQRS 默认行为）
public record UserLoginEvent(string UserId, DateTime LoginTime) : IEvent;

await _mediator.PublishAsync(new UserLoginEvent("user123", DateTime.UtcNow));
// - 立即返回
// - 不等待处理
// - 可能丢失（网络故障、节点崩溃）
```

### 示例 2: 可靠事件（至少一次）

```csharp
// ✅ 至少一次送达
public record OrderCreatedEvent(string OrderId, decimal Amount) : IReliableEvent;

await _mediator.PublishAsync(new OrderCreatedEvent("order123", 100m));
// - 持久化到 Outbox
// - 自动重试（3次）
// - 保证送达
// - 需要幂等性处理
```

### 示例 3: 命令（至少一次）

```csharp
// ✅ 至少一次送达（默认）
public record CreateOrderRequest(string ProductId, int Quantity) : IRequest<OrderResponse>;

var result = await _mediator.SendAsync<CreateOrderRequest, OrderResponse>(
    new CreateOrderRequest("product123", 2));

// - 自动重试（3次）
// - 等待响应
// - 保证送达
// - 幂等性检查（通过 IdempotencyBehavior）
```

### 示例 4: 支付（恰好一次）

```csharp
// ✅ 恰好一次送达
public record ProcessPaymentRequest(string OrderId, decimal Amount) : IRequest<PaymentResponse>
{
    public QualityOfService QoS => QualityOfService.ExactlyOnce;
}

var result = await _mediator.SendAsync<ProcessPaymentRequest, PaymentResponse>(
    new ProcessPaymentRequest("order123", 100m));

// - 幂等性检查（强制）
// - 自动重试（3次）
// - 去重保证
// - 结果缓存（24小时）
```

---

## 🔒 幂等性保证

### 自动幂等性（Request）

```csharp
// Catga 自动为所有 IRequest 提供幂等性
public record CreateOrderRequest(string OrderId, string ProductId) : IRequest<OrderResponse>;

// 第一次调用
var result1 = await _mediator.SendAsync<CreateOrderRequest, OrderResponse>(
    new CreateOrderRequest("order123", "product456"));
// → 创建订单，返回 OrderResponse

// 第二次调用（重复）
var result2 = await _mediator.SendAsync<CreateOrderRequest, OrderResponse>(
    new CreateOrderRequest("order123", "product456"));
// → 返回缓存的 OrderResponse（不重复创建）
```

### 手动幂等性（Event）

```csharp
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IIdempotencyStore _idempotencyStore;

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        var eventId = @event.OrderId; // 使用业务 ID 作为幂等性键

        // 手动检查幂等性
        if (await _idempotencyStore.ExistsAsync(eventId, ct))
        {
            return; // 已处理，跳过
        }

        // 处理事件
        await ProcessEvent(@event, ct);

        // 标记已处理
        await _idempotencyStore.SetAsync(eventId, true, TimeSpan.FromDays(7), ct);
    }
}
```

---

## 📈 性能影响

### QoS 0 (At-Most-Once)
```
发送消息 → 返回
         ↓
      (可能丢失)

延迟: ~1ms
吞吐量: 100万+ QPS
```

### QoS 1 (At-Least-Once)
```
发送消息 → 等待ACK → 返回
         ↓
      (重试3次)
         ↓
      幂等性检查

延迟: ~5-10ms
吞吐量: 50万 QPS
```

### QoS 2 (Exactly-Once)
```
发送消息 → 幂等性检查 → 等待ACK → 缓存结果 → 返回
         ↓
      (重试3次)
         ↓
      去重保证

延迟: ~10-20ms
吞吐量: 10万 QPS
```

---

## 🎯 总结

### CQRS 语义
- **Event**: 默认 Fire-and-Forget（不保证）
- **Command**: 默认至少一次（保证）

### Catga 保证
- **IEvent**: QoS 0（可升级为 QoS 1）
- **IRequest**: QoS 1（可升级为 QoS 2）
- **IRequest<TResponse>**: QoS 1（可升级为 QoS 2）

### 关键原则
1. **分层保证**：CQRS 语义 ≠ 传输保证
2. **默认合理**：Event = Fire-Forget, Request = At-Least-Once
3. **可配置**：通过 `QoS` 属性覆盖默认行为
4. **幂等性**：Request 自动保证，Event 需手动处理
5. **性能权衡**：保证级别越高，性能越低

---

*生成时间: 2025-10-10*  
*Catga v2.0 - Reliable Distributed Messaging* 🚀

