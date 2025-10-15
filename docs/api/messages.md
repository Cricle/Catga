# 消息类型

Catga 中的所有消息类型定义。

## IMessage

所有消息的基接口。

### 命名空间

```csharp
Catga.Messages
```

### 接口定义

```csharp
public interface IMessage
{
    string MessageId { get; }
    string? CorrelationId { get; }
    DateTime CreatedAt { get; }
}
```

### 属性

- `MessageId` - 消息的唯一标识符
- `CorrelationId` - 关联标识符，用于追踪相关消息
- `CreatedAt` - 消息创建时间

## MessageBase

`IMessage` 的基类实现，提供默认值。

```csharp
public abstract record MessageBase : IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
```

**示例**

```csharp
public record MyCommand : MessageBase, IRequest<MyResponse>
{
    public string Data { get; init; } = string.Empty;
}
```

## IRequest&lt;TResponse&gt;

请求接口，包括命令和查询。

### 接口定义

```csharp
public interface IRequest<out TResponse> : IMessage
{
}
```

### 类型参数

- `TResponse` - 响应类型

## ICommand&lt;TResponse&gt;

命令接口，表示会改变系统状态的操作。

### 接口定义

```csharp
public interface IRequest<out TResponse> : IRequest<TResponse>
{
}
```

### 示例

```csharp
public record CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string CustomerId { get; init; } = string.Empty;
}

public record OrderResult
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

## IQuery&lt;TResponse&gt;

查询接口，表示不会改变系统状态的读取操作。

### 接口定义

```csharp
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
```

### 示例

```csharp
public record GetOrderQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

public record OrderDto
{
    public string OrderId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
}
```

## IEvent

事件接口，表示已经发生的事情。

### 接口定义

```csharp
public interface IEvent : IMessage
{
    DateTime OccurredAt { get; }
}
```

### 属性

- `OccurredAt` - 事件发生的时间

## EventBase

`IEvent` 的基类实现。

```csharp
public abstract record EventBase : MessageBase, IEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
```

### 示例

```csharp
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}

public record OrderShippedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string TrackingNumber { get; init; } = string.Empty;
    public DateTime ShippedAt { get; init; }
}
```

## 命名约定

### 命令 (Commands)

- 使用祈使语气
- 以动词开头
- 示例：
  - `CreateOrderCommand`
  - `UpdateProductCommand`
  - `DeleteCustomerCommand`
  - `ProcessPaymentCommand`

### 查询 (Queries)

- 使用 "Get" 或 "Find" 前缀
- 清楚表达查询意图
- 示例：
  - `GetOrderQuery`
  - `FindProductsByCategory`
  - `SearchCustomersQuery`
  - `GetOrderHistoryQuery`

### 事件 (Events)

- 使用过去式
- 描述已发生的事情
- 示例：
  - `OrderCreatedEvent`
  - `PaymentProcessedEvent`
  - `ProductUpdatedEvent`
  - `CustomerDeletedEvent`

## 最佳实践

### 1. 使用 record 类型

✅ **推荐**

```csharp
public record CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}
```

**优点**:
- 不可变性
- 值相等性
- 简洁的语法
- 良好的性能

### 2. 提供默认值

✅ **推荐**

```csharp
public record CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    public string ProductId { get; init; } = string.Empty; // 提供默认值
    public int Quantity { get; init; } = 1; // 提供默认值
}
```

### 3. 使用命名空间组织

```csharp
// Commands
namespace MyApp.Orders.Commands
{
    public record CreateOrderCommand : MessageBase, IRequest<OrderResult> { }
}

// Queries
namespace MyApp.Orders.Queries
{
    public record GetOrderQuery : MessageBase, IQuery<OrderDto> { }
}

// Events
namespace MyApp.Orders.Events
{
    public record OrderCreatedEvent : EventBase { }
}
```

### 4. 保持消息简单

✅ **推荐** - 简单的数据传输对象

```csharp
public record CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}
```

❌ **不推荐** - 包含业务逻辑

```csharp
public record CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }

    // ❌ 不要在消息中放业务逻辑
    public decimal CalculateTotalPrice() => Quantity * GetProductPrice();
}
```

### 5. 使用验证属性

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    [Required]
    [StringLength(50)]
    public string ProductId { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; init; }

    [EmailAddress]
    public string CustomerEmail { get; init; } = string.Empty;
}
```

## AOT 兼容性

所有消息类型都是 100% AOT 兼容的。

```csharp
// 使用 JSON 源生成器进行序列化
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(OrderCreatedEvent))]
partial class MyJsonContext : JsonSerializerContext { }
```

## 相关文档

- [处理器](handlers.md)
- [Mediator](mediator.md)
- [Pipeline Behaviors](pipeline.md)

