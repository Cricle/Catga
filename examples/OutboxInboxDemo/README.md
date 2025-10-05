# Outbox & Inbox 模式演示

这个示例展示了如何使用 Catga 的 Outbox 和 Inbox 模式来实现可靠的分布式消息传递。

## 🎯 演示内容

1. **Outbox 模式**: 确保业务事务和消息发送的原子性
2. **Inbox 模式**: 确保消息处理的幂等性（防止重复处理）
3. **完整流程**: 创建订单 → 保存到 Outbox → 后台发送 → Inbox 幂等处理

## 🚀 运行示例

```bash
cd examples/OutboxInboxDemo
dotnet run
```

## 📊 预期输出

```
=== Outbox & Inbox 模式演示开始 ===

📦 演示 1: 创建订单（使用 Outbox 模式）
✅ 订单创建成功: abcd1234
📤 Outbox Publisher 发送消息...
📧 发送邮件通知...
📦 更新库存...

📦 演示 2: 创建第二个订单
✅ 订单创建成功: efgh5678
...

📥 演示 3: 测试 Inbox 幂等性
📤 第一次发送事件 (MessageId: xxx)
   ✅ 处理器执行
📤 第二次发送相同事件 (MessageId: xxx)
   ⏭️  已处理，跳过（Inbox）

💡 注意: 即使发送了两次，处理器应该只执行一次！

=== 演示完成 ===
```

## 🔍 关键特性

### Outbox 模式

```csharp
// 1. 业务逻辑 + 事件发布在同一个"逻辑事务"中
var orderId = CreateOrder(...);
await _mediator.PublishAsync(new OrderCreatedEvent { OrderId = orderId });

// 2. OutboxBehavior 自动将事件保存到 Outbox
// 3. OutboxPublisher 后台服务轮询并发送
```

### Inbox 模式

```csharp
// 1. 事件必须有 MessageId
public record OrderCreatedEvent : IEvent, MessageBase
{
    public MessageId MessageId { get; init; } = MessageId.Generate();
}

// 2. InboxBehavior 自动检查消息是否已处理
// 3. 如果已处理，返回缓存结果（跳过业务逻辑）
```

## 📚 相关文档

- [Outbox & Inbox 模式详解](/docs/patterns/outbox-inbox.md)
- [分布式消息可靠性](/docs/reliability.md)
- [生产环境部署](/docs/deployment.md)

## 🔄 切换到 Redis 版本

要在生产环境中使用，将内存版本替换为 Redis 版本：

```csharp
// 替换
services.AddOutbox();
services.AddInbox();

// 为
services.AddRedisOutbox(options =>
{
    options.ConnectionString = "localhost:6379";
});
services.AddRedisInbox(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

## ⚙️ 配置选项

### Outbox 配置

```csharp
services.AddOutbox(options =>
{
    options.EnablePublisher = true;                    // 启用后台发布器
    options.PollingInterval = TimeSpan.FromSeconds(5); // 轮询间隔
    options.BatchSize = 100;                           // 批次大小
    options.RetentionPeriod = TimeSpan.FromHours(24);  // 保留时间
});
```

### Inbox 配置

```csharp
services.AddInbox(options =>
{
    options.LockDuration = TimeSpan.FromMinutes(5);    // 锁定时长
    options.RetentionPeriod = TimeSpan.FromHours(24);  // 保留时间
});
```

---

**可靠的分布式消息传递，从 Outbox + Inbox 开始！** 🚀

