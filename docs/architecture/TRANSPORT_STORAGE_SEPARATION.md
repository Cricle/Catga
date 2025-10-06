# 传输与存储分离架构

## 🎯 设计原则

参考 **MassTransit** 的架构设计，我们将消息系统的两大关注点彻底分离：

1. **传输层 (Transport Layer)** - 负责消息的发送和接收
2. **存储层 (Persistence Layer)** - 负责 Outbox/Inbox 模式的持久化

### 为什么分离？

**单一职责原则 (SRP)**：
- 传输层关注 **如何传递消息**（NATS, Redis Pub/Sub, RabbitMQ）
- 存储层关注 **如何存储消息**（Redis, SQL, MongoDB）

**灵活组合**：
- ✅ 使用 NATS 传输 + Redis 存储
- ✅ 使用 Redis 传输 + SQL 存储
- ✅ 使用 RabbitMQ 传输 + MongoDB 存储
- ✅ 传输和存储可以独立演进、独立扩展

---

## 📦 核心接口

### 1. 传输层接口

```csharp
public interface IMessageTransport
{
    // 发布消息（广播）
    Task PublishAsync<TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    // 发送消息（点对点）
    Task SendAsync<TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    // 订阅消息
    Task SubscribeAsync<TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    // 传输层名称
    string Name { get; }
}
```

### 2. 存储层接口

#### Outbox 存储

```csharp
public interface IOutboxStore
{
    // 添加消息到 Outbox
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    // 获取待发布的消息
    Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default);

    // 标记为已发布
    Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default);

    // 标记为失败（重试）
    Task MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    // 清理已发布消息
    Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);
}
```

#### Inbox 存储

```csharp
public interface IInboxStore
{
    // 尝试锁定消息（幂等性检查）
    Task<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    // 标记为已处理
    Task MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default);

    // 检查是否已处理
    Task<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    // 获取处理结果
    Task<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    // 释放锁定
    Task ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    // 清理已处理消息
    Task DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);
}
```

---

## 🏗️ 实现示例

### 传输层实现

#### 1. NATS 传输

```csharp
public class NatsMessageTransport : IMessageTransport
{
    private readonly INatsConnection _connection;
    private readonly IMessageSerializer _serializer;

    public string Name => "NATS";

    public async Task PublishAsync<TMessage>(TMessage message, ...)
    {
        var subject = GetSubject<TMessage>();
        var payload = _serializer.Serialize(message);
        await _connection.PublishAsync(subject, payload, ...);
    }

    // ... 其他方法
}
```

#### 2. Redis 传输 (Pub/Sub)

```csharp
public class RedisMessageTransport : IMessageTransport
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;

    public string Name => "Redis";

    public async Task PublishAsync<TMessage>(TMessage message, ...)
    {
        var channel = GetChannel<TMessage>();
        var payload = _serializer.Serialize(message);
        var subscriber = _redis.GetSubscriber();
        await subscriber.PublishAsync(channel, payload);
    }

    // ... 其他方法
}
```

### 存储层实现

#### 1. Redis 持久化

```csharp
// Outbox 持久化
public class RedisOutboxPersistence : IOutboxStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;

    // 使用 Redis SortedSet + Hash 实现
    // ...
}

// Inbox 持久化
public class RedisInboxPersistence : IInboxStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;

    // 使用 Redis String + TTL 实现
    // ...
}
```

#### 2. SQL 持久化（未来）

```csharp
public class SqlOutboxPersistence : IOutboxStore
{
    // 使用 SQL 数据库表实现
}

public class SqlInboxPersistence : IInboxStore
{
    // 使用 SQL 数据库表实现
}
```

---

## 🔧 使用示例

### 场景 1：NATS 传输 + Redis 存储

```csharp
services.AddCatga()
    // 传输层：NATS
    .AddNatsTransport(options =>
    {
        options.SubjectPrefix = "my-app";
    })

    // 存储层：Redis
    .AddRedisOutboxPersistence(options =>
    {
        options.KeyPrefix = "outbox";
    })
    .AddRedisInboxPersistence(options =>
    {
        options.KeyPrefix = "inbox";
    })

    // 序列化：MemoryPack
    .AddMessageSerializer<MemoryPackMessageSerializer>();
```

### 场景 2：Redis 全栈（传输 + 存储都用 Redis）

```csharp
services.AddCatga()
    // Redis 全栈
    .AddRedisFullStack(
        configureTransport: opt => opt.ChannelPrefix = "my-app",
        configureOutbox: opt => opt.KeyPrefix = "outbox",
        configureInbox: opt => opt.KeyPrefix = "inbox"
    )

    // 序列化：JSON
    .AddMessageSerializer<JsonMessageSerializer>();
```

### 场景 3：内存传输（测试环境）+ Redis 存储

```csharp
services.AddCatga()
    // 传输层：内存（仅测试用）
    .AddInMemoryTransport()

    // 存储层：Redis（生产级可靠性）
    .AddRedisOutboxPersistence()
    .AddRedisInboxPersistence();
```

---

## 🔄 Outbox 流程（传输 + 存储）

```
┌─────────────────────────────────────────────────────────────┐
│                    OutboxBehaviorV2                          │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  1️⃣  保存到 Outbox 存储 (IOutboxStore)                       │
│      ↓                                                        │
│      [Redis/SQL/MongoDB]                                      │
│                                                               │
│  2️⃣  执行业务逻辑                                             │
│      ↓                                                        │
│      [Your Handler]                                           │
│                                                               │
│  3️⃣  通过传输层发布 (IMessageTransport)                      │
│      ↓                                                        │
│      [NATS/Redis/RabbitMQ]                                    │
│                                                               │
│  4️⃣  标记为已发布 (IOutboxStore)                             │
│      ↓                                                        │
│      [Redis/SQL/MongoDB]                                      │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

**关键点**：
- **存储层** 与业务事务在同一个事务中，保证原子性
- **传输层** 负责将消息发送到消息队列
- 即使传输失败，消息也已持久化，可以重试

---

## 📊 对比：旧架构 vs 新架构

### 旧架构（混合）

```
❌ 问题：
- NatsOutboxStore：既做传输又做存储，职责混乱
- RedisOutboxStore：同样混合了两种职责
- 无法灵活组合不同的传输和存储
```

```csharp
// ❌ 旧方式：职责混合
public class NatsOutboxStore : IOutboxStore
{
    // 既要管理 NATS 连接（传输）
    // 又要管理消息存储（持久化）
    // 违反单一职责原则！
}
```

### 新架构（分离）

```
✅ 优势：
- IMessageTransport：专注传输
- IOutboxStore：专注存储
- 可以自由组合：NATS传输 + Redis存储
- 每个组件职责清晰，易于测试和维护
```

```csharp
// ✅ 新方式：职责分离
public class NatsMessageTransport : IMessageTransport
{
    // 只负责 NATS 消息传输
}

public class RedisOutboxPersistence : IOutboxStore
{
    // 只负责 Redis 持久化存储
}
```

---

## 🎁 好处总结

1. **清晰的职责分离** - 传输归传输，存储归存储
2. **灵活的组合** - 可以混搭不同的传输和存储实现
3. **易于测试** - 可以单独测试传输层或存储层
4. **易于扩展** - 添加新的传输或存储实现不影响现有代码
5. **符合 SOLID 原则** - 单一职责、开放封闭、依赖倒置

---

## 📖 参考资料

- [MassTransit Architecture](https://masstransit-project.com/architecture/interoperability.html)
- [Transactional Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [Inbox Pattern](https://microservices.io/patterns/data/inbox.html)

