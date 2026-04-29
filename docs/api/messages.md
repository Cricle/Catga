# 消息类型

这一页只回答一个问题：Catga 当前的消息契约长什么样，以及业务代码应该优先怎么写。

## 当前主契约

Catga 的核心消息接口包括：

- `IMessage`
- `IRequest<TResponse>`
- `IRequest`
- `IEvent`
- `IReliableEvent`

命名空间主要是：

```csharp
Catga.Abstractions
```

## IMessage

`IMessage` 是所有消息的底层契约。

```csharp
public interface IMessage
{
    long MessageId { get; }
    DateTime CreatedAt { get; }
    long? CorrelationId { get; }
    QualityOfService QoS { get; }
    DeliveryMode DeliveryMode { get; }
}
```

关键点：

- `MessageId` 现在是 `long`
- `CorrelationId` 也是 `long?`
- QoS 和投递模式属于消息契约的一部分

## IRequest<TResponse> / IRequest

请求分成两类：

- 有响应：`IRequest<TResponse>`
- 无响应：`IRequest`

```csharp
public interface IRequest<TResponse> : IMessage
{
}

public interface IRequest : IMessage
{
}
```

常见理解：

- command 常常用 `IRequest` 或 `IRequest<TResponse>`
- query 常常用 `IRequest<TResponse>`

## IEvent / IReliableEvent

事件也分两类：

- 普通事件：`IEvent`
- 至少一次投递语义的可靠事件：`IReliableEvent`

```csharp
public interface IEvent : IMessage
{
    DateTime OccurredAt { get; }
}

public interface IReliableEvent : IEvent
{
}
```

默认语义上：

- `IEvent` 更偏 `AtMostOnce`
- `IReliableEvent` 更偏 `AtLeastOnce`

## 当前推荐写法

### 方式 1：直接实现接口

```csharp
using Catga.Abstractions;

public sealed record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;

public sealed record GetOrder(string OrderId)
    : IRequest<OrderDto>;

public sealed record OrderCreated(string OrderId, decimal Amount)
    : IEvent;
```

这是最直接的写法，适合简单消息。

### 方式 2：使用基础基类

当前也提供了几类基础 record：

- `CommandBase`
- `QueryBase<TResponse>`
- `EventBase`
- `ReliableEventBase`

例如：

```csharp
using Catga.Abstractions;

public sealed record CreateOrder(string OrderId, decimal Amount) : CommandBase;

public sealed record GetOrder(string OrderId) : QueryBase<OrderDto>;

public sealed record OrderCreated(string OrderId, decimal Amount) : EventBase;
```

如果你希望显式持有 `MessageId` / `CorrelationId` 等基础字段，这类基类更方便。

## 命名建议

### Command

- 用动词开头
- 明确表达一次业务动作

示例：

- `CreateOrder`
- `CancelOrder`
- `ReserveInventory`

### Query

- 用 `Get` / `Find` / `Search` 等前缀
- 名字里体现返回视角

示例：

- `GetOrder`
- `FindOrdersByCustomer`

### Event

- 用过去式
- 表示“已经发生”

示例：

- `OrderCreated`
- `PaymentSucceeded`
- `InventoryReserved`

## 最佳实践

### 1. 保持消息简单

消息应尽量只承载数据，不要塞业务逻辑。

```csharp
public sealed record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;
```

### 2. 优先使用 record

`record` 更适合消息语义：

- 值对象风格更自然
- 更适合不可变建模
- 序列化和对比都更清晰

### 3. 需要 AOT 时优先考虑 MemoryPack

如果你走 MemoryPack 路线，消息通常还要标注：

```csharp
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;
```

### 4. 需要自定义元数据时再选基类

如果只想表达业务字段，直接实现接口通常更轻。
如果需要显式承载 `MessageId` / `CorrelationId`，再考虑 `CommandBase` / `EventBase`。

## AOT 相关

消息类型本身可以设计成 AOT 友好，但最终是否顺利发布 Native AOT，还取决于：

- serializer 选择
- transport / persistence 组合
- 是否使用源生成配置

如果你走 JSON 路线，通常要配 `JsonSerializerContext`：

```csharp
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(OrderCreated))]
public partial class MyJsonContext : JsonSerializerContext
{
}
```

## 相关文档

- [Mediator API](./mediator.md)
- [序列化指南](../guides/serialization.md)
- [AOT 序列化指南](../aot/serialization-aot-guide.md)
- [架构索引](../architecture/README.md)
