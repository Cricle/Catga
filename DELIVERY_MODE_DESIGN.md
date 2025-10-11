# 投递模式设计文档

## 📋 需求

用户要求：**"保证到达得有2种模式，等结果和不等结果但确保至少一次"**

---

## 🎯 设计方案

### 核心概念

**至少一次送达（At-Least-Once Delivery）** 有两种实现模式：

#### 1. **等待结果（Wait for Result）** - 同步确认
- 发送方等待接收方处理完成
- 阻塞调用直到成功或失败
- 立即知道结果
- 适用场景：支付、订单确认、需要立即反馈的操作

#### 2. **异步重试（Async Retry）** - 不等结果但确保送达
- 发送方立即返回，不等待结果
- 后台重试机制保证至少一次送达
- 使用指数退避策略
- 适用场景：通知、日志、数据同步、高吞吐场景

---

## 📊 模式对比

| 特性 | 等待结果 | 异步重试 |
|------|---------|---------|
| **阻塞** | ✅ 是 | ❌ 否 |
| **立即反馈** | ✅ 是 | ❌ 否 |
| **吞吐量** | 低 | 高 |
| **延迟** | 高 | 低 |
| **可靠性** | 高 | 高 |
| **重试** | 应用层手动 | 自动后台重试 |
| **适用场景** | 关键操作 | 非关键操作 |

---

## 🔧 实现

### 1. 新增 `DeliveryMode` 枚举

```csharp
public enum DeliveryMode
{
    /// <summary>
    /// 等待结果（Wait for Result）
    /// - 同步等待消息处理完成
    /// - 等待 ACK 确认
    /// - 阻塞调用直到成功或失败
    /// </summary>
    WaitForResult = 0,

    /// <summary>
    /// 异步重试（Async Retry）
    /// - 不等待结果，立即返回
    /// - 后台重试机制保证至少一次送达
    /// - 使用持久化队列（Outbox、JetStream、Redis Streams）
    /// </summary>
    AsyncRetry = 1
}
```

### 2. 扩展 `IMessage` 接口

```csharp
public interface IMessage
{
    QualityOfService QoS => QualityOfService.AtLeastOnce;

    /// <summary>
    /// 投递模式（仅对 QoS 1/2 有效）
    /// </summary>
    DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}
```

### 3. Transport 实现

#### InMemoryMessageTransport

```csharp
var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;
var deliveryMode = (message as IMessage)?.DeliveryMode ?? DeliveryMode.WaitForResult;

switch (qos)
{
    case QualityOfService.AtMostOnce:
        // QoS 0: Fire-and-forget
        _ = FireAndForgetAsync(handlers, message, context, cancellationToken);
        break;

    case QualityOfService.AtLeastOnce:
        if (deliveryMode == DeliveryMode.WaitForResult)
        {
            // 模式 1: 等待结果（同步确认）
            var tasks = new Task[handlers.Count];
            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                tasks[i] = handler(message, context);
            }
            await Task.WhenAll(tasks);
        }
        else
        {
            // 模式 2: 异步重试（不等结果但确保至少一次）
            _ = DeliverWithRetryAsync(handlers, message, context, cancellationToken);
        }
        break;
}
```

#### 异步重试实现

```csharp
private static async ValueTask DeliverWithRetryAsync<TMessage>(
    List<Delegate> handlers,
    TMessage message,
    TransportContext context,
    CancellationToken cancellationToken) where TMessage : class
{
    const int maxRetries = 3;
    var baseDelay = TimeSpan.FromMilliseconds(100);

    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        try
        {
            var tasks = new Task[handlers.Count];
            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                tasks[i] = handler(message, context);
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            
            // 成功，退出
            return;
        }
        catch when (attempt < maxRetries)
        {
            // 失败但还有重试次数，指数退避
            var delay = TimeSpan.FromMilliseconds(
                baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 最后一次重试失败
            // 生产环境应该记录到死信队列或告警
        }
    }
}
```

---

## 📝 使用示例

### 示例 1: 等待结果（默认）

```csharp
// 支付命令 - 需要立即知道结果
public record ProcessPaymentCommand(decimal Amount) : IRequest<PaymentResult>
{
    // 默认：QoS = AtLeastOnce, DeliveryMode = WaitForResult
}

// 使用
var result = await mediator.SendAsync(new ProcessPaymentCommand(100.00m));
// 阻塞等待，直到支付完成或失败
```

### 示例 2: 异步重试

```csharp
// 发送通知 - 不需要立即反馈
public record SendNotificationCommand(string UserId, string Message) : IRequest
{
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.AsyncRetry;
}

// 使用
await mediator.SendAsync(new SendNotificationCommand("user123", "Hello"));
// 立即返回，后台异步重试确保送达
```

### 示例 3: Fire-and-Forget（QoS 0）

```csharp
// 日志事件 - 不保证送达
public record LogEvent(string Message) : IEvent
{
    // IEvent 默认：QoS = AtMostOnce（火忘）
}

// 使用
await mediator.PublishAsync(new LogEvent("User logged in"));
// 立即返回，不等待，不重试
```

---

## 🎯 QoS + DeliveryMode 组合

| QoS | DeliveryMode | 行为 | 适用场景 |
|-----|-------------|------|---------|
| AtMostOnce | （忽略） | 发送即忘，不保证 | 日志、监控 |
| AtLeastOnce | WaitForResult | 等待确认，阻塞 | 支付、订单 |
| AtLeastOnce | AsyncRetry | 后台重试，不阻塞 | 通知、同步 |
| ExactlyOnce | WaitForResult | 幂等检查 + 等待 | 金融交易 |

---

## 🚀 重试策略

### 指数退避（Exponential Backoff）

```
Attempt 0: 立即执行
Attempt 1: 延迟 100ms (baseDelay * 2^0)
Attempt 2: 延迟 200ms (baseDelay * 2^1)
Attempt 3: 延迟 400ms (baseDelay * 2^2)
```

### 配置参数

- `maxRetries`: 最大重试次数（默认 3）
- `baseDelay`: 基础延迟（默认 100ms）
- `exponentialFactor`: 指数因子（默认 2）

---

## 📈 性能特征

### 等待结果（WaitForResult）

```
发送方 --[发送消息]--> 接收方
   |                      |
   |                  处理中...
   |                      |
   <----[返回结果]--------
   |
继续执行
```

**性能**:
- 吞吐量: 低（受接收方处理速度限制）
- 延迟: 高（等待处理完成）
- 可靠性: 高（立即知道成功或失败）

### 异步重试（AsyncRetry）

```
发送方 --[发送消息]--> 后台队列 --[重试]--> 接收方
   |
立即返回
   |
继续执行
```

**性能**:
- 吞吐量: 高（不阻塞）
- 延迟: 低（立即返回）
- 可靠性: 高（重试保证）

---

## ✅ 优势

1. **灵活性**: 开发者可根据场景选择模式
2. **简单性**: 只需设置 `DeliveryMode` 属性
3. **一致性**: 统一的 API，不同的行为
4. **性能**: AsyncRetry 模式高吞吐量
5. **可靠性**: 两种模式都保证至少一次送达

---

## 🔜 未来增强

1. **持久化队列**: 集成 Outbox、JetStream、Redis Streams
2. **死信队列**: 重试失败后的消息处理
3. **监控告警**: 失败重试的可观测性
4. **配置化重试策略**: 允许自定义重试参数
5. **优先级队列**: 不同消息的不同重试策略

---

## 📋 总结

通过 `DeliveryMode`，我们为"至少一次"提供了两种实现：

- ✅ **WaitForResult**: 同步等待，立即反馈
- ✅ **AsyncRetry**: 异步重试，高吞吐量

两种模式都保证**至少一次送达**，但在延迟、吞吐量和使用场景上有所不同。


