# Outbox & Inbox 模式

## 📋 概述

Outbox 和 Inbox 模式是确保分布式系统消息可靠性的两个关键模式，Catga 框架提供了完整的实现。

### 🎯 核心问题

在分布式系统中，我们需要解决两个关键问题：

1. **Outbox 模式**: 如何保证业务事务和消息发送的原子性？
2. **Inbox 模式**: 如何保证消息处理的幂等性（至少一次 → 恰好一次）？

---

## 🔄 Outbox 模式

### 什么是 Outbox 模式？

Outbox 模式确保**业务事务**和**消息发送**的原子性，避免以下问题：

- ❌ 事务提交成功，但消息发送失败
- ❌ 消息发送成功，但事务回滚
- ✅ 两者要么同时成功，要么同时失败

### 工作原理

```
┌─────────────────────────────────────────────────────┐
│          业务事务 (Database Transaction)            │
│                                                      │
│  1. 更新业务数据 (e.g., 创建订单)                   │
│  2. 插入消息到 Outbox 表                            │
│                                                      │
│  ✅ 提交事务（原子操作）                             │
└─────────────────────────────────────────────────────┘
                       ↓
┌─────────────────────────────────────────────────────┐
│      Outbox Publisher (后台服务)                     │
│                                                      │
│  3. 轮询 Outbox 表获取待发送消息                     │
│  4. 发送消息到消息队列 (NATS/Kafka/RabbitMQ)         │
│  5. 标记消息为已发送                                 │
└─────────────────────────────────────────────────────┘
```

### 使用方式

#### 1. 内存版本（开发/测试）

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 核心
builder.Services.AddCatga();

// 添加 Outbox 模式（内存版本）
builder.Services.AddOutbox(options =>
{
    options.EnablePublisher = true;           // 启用后台发布器
    options.PollingInterval = TimeSpan.FromSeconds(5);  // 轮询间隔
    options.BatchSize = 100;                  // 每批处理消息数
});

var app = builder.Build();
app.Run();
```

#### 2. Redis 版本（生产环境）

```csharp
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 核心
builder.Services.AddCatga();

// 添加 Redis Outbox（生产环境推荐）
builder.Services.AddRedisOutbox(options =>
{
    options.ConnectionString = "localhost:6379";
    options.OutboxPollingInterval = TimeSpan.FromSeconds(5);
    options.OutboxBatchSize = 100;
});

var app = builder.Build();
app.Run();
```

### 业务代码示例

```csharp
public record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent, MessageBase;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly IOrderRepository _orderRepo;
    private readonly ICatgaMediator _mediator;

    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 在同一个事务中执行
        using var transaction = await _orderRepo.BeginTransactionAsync();

        try
        {
            // 1. 业务逻辑 - 创建订单
            var order = new Order
            {
                CustomerId = request.CustomerId,
                Amount = request.Amount
            };
            await _orderRepo.AddAsync(order);

            // 2. 发布事件（自动保存到 Outbox）
            await _mediator.PublishAsync(new OrderCreatedEvent
            {
                OrderId = order.Id,
                Amount = order.Amount,
                MessageId = MessageId.Generate(),
                CorrelationId = CorrelationId.Generate()
            });

            // 3. 提交事务（订单和 Outbox 消息原子提交）
            await transaction.CommitAsync();

            return CatgaResult<OrderResult>.Success(new OrderResult { OrderId = order.Id });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

### Outbox 表结构（概念）

```sql
CREATE TABLE Outbox (
    MessageId VARCHAR(50) PRIMARY KEY,
    MessageType VARCHAR(255),
    Payload TEXT,
    CreatedAt DATETIME,
    PublishedAt DATETIME NULL,
    Status INT,          -- 0=Pending, 1=Published, 2=Failed
    RetryCount INT,
    MaxRetries INT,
    LastError TEXT NULL,
    CorrelationId VARCHAR(50) NULL
);

-- 索引优化
CREATE INDEX IX_Outbox_Status_CreatedAt ON Outbox(Status, CreatedAt);
```

---

## 📥 Inbox 模式

### 什么是 Inbox 模式？

Inbox 模式确保**消息处理的幂等性**，实现"恰好一次"语义：

- ❌ 同一消息被重复处理多次
- ✅ 无论收到多少次，只处理一次

### 工作原理

```
┌─────────────────────────────────────────────────────┐
│          收到消息 (e.g., OrderCreatedEvent)          │
└─────────────────────────────────────────────────────┘
                       ↓
┌─────────────────────────────────────────────────────┐
│             Inbox 幂等性检查                         │
│                                                      │
│  1. 检查 MessageId 是否已处理                        │
│     ├─ 已处理 → 返回缓存结果（跳过处理）            │
│     └─ 未处理 → 继续处理                            │
│                                                      │
│  2. 获取分布式锁（防止并发处理）                     │
│  3. 执行业务逻辑                                     │
│  4. 保存处理结果到 Inbox                            │
│  5. 释放锁                                          │
└─────────────────────────────────────────────────────┘
```

### 使用方式

#### 1. 内存版本（开发/测试）

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 核心
builder.Services.AddCatga();

// 添加 Inbox 模式（内存版本）
builder.Services.AddInbox(options =>
{
    options.LockDuration = TimeSpan.FromMinutes(5);   // 锁定时长
    options.RetentionPeriod = TimeSpan.FromHours(24); // 消息保留时间
});

var app = builder.Build();
app.Run();
```

#### 2. Redis 版本（生产环境）

```csharp
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 核心
builder.Services.AddCatga();

// 添加 Redis Inbox（生产环境推荐）
builder.Services.AddRedisInbox(options =>
{
    options.ConnectionString = "localhost:6379";
    options.InboxRetentionPeriod = TimeSpan.FromHours(24);
});

var app = builder.Build();
app.Run();
```

### 业务代码示例

```csharp
public record OrderCreatedEvent : IEvent, MessageBase
{
    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
    public MessageId MessageId { get; init; }  // 必须！
    public CorrelationId CorrelationId { get; init; }
}

public class SendOrderEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Inbox Behavior 会自动处理幂等性
        // 即使这个事件收到多次，邮件只会发送一次

        await _emailService.SendOrderConfirmationAsync(@event.OrderId);

        // 结果会自动缓存到 Inbox
    }
}
```

### Inbox 表结构（概念）

```sql
CREATE TABLE Inbox (
    MessageId VARCHAR(50) PRIMARY KEY,
    MessageType VARCHAR(255),
    Payload TEXT,
    ReceivedAt DATETIME,
    ProcessedAt DATETIME NULL,
    ProcessingResult TEXT NULL,
    Status INT,          -- 0=Pending, 1=Processing, 2=Processed
    LockExpiresAt DATETIME NULL,
    CorrelationId VARCHAR(50) NULL
);

-- 索引优化
CREATE INDEX IX_Inbox_Status ON Inbox(Status);
CREATE INDEX IX_Inbox_LockExpiresAt ON Inbox(LockExpiresAt) WHERE Status = 1;
```

---

## 🔗 组合使用

在实际生产环境中，通常**同时使用** Outbox 和 Inbox 模式：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 核心
builder.Services.AddCatga();

// 添加 Redis Outbox + Inbox（完整可靠性）
builder.Services.AddRedisOutbox(options =>
{
    options.ConnectionString = "localhost:6379";
    options.OutboxPollingInterval = TimeSpan.FromSeconds(5);
});

builder.Services.AddRedisInbox(options =>
{
    options.ConnectionString = "localhost:6379";
});

var app = builder.Build();
app.Run();
```

### 完整流程

```
┌────────────┐      Outbox       ┌──────────┐      Inbox      ┌────────────┐
│  Service A │ ─────────────────> │   NATS   │ ───────────────> │  Service B │
│            │                    │          │                  │            │
│ 1. 业务事务│                    │ 2. 可靠传输│                  │ 3. 幂等处理│
│ 2. 保存消息│                    │          │                  │ 4. 防重复  │
│ 3. 提交    │                    │          │                  │            │
└────────────┘                    └──────────┘                  └────────────┘

✅ 消息不会丢失（Outbox）
✅ 消息不会重复处理（Inbox）
✅ 恰好一次语义（Exactly-Once）
```

---

## 📊 性能考虑

### Outbox Publisher 调优

```csharp
builder.Services.AddRedisOutbox(options =>
{
    // 高吞吐场景
    options.OutboxPollingInterval = TimeSpan.FromSeconds(1);  // 更频繁
    options.OutboxBatchSize = 500;                            // 更大批次

    // 低延迟场景
    options.OutboxPollingInterval = TimeSpan.FromMilliseconds(500);
    options.OutboxBatchSize = 50;
});
```

### Inbox 锁定时长

```csharp
builder.Services.AddRedisInbox(options =>
{
    // 快速处理的消息
    options.LockDuration = TimeSpan.FromMinutes(1);

    // 长时间处理的消息（如文件处理）
    options.LockDuration = TimeSpan.FromMinutes(15);
});
```

---

## 🧹 清理策略

### 自动清理

Catga 会自动清理旧消息：

```csharp
// Outbox: 已发布的消息保留 24 小时后自动删除
options.OutboxRetentionPeriod = TimeSpan.FromHours(24);

// Inbox: 已处理的消息保留 24 小时后自动删除
options.InboxRetentionPeriod = TimeSpan.FromHours(24);
```

### Redis TTL

Redis 版本使用 TTL 自动过期：

- Outbox 已发布消息：24 小时 TTL
- Inbox 已处理消息：24 小时 TTL
- Inbox 锁：根据 `LockDuration` 设置

---

## ⚠️ 注意事项

### 1. MessageId 是必须的

```csharp
// ❌ 错误：没有 MessageId
public record MyEvent(string Data) : IEvent;

// ✅ 正确：实现 MessageBase 或提供 MessageId
public record MyEvent : IEvent, MessageBase
{
    public string Data { get; init; }
    public MessageId MessageId { get; init; } = MessageId.Generate();
}
```

### 2. Outbox 需要事务支持

Outbox 模式最有效时，应该在**同一个数据库事务**中：

```csharp
// ✅ 理想情况：使用支持事务的存储（PostgreSQL + Outbox 表）
using var transaction = await _dbContext.Database.BeginTransactionAsync();

// 业务操作
_dbContext.Orders.Add(order);

// Outbox 消息
_dbContext.OutboxMessages.Add(outboxMessage);

// 原子提交
await _dbContext.SaveChangesAsync();
await transaction.CommitAsync();
```

### 3. 监控和告警

监控 Outbox/Inbox 的健康状况：

```csharp
// 监控 Outbox 积压
var pendingCount = await outboxStore.GetPendingMessagesAsync(maxCount: 1);
if (pendingCount > 1000)
{
    _logger.LogWarning("Outbox backlog too high: {Count}", pendingCount);
}

// 监控 Inbox 失败
var failedCount = await inboxStore.GetFailedCountAsync();
if (failedCount > 100)
{
    _logger.LogError("Too many failed inbox messages: {Count}", failedCount);
}
```

---

## 📚 最佳实践

1. **生产环境使用 Redis 版本**
   - 内存版本仅用于开发/测试
   - Redis 提供持久化和分布式锁

2. **合理设置批次大小**
   - 根据消息大小和处理速度调整
   - 监控 Outbox 积压情况

3. **设置告警**
   - Outbox 消息积压过多
   - Inbox 锁过期过多
   - 发送失败率过高

4. **定期清理**
   - 虽然有自动清理，但建议定期检查
   - 防止存储无限增长

5. **消息幂等性设计**
   - 即使有 Inbox，业务逻辑本身也应支持幂等
   - 双重保险

---

## 🔗 相关文档

- [分布式事务 (Saga)](/docs/patterns/saga.md)
- [NATS 分布式传输](/docs/transports/nats.md)
- [Redis 持久化](/docs/storage/redis.md)
- [消息可靠性保证](/docs/reliability.md)

---

**Outbox + Inbox = 可靠的分布式消息传递** 🚀

