# 🎬 事件溯源（Event Sourcing）

Catga 提供完整的事件溯源支持，包括事件存储、快照、投影等功能。

---

## 📋 核心概念

### 什么是事件溯源？

事件溯源是一种数据持久化模式，通过存储状态变化的事件序列，而不是直接存储当前状态。

**传统方式**:
```
CREATE: Order { Id: 1, Status: "Created", Total: 0 }
UPDATE: Order SET Status = "Confirmed", Total = 100
UPDATE: Order SET Status = "Shipped"
```

**事件溯源**:
```
Event 1: OrderCreated { OrderId: 1, CustomerId: "C1" }
Event 2: ItemAdded { OrderId: 1, Product: "A", Quantity: 2, Price: 50 }
Event 3: OrderConfirmed { OrderId: 1 }
Event 4: OrderShipped { OrderId: 1, TrackingNumber: "TRACK-123" }
```

---

## 🎯 核心组件

### 1. IEventStore - 事件存储

```csharp
public interface IEventStore
{
    // 追加事件
    Task AppendToStreamAsync(string streamId, IEnumerable<IEvent> events);

    // 读取事件流
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId);

    // 读取所有事件
    IAsyncEnumerable<StoredEvent> ReadAllAsync(long fromPosition = 0);

    // 快照
    Task SaveSnapshotAsync<TSnapshot>(string streamId, long version, TSnapshot snapshot);
    Task<(TSnapshot? Snapshot, long Version)> LoadSnapshotAsync<TSnapshot>(string streamId);
}
```

### 2. AggregateRoot - 聚合根

```csharp
public abstract class AggregateRoot
{
    public string Id { get; protected set; }
    public long Version { get; protected set; }

    // 应用事件（子类实现）
    protected abstract void Apply(IEvent @event);

    // 触发新事件
    protected void RaiseEvent(IEvent @event);

    // 从历史重建
    public void LoadFromHistory(IEnumerable<IEvent> events);
}
```

### 3. IProjection - 投影（读模型）

```csharp
public interface IProjection
{
    string ProjectionName { get; }
    Task HandleAsync(StoredEvent storedEvent, CancellationToken cancellationToken);
}
```

---

## 🚀 快速开始

### 1. 配置事件溯源

```csharp
services.AddEventSourcing();  // 使用内存存储

// 或添加投影
services.AddEventSourcing();
services.AddProjection<OrderSummaryProjection>();
```

### 2. 定义聚合根

```csharp
public class Order : AggregateRoot
{
    public string CustomerId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public List<OrderItem> Items { get; private set; } = new();

    // 创建订单
    public void Create(string orderId, string customerId)
    {
        RaiseEvent(new OrderCreatedEvent(orderId, customerId, DateTime.UtcNow));
    }

    // 添加商品
    public void AddItem(string productId, int quantity, decimal price)
    {
        RaiseEvent(new OrderItemAddedEvent(Id, productId, quantity, price));
    }

    // 确认订单
    public void Confirm()
    {
        if (Status != OrderStatus.Created)
            throw new InvalidOperationException("Can only confirm created orders");

        RaiseEvent(new OrderConfirmedEvent(Id, DateTime.UtcNow));
    }

    // 应用事件（重建状态）
    protected override void Apply(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreatedEvent e:
                Id = e.OrderId;
                CustomerId = e.CustomerId;
                Status = OrderStatus.Created;
                break;

            case OrderItemAddedEvent e:
                Items.Add(new OrderItem(e.ProductId, e.Quantity, e.Price));
                break;

            case OrderConfirmedEvent:
                Status = OrderStatus.Confirmed;
                break;
        }
    }
}
```

### 3. 使用聚合根

```csharp
// 创建新订单
var order = new Order();
order.Create("order-001", "customer-123");
order.AddItem("product-A", 2, 29.99m);
order.AddItem("product-B", 1, 49.99m);
order.Confirm();

// 保存事件
await eventStore.AppendToStreamAsync($"order-{order.Id}", order.UncommittedEvents);
order.MarkEventsAsCommitted();

// 重建订单
var events = await eventStore.ReadStreamAsync("order-order-001");
var rebuiltOrder = new Order();
rebuiltOrder.LoadFromHistory(events.Select(e => DeserializeEvent(e)));

Console.WriteLine($"订单状态: {rebuiltOrder.Status}");
Console.WriteLine($"总金额: ${rebuiltOrder.Total}");
```

---

## 📸 快照机制

快照用于优化性能，避免每次都重放所有事件。

```csharp
// 保存快照
var snapshot = new OrderSnapshot
{
    Id = order.Id,
    CustomerId = order.CustomerId,
    Status = order.Status,
    Items = order.Items.ToList(),
    Total = order.Total
};

await eventStore.SaveSnapshotAsync($"order-{order.Id}", order.Version, snapshot);

// 加载快照
var (loadedSnapshot, version) = await eventStore.LoadSnapshotAsync<OrderSnapshot>($"order-{order.Id}");

if (loadedSnapshot != null)
{
    // 从快照重建
    var order = new Order();
    order.LoadFromSnapshot(loadedSnapshot, version);

    // 只重放快照之后的事件
    var events = await eventStore.ReadStreamAsync($"order-{order.Id}", fromVersion: version + 1);
    order.LoadFromHistory(events.Select(e => DeserializeEvent(e)));
}
```

**快照策略**:
- 每 N 个事件保存一次快照（例如：100）
- 定时保存快照
- 手动触发快照

---

## 🎯 投影（读模型）

投影用于构建优化的读模型。

```csharp
public class OrderSummaryProjection : IProjection
{
    private readonly IDatabase _database;

    public string ProjectionName => "OrderSummary";

    public async Task HandleAsync(StoredEvent storedEvent, CancellationToken cancellationToken)
    {
        switch (storedEvent.EventType)
        {
            case var t when t.Contains("OrderCreatedEvent"):
                var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(storedEvent.EventData);
                await _database.InsertAsync(new OrderSummary
                {
                    OrderId = evt.OrderId,
                    CustomerId = evt.CustomerId,
                    CreatedAt = evt.CreatedAt,
                    Status = "Created"
                });
                break;

            case var t when t.Contains("OrderConfirmedEvent"):
                var confirmEvt = JsonSerializer.Deserialize<OrderConfirmedEvent>(storedEvent.EventData);
                await _database.UpdateAsync(confirmEvt.OrderId, new { Status = "Confirmed" });
                break;
        }
    }
}

// 注册投影
services.AddProjection<OrderSummaryProjection>();
```

**投影特点**:
- 异步处理事件
- 构建优化的读模型
- 支持多个投影
- 可以重建投影

**重建投影**:
```csharp
await projectionManager.RebuildProjectionAsync("OrderSummary");
```

---

## 📊 实际应用场景

### 1. 审计日志

```csharp
// 所有状态变化都有完整记录
var events = await eventStore.ReadStreamAsync("order-123");
foreach (var evt in events)
{
    Console.WriteLine($"{evt.Timestamp}: {evt.EventType}");
    // 2025-01-01 10:00: OrderCreated
    // 2025-01-01 10:05: ItemAdded
    // 2025-01-01 10:10: OrderConfirmed
}
```

### 2. 时间旅行

```csharp
// 查看订单在某个时间点的状态
var eventsUntil = await eventStore.ReadStreamAsync("order-123", maxCount: 2);
var order = new Order();
order.LoadFromHistory(eventsUntil);
// 订单只加载了前 2 个事件的状态
```

### 3. 事件回溯分析

```csharp
// 分析所有取消的订单
await foreach (var evt in eventStore.ReadAllAsync())
{
    if (evt.EventType.Contains("OrderCancelled"))
    {
        var cancelEvent = JsonSerializer.Deserialize<OrderCancelledEvent>(evt.EventData);
        Console.WriteLine($"订单 {cancelEvent.OrderId} 被取消: {cancelEvent.Reason}");
    }
}
```

### 4. 多个读模型

```csharp
// 订单总览投影（用于列表）
services.AddProjection<OrderSummaryProjection>();

// 订单详情投影（用于详情页）
services.AddProjection<OrderDetailsProjection>();

// 客户订单历史投影（用于客户中心）
services.AddProjection<CustomerOrderHistoryProjection>();

// 所有投影独立更新，互不影响
```

---

## 💡 最佳实践

### 1. 事件设计

```csharp
// ✅ 好的事件设计
public record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    DateTime CreatedAt) : IEvent;

// ❌ 不好的事件设计
public record OrderUpdatedEvent(Order Order) : IEvent;  // 包含整个实体
```

**原则**:
- 事件不可变
- 事件表达业务意图
- 事件粒度适中
- 事件包含必要信息

### 2. 聚合边界

```csharp
// ✅ 好的聚合边界
public class Order : AggregateRoot  // 订单是一个聚合
{
    public List<OrderItem> Items { get; }  // 订单项是值对象
}

// ❌ 不好的聚合边界
public class Order : AggregateRoot
{
    public Customer Customer { get; }  // 客户应该是独立的聚合
}
```

### 3. 快照策略

```csharp
// 每 100 个事件保存一次快照
if (order.Version % 100 == 0)
{
    await eventStore.SaveSnapshotAsync($"order-{order.Id}", order.Version, CreateSnapshot(order));
}
```

### 4. 事件版本管理

```csharp
// 使用版本号处理事件演化
public record OrderCreatedEventV1(string OrderId, string CustomerId) : IEvent;

public record OrderCreatedEventV2(
    string OrderId,
    string CustomerId,
    string Email) : IEvent;  // 新版本增加了 Email

// 在 Apply 中处理两个版本
protected override void Apply(IEvent @event)
{
    switch (@event)
    {
        case OrderCreatedEventV2 e:
            // 处理 V2
            break;
        case OrderCreatedEventV1 e:
            // 处理 V1（向后兼容）
            break;
    }
}
```

---

## 🎊 总结

**事件溯源的优势**:
- ✅ 完整的审计日志
- ✅ 时间旅行能力
- ✅ 事件回溯分析
- ✅ 多个读模型
- ✅ 业务语言清晰

**事件溯源的挑战**:
- ⚠️ 学习曲线
- ⚠️ 事件版本管理
- ⚠️ 查询性能（需要投影）

**适用场景**:
- 需要完整审计的系统
- 需要复杂业务分析的系统
- 需要多个读模型的系统
- 金融、医疗等高合规要求的系统

**Catga 事件溯源特点**:
- ✅ 简洁的 API
- ✅ 完整的快照支持
- ✅ 灵活的投影机制
- ✅ 内存实现（测试友好）
- ✅ 易于扩展到持久化存储

