# Catga.Persistence.Nats

## 概述

基于 NATS JetStream 的持久化存储实现，提供事件溯源、Outbox 模式和 Inbox 模式的完整支持。

## ✅ 状态：生产就绪

此项目已完成开发并可用于生产环境。所有组件均基于 NATS JetStream API 实现。

## 功能特性

### 📦 组件

| 组件 | 实现 | 说明 |
|------|------|------|
| **EventStore** | `NatsJSEventStore` | 基于 JetStream Streams 的事件存储 |
| **OutboxStore** | `NatsJSOutboxStore` | 基于 JetStream 的 Outbox 模式 |
| **InboxStore** | `NatsJSInboxStore` | 基于 JetStream 的 Inbox 模式 |

### 🚀 核心优势

- ✅ **持久化存储** - 使用 JetStream File Storage
- ✅ **高可用** - NATS 集群支持
- ✅ **乐观并发控制** - EventStore 支持版本检查
- ✅ **自动过期** - 支持 TTL 配置
- ✅ **分布式** - 天然支持分布式场景
- ✅ **AOT 兼容** - 仅有 JSON 序列化警告（可忽略）

## 安装

```xml
<PackageReference Include="Catga.Persistence.Nats" Version="x.x.x" />
```

## 使用方法

### 1. 注册 NATS 连接

```csharp
using NATS.Client.Core;

services.AddSingleton<INatsConnection>(sp =>
{
    var options = NatsOpts.Default with
    {
        Url = "nats://localhost:4222"
    };
    return new NatsConnection(options);
});
```

### 2. 注册 NATS Persistence

```csharp
using Catga;

// 方式1: 使用默认配置
services.AddNatsPersistence();

// 方式2: 自定义 Stream 名称
services.AddNatsPersistence(options =>
{
    options.EventStreamName = "MY_EVENTS";
    options.OutboxStreamName = "MY_OUTBOX";
    options.InboxStreamName = "MY_INBOX";
});

// 方式3: 单独注册
services.AddNatsEventStore("MY_EVENTS");
services.AddNatsOutboxStore("MY_OUTBOX");
services.AddNatsInboxStore("MY_INBOX");
```

### 3. 使用示例

#### Event Sourcing

```csharp
public class OrderAggregate
{
    private readonly IEventStore _eventStore;

    public OrderAggregate(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task CreateOrder(string orderId, decimal amount)
    {
        var events = new List<IEvent>
        {
            new OrderCreatedEvent { OrderId = orderId, Amount = amount }
        };

        await _eventStore.AppendAsync(orderId, events);
    }

    public async Task<EventStream> GetOrderHistory(string orderId)
    {
        return await _eventStore.ReadAsync(orderId);
    }
}
```

## 架构设计

### JetStream Streams 配置

| Stream | Subjects | Retention | TTL |
|--------|----------|-----------|-----|
| **CATGA_EVENTS** | `CATGA_EVENTS.>` | Limits | 365 天 |
| **CATGA_OUTBOX** | `CATGA_OUTBOX.>` | Limits | 自动清理 |
| **CATGA_INBOX** | `CATGA_INBOX.>` | Limits | 7 天 |

### 序列化

- 使用 `System.Text.Json` 进行 JSON 序列化
- EventStore 使用 Type Envelope 包装以支持多态事件
- OutboxStore/InboxStore 直接序列化消息对象

### 并发控制

EventStore 支持乐观并发控制：

```csharp
// 预期版本为 5，如果不匹配则抛出 ConcurrencyException
await _eventStore.AppendAsync(streamId, events, expectedVersion: 5);
```

## 性能优化

1. **批量操作** - 通过 `AppendAsync` 批量追加事件
2. **临时 Consumer** - 读取操作使用临时 Consumer，自动清理
3. **Filter Subject** - 使用精确的 Subject 过滤减少网络传输
4. **异步迭代** - 使用 `IAsyncEnumerable` 流式处理大量数据

## 依赖项

- `NATS.Client.Core` - NATS .NET v2 核心客户端
- `NATS.Client.JetStream` - JetStream API 支持
- `System.Text.Json` - JSON 序列化

## 注意事项

### AOT 警告

项目编译时会产生 `IL2026` 和 `IL3050` 警告，这些是 `System.Text.Json` 反射序列化的警告。在非 AOT 场景下可以安全忽略。如需 AOT 支持，可以使用 Source Generator。

### NATS 服务器要求

- 需要启用 JetStream 功能
- 推荐 NATS Server 2.10+ 版本

### 临时 Consumer

EventStore 的读取操作会创建临时 Consumer（使用 GUID 命名），这些 Consumer 在连接断开后会自动清理。如果频繁读取，可以考虑使用持久 Consumer 优化。

## 与其他实现对比

| 特性 | InMemory | Redis | NATS |
|------|----------|-------|------|
| 持久化 | ❌ | ✅ | ✅ |
| 分布式 | ❌ | ✅ | ✅ |
| 高可用 | ❌ | ✅ Sentinel/Cluster | ✅ Cluster |
| 事件溯源 | ✅ | ✅ | ✅ |
| 消息传输 | ✅ | ✅ | ✅ |
| 性能 | 🏆 最快 | ⚡ 快 | ⚡ 快 |
| 场景 | 开发/测试 | 生产 | 生产 |

## 路线图

- [ ] 支持 NATS Source Generator 以消除 AOT 警告
- [ ] 添加快照功能以优化 EventStore 性能
- [ ] 支持持久 Consumer 配置
- [ ] 添加监控指标（Metrics）

## 许可证

MIT License
