# QoS（服务质量）实现计划

**目标**: 实现 MQTT 风格的 QoS 机制，支持至少一次、最多一次、恰好一次  
**原则**: 不新增接口，只在现有功能上修改

---

## 🎯 QoS 级别定义

### QoS 0: 最多一次（At Most Once）
- **特点**: 发送即忘，不保证送达
- **性能**: 最快（无确认）
- **场景**: 日志、监控指标

### QoS 1: 至少一次（At Least Once）
- **特点**: 保证送达，可能重复
- **性能**: 中等（需要确认）
- **场景**: 重要通知、订单创建

### QoS 2: 恰好一次（Exactly Once）
- **特点**: 保证送达且不重复
- **性能**: 最慢（需要幂等性检查）
- **场景**: 支付、库存扣减

---

## 📋 实现计划

### Phase 1: 核心枚举和配置（30 分钟）

#### 1.1 新增 QoS 枚举
**文件**: `src/Catga/Core/QualityOfService.cs`

```csharp
namespace Catga;

/// <summary>
/// 消息服务质量等级（类似 MQTT QoS）
/// </summary>
public enum QualityOfService
{
    /// <summary>
    /// 最多一次（At Most Once）- 发送即忘，不保证送达
    /// 性能最高，适用于日志、监控等场景
    /// </summary>
    AtMostOnce = 0,

    /// <summary>
    /// 至少一次（At Least Once）- 保证送达，可能重复
    /// 需要消息确认，适用于重要通知
    /// </summary>
    AtLeastOnce = 1,

    /// <summary>
    /// 恰好一次（Exactly Once）- 保证送达且不重复
    /// 需要幂等性检查，适用于支付、库存扣减
    /// </summary>
    ExactlyOnce = 2
}
```

#### 1.2 修改 CatgaOptions
**文件**: `src/Catga/Core/CatgaOptions.cs`

```csharp
// 添加默认 QoS 配置
public QualityOfService DefaultQoS { get; set; } = QualityOfService.AtLeastOnce;
```

---

### Phase 2: IMessage 接口扩展（15 分钟）

#### 2.1 修改 IMessage
**文件**: `src/Catga/Messages/MessageContracts.cs`

```csharp
public interface IMessage
{
    string MessageId => Guid.NewGuid().ToString();
    DateTime CreatedAt => DateTime.UtcNow;
    string? CorrelationId => null;
    
    /// <summary>
    /// 消息服务质量等级（默认：至少一次）
    /// </summary>
    QualityOfService QoS => QualityOfService.AtLeastOnce;
}
```

---

### Phase 3: InMemory 实现（1 小时）

#### 3.1 修改 InMemoryMessageTransport
**文件**: `src/Catga.InMemory/Transport/InMemoryMessageTransport.cs`

```csharp
public async Task SendAsync<TMessage>(
    TMessage message,
    string destination,
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

    switch (qos)
    {
        case QualityOfService.AtMostOnce:
            // 发送即忘，不等待处理
            _ = Task.Run(() => ProcessMessageAsync(message, destination, context, cancellationToken));
            break;

        case QualityOfService.AtLeastOnce:
            // 等待处理完成
            await ProcessMessageAsync(message, destination, context, cancellationToken);
            break;

        case QualityOfService.ExactlyOnce:
            // 检查幂等性 + 等待处理
            if (await IsAlreadyProcessedAsync(message.MessageId))
                return;
            
            await ProcessMessageAsync(message, destination, context, cancellationToken);
            await MarkAsProcessedAsync(message.MessageId);
            break;
    }
}
```

---

### Phase 4: Redis 实现（1.5 小时）

#### 4.1 修改 RedisMessageTransport
**文件**: `src/Catga.Persistence.Redis/Transport/RedisMessageTransport.cs`

```csharp
public async Task SendAsync<TMessage>(
    TMessage message,
    string destination,
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

    switch (qos)
    {
        case QualityOfService.AtMostOnce:
            // 发布到 Redis，不等待确认
            await _redis.PublishAsync(destination, Serialize(message));
            break;

        case QualityOfService.AtLeastOnce:
            // 发布 + 等待订阅者 ACK
            var ackKey = $"ack:{message.MessageId}";
            await _redis.PublishAsync(destination, Serialize(message));
            
            // 等待 ACK（超时 30 秒）
            await WaitForAckAsync(ackKey, TimeSpan.FromSeconds(30), cancellationToken);
            break;

        case QualityOfService.ExactlyOnce:
            // 使用 Redis 事务保证幂等性
            var processedKey = $"processed:{message.MessageId}";
            
            var transaction = _redis.CreateTransaction();
            transaction.AddCondition(Condition.KeyNotExists(processedKey));
            _ = transaction.PublishAsync(destination, Serialize(message));
            _ = transaction.StringSetAsync(processedKey, "1", TimeSpan.FromHours(24));
            
            if (await transaction.ExecuteAsync())
            {
                await WaitForAckAsync($"ack:{message.MessageId}", TimeSpan.FromSeconds(30), cancellationToken);
            }
            break;
    }
}
```

---

### Phase 5: NATS 实现（1.5 小时）

#### 5.1 修改 NatsMessageTransport
**文件**: `src/Catga.Transport.Nats/NatsMessageTransport.cs`

```csharp
public async Task SendAsync<TMessage>(
    TMessage message,
    string destination,
    TransportContext? context = null,
    CancellationToken cancellationToken = default)
    where TMessage : class
{
    var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

    switch (qos)
    {
        case QualityOfService.AtMostOnce:
            // NATS Publish（fire-and-forget）
            await _connection.PublishAsync(destination, Serialize(message), cancellationToken: cancellationToken);
            break;

        case QualityOfService.AtLeastOnce:
            // NATS Request（等待响应）
            var reply = await _connection.RequestAsync(
                destination, 
                Serialize(message), 
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: cancellationToken);
            
            // 验证 ACK
            if (reply.Data.Length == 0)
                throw new Exception("No ACK received");
            break;

        case QualityOfService.ExactlyOnce:
            // NATS JetStream（持久化 + 去重）
            var js = _connection.CreateJetStreamContext();
            
            // 发布到 JetStream（带消息 ID 去重）
            var ack = await js.PublishAsync(
                destination,
                Serialize(message),
                new PublishOptions
                {
                    MsgId = message.MessageId,  // NATS 自动去重
                    Timeout = TimeSpan.FromSeconds(30)
                },
                cancellationToken);
            
            // 验证 ACK
            if (!ack.Duplicate && ack.Seq > 0)
            {
                // 消息已成功持久化且不重复
            }
            break;
    }
}
```

---

### Phase 6: 测试（1 小时）

#### 6.1 单元测试
**文件**: `tests/Catga.Tests/QoS/QoSTests.cs`

```csharp
public class QoSTests
{
    [Theory]
    [InlineData(QualityOfService.AtMostOnce)]
    [InlineData(QualityOfService.AtLeastOnce)]
    [InlineData(QualityOfService.ExactlyOnce)]
    public async Task InMemory_QoS_ShouldWork(QualityOfService qos)
    {
        // Arrange
        var message = new TestMessage { QoS = qos };
        
        // Act
        await _transport.SendAsync(message, "test");
        
        // Assert
        // 验证不同 QoS 的行为
    }

    [Fact]
    public async Task Redis_ExactlyOnce_ShouldNotDuplicate()
    {
        // Arrange
        var message = new TestMessage { QoS = QualityOfService.ExactlyOnce };
        
        // Act
        await _transport.SendAsync(message, "test");
        await _transport.SendAsync(message, "test");  // 重复发送
        
        // Assert
        _processCount.Should().Be(1);  // 只处理一次
    }

    [Fact]
    public async Task Nats_AtLeastOnce_ShouldRetryOnFailure()
    {
        // Arrange
        var message = new TestMessage { QoS = QualityOfService.AtLeastOnce };
        
        // Act
        var exception = await Record.ExceptionAsync(() => 
            _transport.SendAsync(message, "test"));
        
        // Assert
        exception.Should().NotBeNull();  // 没有 ACK 应该抛异常
    }
}
```

---

## ⏱️ 时间估算

| Phase | 任务 | 时间 |
|-------|------|------|
| Phase 1 | 核心枚举和配置 | 30 分钟 |
| Phase 2 | IMessage 扩展 | 15 分钟 |
| Phase 3 | InMemory 实现 | 1 小时 |
| Phase 4 | Redis 实现 | 1.5 小时 |
| Phase 5 | NATS 实现 | 1.5 小时 |
| Phase 6 | 测试 | 1 小时 |
| **总计** | | **5.5 小时** |

---

## 📊 QoS 性能对比

| QoS | 延迟 | 吞吐量 | 可靠性 | 幂等性 |
|-----|------|--------|--------|--------|
| AtMostOnce | <1ms | 最高 | 最低 | ❌ |
| AtLeastOnce | ~5ms | 中等 | 高 | ❌ |
| ExactlyOnce | ~10ms | 最低 | 最高 | ✅ |

---

## 🎯 用户使用示例

### 示例 1: 日志（QoS 0）

```csharp
public record LogMessage(string Message) : IEvent
{
    public override QualityOfService QoS => QualityOfService.AtMostOnce;
}

// 使用
await _mediator.PublishAsync(new LogMessage("User logged in"));
// ✅ 发送即忘，最快
```

### 示例 2: 订单通知（QoS 1）

```csharp
public record OrderCreatedEvent(string OrderId) : IEvent
{
    public override QualityOfService QoS => QualityOfService.AtLeastOnce;
}

// 使用
await _mediator.PublishAsync(new OrderCreatedEvent("order-123"));
// ✅ 保证送达，可能重复
```

### 示例 3: 支付（QoS 2）

```csharp
public record ProcessPaymentCommand(string PaymentId, decimal Amount) : IRequest<PaymentResponse>
{
    public override QualityOfService QoS => QualityOfService.ExactlyOnce;
}

// 使用
var result = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResponse>(
    new ProcessPaymentCommand("pay-456", 100m));
// ✅ 保证送达且不重复，最安全
```

---

## ✅ 验收标准

1. ✅ 所有 3 种 QoS 在 InMemory、Redis、NATS 中都正确实现
2. ✅ QoS 2 真正保证不重复（幂等性检查）
3. ✅ 单元测试覆盖率 > 90%
4. ✅ 性能测试验证（QoS 0 最快，QoS 2 最慢但最可靠）
5. ✅ 文档更新（README 增加 QoS 说明）

---

## 🚀 开始执行！

