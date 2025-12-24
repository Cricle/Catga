# Catga Framework - AI 完整视图指南

> **专为 AI 助手设计**: 本指南提供 Catga 框架所有功能的完整编写指南、注意事项和最佳实践。

## 🎯 核心原则

### 1. 依赖管理原则 ⚠️ 重要

**核心库不应依赖具体实现**

```
✅ 正确的依赖关系:
Catga (核心)
  ├─ 只依赖抽象接口
  ├─ DistributedLock.Core (抽象)
  ├─ Microsoft.Extensions.* (抽象)
  └─ Polly (弹性)

Catga.Serialization.MemoryPack (序列化实现)
  ├─ 依赖 Catga
  ├─ 依赖 MemoryPack
  └─ 使用反射处理核心类型

Catga.Persistence.InMemory (持久化实现)
  ├─ 依赖 Catga
  └─ 依赖 DistributedLock.WaitHandles (具体实现)

❌ 错误的依赖关系:
Catga (核心)
  └─ MemoryPack ❌ 不应该依赖具体序列化库
  └─ DistributedLock.WaitHandles ❌ 不应该依赖具体实现
```

**关键规则**:
1. 核心库只依赖抽象和接口
2. 具体实现在各自的实现库中
3. 序列化库使用反射处理核心类型
4. 不要在核心类型上添加 `[MemoryPackable]` 特性

### 2. 类型设计原则

**核心数据类型应该是纯 POCO**

```csharp
// ✅ 正确 - 核心库中的类型
namespace Catga.Flow;

public sealed class FlowPosition
{
    public string FlowId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public int Version { get; set; }
    // 纯 POCO，无序列化特性
}

// ✅ 正确 - 序列化库中处理
namespace Catga.Serialization.MemoryPack;

public class MemoryPackFlowSerializer
{
    public byte[] Serialize(FlowPosition position)
    {
        // 使用反射方法序列化
        return MemoryPackSerializer.Serialize(typeof(FlowPosition), position);
    }
}

// ❌ 错误 - 不要在核心类型上添加特性
namespace Catga.Flow;

[MemoryPackable] // ❌ 不要这样做
public partial class FlowPosition // ❌ 不要 partial
{
    // ...
}
```

## 📦 包结构和职责

### 核心包

#### Catga (核心框架)
**职责**: 提供 CQRS 核心抽象和接口
**依赖**: 
- DistributedLock.Core (抽象)
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Logging.Abstractions
- Polly

**包含**:
- `ICatgaMediator` - 消息中介
- `IRequest<T>` - 命令/查询接口
- `IEvent` - 事件接口
- `IRequestHandler<TRequest, TResponse>` - 处理器接口
- `CatgaResult<T>` - 结果类型
- Flow DSL 核心类型（纯 POCO）

**不包含**:
- ❌ 具体的序列化实现
- ❌ 具体的持久化实现
- ❌ 具体的传输实现

#### Catga.AspNetCore
**职责**: ASP.NET Core 集成
**依赖**: Catga, Microsoft.AspNetCore.*

#### Catga.Cluster
**职责**: 集群支持
**依赖**: Catga

### 序列化包

#### Catga.Serialization.MemoryPack
**职责**: MemoryPack 序列化实现
**依赖**: Catga, MemoryPack

**关键实现**:
```csharp
// 使用反射处理核心类型
public byte[] Serialize<T>(T value)
{
    return MemoryPackSerializer.Serialize(typeof(T), value);
}

public T? Deserialize<T>(byte[] data)
{
    return (T?)MemoryPackSerializer.Deserialize(typeof(T), data);
}
```

### 持久化包

#### Catga.Persistence.InMemory
**职责**: 内存持久化（开发/测试）
**依赖**: Catga, DistributedLock.WaitHandles

**注意**: 这里可以依赖具体的锁实现

#### Catga.Persistence.Redis
**职责**: Redis 持久化
**依赖**: Catga, StackExchange.Redis, DistributedLock.Redis

#### Catga.Persistence.Nats
**职责**: NATS 持久化
**依赖**: Catga, NATS.Client

### 传输包

#### Catga.Transport.InMemory
**职责**: 内存传输（开发/测试）
**依赖**: Catga

#### Catga.Transport.Redis
**职责**: Redis 传输
**依赖**: Catga, StackExchange.Redis

#### Catga.Transport.Nats
**职责**: NATS 传输
**依赖**: Catga, NATS.Client

### 调度包

#### Catga.Scheduling.Hangfire
**职责**: Hangfire 集成
**依赖**: Catga, Hangfire

#### Catga.Scheduling.Quartz
**职责**: Quartz 集成
**依赖**: Catga, Quartz

### 源代码生成器

#### Catga.SourceGenerator
**职责**: 编译时代码生成
**依赖**: Microsoft.CodeAnalysis


## 🔧 完整功能编写指南

### 1. 创建新的 CQRS 功能

#### 步骤 1: 定义消息类型

**用户消息类型（应用层）**:
```csharp
using MemoryPack;

namespace MyApp.Commands;

// ✅ 用户定义的命令 - 添加 MemoryPackable
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId, 
    List<OrderItem> Items) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}

// ✅ 用户定义的结果 - 添加 MemoryPackable
[MemoryPackable]
public partial record OrderCreatedResult(
    string OrderId, 
    decimal Total, 
    DateTime CreatedAt);

// ✅ 用户定义的事件 - 添加 MemoryPackable
[MemoryPackable]
public partial record OrderCreatedEvent(
    string OrderId, 
    string CustomerId, 
    decimal Total) : IEvent
{
    public long MessageId { get; init; }
}
```

**核心框架类型（Catga 库内部）**:
```csharp
namespace Catga.Flow;

// ✅ 核心类型 - 不添加 MemoryPackable
public sealed class FlowPosition
{
    public string FlowId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public int Version { get; set; }
}

// ✅ 核心类型 - 不添加 MemoryPackable
public sealed class StoredSnapshot
{
    public string AggregateId { get; set; } = string.Empty;
    public long Version { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
```

#### 步骤 2: 实现处理器

```csharp
namespace MyApp.Handlers;

public sealed class CreateOrderHandler(
    IOrderRepository repository,
    ICatgaMediator mediator,
    ILogger<CreateOrderHandler> logger) 
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, 
        CancellationToken ct = default)
    {
        // 1. 验证输入
        if (string.IsNullOrWhiteSpace(cmd.CustomerId))
        {
            logger.LogWarning("CreateOrder failed: Customer ID is required");
            return CatgaResult<OrderCreatedResult>.Failure("Customer ID is required");
        }

        if (cmd.Items == null || cmd.Items.Count == 0)
        {
            logger.LogWarning("CreateOrder failed: No items provided");
            return CatgaResult<OrderCreatedResult>.Failure(
                "Order must contain at least one item");
        }

        // 2. 执行业务逻辑
        try
        {
            var orderId = Guid.NewGuid().ToString("N")[..8];
            var total = cmd.Items.Sum(i => i.Price * i.Quantity);
            var createdAt = DateTime.UtcNow;

            var order = new Order(
                orderId, 
                cmd.CustomerId, 
                cmd.Items, 
                OrderStatus.Pending,
                total, 
                createdAt);

            await repository.SaveAsync(order, ct);

            logger.LogInformation(
                "Order {OrderId} created for customer {CustomerId}, total: {Total}",
                orderId, cmd.CustomerId, total);

            // 3. 发布领域事件
            await mediator.PublishAsync(
                new OrderCreatedEvent(orderId, cmd.CustomerId, total), ct);

            // 4. 返回结果
            return CatgaResult<OrderCreatedResult>.Success(
                new OrderCreatedResult(orderId, total, createdAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create order for customer {CustomerId}", 
                cmd.CustomerId);
            return CatgaResult<OrderCreatedResult>.Failure(
                $"Failed to create order: {ex.Message}");
        }
    }
}
```

#### 步骤 3: 实现事件处理器

```csharp
namespace MyApp.Handlers;

// 事件处理器 1: 发送通知
public sealed class OrderNotificationHandler(
    IEmailService emailService,
    ILogger<OrderNotificationHandler> logger) 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await emailService.SendOrderConfirmationAsync(
                evt.CustomerId, evt.OrderId, evt.Total, ct);
            
            logger.LogInformation(
                "Sent order confirmation email for order {OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to send order confirmation for {OrderId}", evt.OrderId);
            // 不要抛出异常，让其他处理器继续执行
        }
    }
}

// 事件处理器 2: 更新统计
public sealed class OrderAnalyticsHandler(
    IAnalyticsService analytics,
    ILogger<OrderAnalyticsHandler> logger) 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await analytics.TrackOrderCreatedAsync(
                evt.OrderId, evt.CustomerId, evt.Total, ct);
            
            logger.LogInformation(
                "Tracked order analytics for {OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to track analytics for {OrderId}", evt.OrderId);
        }
    }
}
```

#### 步骤 4: 注册服务

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. 配置 Catga
var catga = builder.Services.AddCatga()
    .UseMemoryPack(); // 使用 MemoryPack 序列化

// 2. 配置持久化
if (builder.Environment.IsDevelopment())
{
    catga.UseInMemory(); // 开发环境
}
else
{
    catga.UseRedis(builder.Configuration.GetConnectionString("Redis")!); // 生产环境
}

// 3. 配置传输
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddInMemoryTransport();
}
else
{
    builder.Services.AddRedisTransport(
        builder.Configuration.GetConnectionString("Redis")!);
}

// 4. 注册处理器
builder.Services.AddCatgaHandlers();

// 5. 注册应用服务
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

var app = builder.Build();

// 6. 映射端点
app.MapPost("/orders", async (
    CreateOrderRequest request, 
    ICatgaMediator mediator) =>
{
    var command = new CreateOrderCommand(request.CustomerId, request.Items);
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);
    
    return result.IsSuccess
        ? Results.Created($"/orders/{result.Value!.OrderId}", result.Value)
        : Results.BadRequest(new { error = result.Error });
});

app.Run();
```

### 2. 实现事件溯源

#### 定义聚合根

```csharp
namespace MyApp.Domain;

public sealed class OrderAggregate
{
    private readonly List<object> _uncommittedEvents = new();
    
    // 状态
    public string Id { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public List<OrderItem> Items { get; private set; } = new();
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public string? TrackingNumber { get; private set; }
    
    // 创建订单
    public void CreateOrder(string customerId, List<OrderItem> items)
    {
        if (string.IsNullOrEmpty(customerId))
            throw new ArgumentException("Customer ID is required", nameof(customerId));
        
        if (items == null || items.Count == 0)
            throw new ArgumentException("Items are required", nameof(items));
        
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var total = items.Sum(i => i.Price * i.Quantity);
        var createdAt = DateTime.UtcNow;
        
        var evt = new OrderCreatedEvent(orderId, customerId, total);
        Apply(evt);
        _uncommittedEvents.Add(evt);
    }
    
    // 支付订单
    public void PayOrder(string paymentMethod)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot pay order in {Status} status");
        
        var evt = new OrderPaidEvent(Id, paymentMethod, DateTime.UtcNow);
        Apply(evt);
        _uncommittedEvents.Add(evt);
    }
    
    // 发货订单
    public void ShipOrder(string trackingNumber)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException(
                $"Cannot ship order in {Status} status");
        
        var evt = new OrderShippedEvent(Id, trackingNumber, DateTime.UtcNow);
        Apply(evt);
        _uncommittedEvents.Add(evt);
    }
    
    // 取消订单
    public void CancelOrder()
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot cancel order in {Status} status");
        
        var evt = new OrderCancelledEvent(Id, DateTime.UtcNow);
        Apply(evt);
        _uncommittedEvents.Add(evt);
    }
    
    // 应用事件（状态变更）
    private void Apply(OrderCreatedEvent evt)
    {
        Id = evt.OrderId;
        CustomerId = evt.CustomerId;
        Total = evt.Total;
        Status = OrderStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }
    
    private void Apply(OrderPaidEvent evt)
    {
        Status = OrderStatus.Paid;
        PaidAt = evt.PaidAt;
    }
    
    private void Apply(OrderShippedEvent evt)
    {
        Status = OrderStatus.Shipped;
        ShippedAt = evt.ShippedAt;
        TrackingNumber = evt.TrackingNumber;
    }
    
    private void Apply(OrderCancelledEvent evt)
    {
        Status = OrderStatus.Cancelled;
    }
    
    // 从事件历史重建状态
    public void LoadFromHistory(IEnumerable<object> events)
    {
        foreach (var evt in events)
        {
            switch (evt)
            {
                case OrderCreatedEvent e:
                    Apply(e);
                    break;
                case OrderPaidEvent e:
                    Apply(e);
                    break;
                case OrderShippedEvent e:
                    Apply(e);
                    break;
                case OrderCancelledEvent e:
                    Apply(e);
                    break;
            }
        }
    }
    
    // 获取未提交的事件
    public IReadOnlyList<object> GetUncommittedEvents() => _uncommittedEvents;
    
    // 清除未提交的事件
    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();
}
```

#### 实现事件存储

```csharp
namespace MyApp.Infrastructure;

public interface IEventStore
{
    Task SaveEventsAsync(string aggregateId, IEnumerable<object> events, 
        long expectedVersion, CancellationToken ct = default);
    Task<List<object>> GetEventsAsync(string aggregateId, 
        CancellationToken ct = default);
}

public sealed class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, List<object>> _events = new();
    
    public Task SaveEventsAsync(
        string aggregateId, 
        IEnumerable<object> events, 
        long expectedVersion, 
        CancellationToken ct = default)
    {
        var eventList = _events.GetOrAdd(aggregateId, _ => new List<object>());
        
        lock (eventList)
        {
            if (eventList.Count != expectedVersion)
                throw new InvalidOperationException("Concurrency conflict");
            
            eventList.AddRange(events);
        }
        
        return Task.CompletedTask;
    }
    
    public Task<List<object>> GetEventsAsync(
        string aggregateId, 
        CancellationToken ct = default)
    {
        if (_events.TryGetValue(aggregateId, out var events))
        {
            lock (events)
            {
                return Task.FromResult(new List<object>(events));
            }
        }
        
        return Task.FromResult(new List<object>());
    }
}
```


### 3. 实现 Flow DSL

#### 定义 Flow

```csharp
namespace MyApp.Flows;

public sealed class OrderProcessingFlow : FlowDefinition
{
    public override string FlowId => "order-processing";
    
    protected override void Configure()
    {
        // 开始节点
        Start("create-order")
            .OnSuccess("validate-inventory")
            .OnFailure("notify-failure");
        
        // 验证库存
        Node("validate-inventory")
            .Execute<ValidateInventoryActivity>()
            .OnSuccess("reserve-inventory")
            .OnFailure("notify-out-of-stock");
        
        // 预留库存
        Node("reserve-inventory")
            .Execute<ReserveInventoryActivity>()
            .OnSuccess("process-payment")
            .OnFailure("notify-reservation-failed");
        
        // 处理支付
        Node("process-payment")
            .Execute<ProcessPaymentActivity>()
            .OnSuccess("confirm-order")
            .OnFailure("release-inventory");
        
        // 确认订单
        Node("confirm-order")
            .Execute<ConfirmOrderActivity>()
            .OnSuccess("end")
            .OnFailure("refund-payment");
        
        // 失败处理
        Node("release-inventory")
            .Execute<ReleaseInventoryActivity>()
            .OnSuccess("notify-failure")
            .OnFailure("notify-failure");
        
        Node("refund-payment")
            .Execute<RefundPaymentActivity>()
            .OnSuccess("release-inventory")
            .OnFailure("notify-failure");
        
        // 通知节点
        Node("notify-failure")
            .Execute<NotifyFailureActivity>()
            .OnSuccess("end")
            .OnFailure("end");
        
        Node("notify-out-of-stock")
            .Execute<NotifyOutOfStockActivity>()
            .OnSuccess("end")
            .OnFailure("end");
        
        Node("notify-reservation-failed")
            .Execute<NotifyReservationFailedActivity>()
            .OnSuccess("end")
            .OnFailure("end");
        
        // 结束节点
        End("end");
    }
}
```

#### 实现 Activity

```csharp
namespace MyApp.Activities;

public sealed class ValidateInventoryActivity(
    IInventoryService inventory,
    ILogger<ValidateInventoryActivity> logger) 
    : IFlowActivity<OrderContext>
{
    public async ValueTask<FlowResult> ExecuteAsync(
        OrderContext context, 
        CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation(
                "Validating inventory for order {OrderId}", context.OrderId);
            
            foreach (var item in context.Items)
            {
                var available = await inventory.CheckAvailabilityAsync(
                    item.ProductId, item.Quantity, ct);
                
                if (!available)
                {
                    logger.LogWarning(
                        "Product {ProductId} not available in quantity {Quantity}",
                        item.ProductId, item.Quantity);
                    
                    return FlowResult.Failure(
                        $"Product {item.ProductId} is out of stock");
                }
            }
            
            logger.LogInformation(
                "Inventory validated for order {OrderId}", context.OrderId);
            
            return FlowResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to validate inventory for order {OrderId}", context.OrderId);
            return FlowResult.Failure($"Inventory validation failed: {ex.Message}");
        }
    }
}

public sealed class ReserveInventoryActivity(
    IInventoryService inventory,
    ILogger<ReserveInventoryActivity> logger) 
    : IFlowActivity<OrderContext>
{
    public async ValueTask<FlowResult> ExecuteAsync(
        OrderContext context, 
        CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation(
                "Reserving inventory for order {OrderId}", context.OrderId);
            
            var reservationId = await inventory.ReserveAsync(
                context.OrderId, context.Items, ct);
            
            context.ReservationId = reservationId;
            
            logger.LogInformation(
                "Inventory reserved for order {OrderId}, reservation: {ReservationId}",
                context.OrderId, reservationId);
            
            return FlowResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to reserve inventory for order {OrderId}", context.OrderId);
            return FlowResult.Failure($"Inventory reservation failed: {ex.Message}");
        }
    }
}
```

### 4. 实现查询（读模型）

#### 定义查询

```csharp
namespace MyApp.Queries;

[MemoryPackable]
public partial record GetOrderQuery(string OrderId) : IRequest<Order?>
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record GetOrdersByCustomerQuery(
    string CustomerId, 
    int PageNumber = 1, 
    int PageSize = 20) : IRequest<PagedResult<Order>>
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record GetOrderStatisticsQuery(
    DateTime? StartDate = null, 
    DateTime? EndDate = null) : IRequest<OrderStatistics>
{
    public long MessageId { get; init; }
}
```

#### 实现查询处理器

```csharp
namespace MyApp.Handlers;

public sealed class GetOrderQueryHandler(
    IOrderReadRepository repository,
    ILogger<GetOrderQueryHandler> logger) 
    : IRequestHandler<GetOrderQuery, Order?>
{
    public async ValueTask<CatgaResult<Order?>> HandleAsync(
        GetOrderQuery query, 
        CancellationToken ct = default)
    {
        try
        {
            var order = await repository.GetByIdAsync(query.OrderId, ct);
            
            if (order == null)
            {
                logger.LogWarning("Order {OrderId} not found", query.OrderId);
                return CatgaResult<Order?>.Success(null);
            }
            
            return CatgaResult<Order?>.Success(order);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get order {OrderId}", query.OrderId);
            return CatgaResult<Order?>.Failure($"Failed to get order: {ex.Message}");
        }
    }
}

public sealed class GetOrdersByCustomerQueryHandler(
    IOrderReadRepository repository,
    ILogger<GetOrdersByCustomerQueryHandler> logger) 
    : IRequestHandler<GetOrdersByCustomerQuery, PagedResult<Order>>
{
    public async ValueTask<CatgaResult<PagedResult<Order>>> HandleAsync(
        GetOrdersByCustomerQuery query, 
        CancellationToken ct = default)
    {
        try
        {
            var (orders, totalCount) = await repository.GetByCustomerAsync(
                query.CustomerId, 
                query.PageNumber, 
                query.PageSize, 
                ct);
            
            var result = new PagedResult<Order>(
                orders, 
                totalCount, 
                query.PageNumber, 
                query.PageSize);
            
            return CatgaResult<PagedResult<Order>>.Success(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to get orders for customer {CustomerId}", query.CustomerId);
            return CatgaResult<PagedResult<Order>>.Failure(
                $"Failed to get orders: {ex.Message}");
        }
    }
}
```

#### 实现读模型更新（事件投影）

```csharp
namespace MyApp.Projections;

public sealed class OrderReadModelProjection(
    IOrderReadRepository readRepository,
    ILogger<OrderReadModelProjection> logger) 
    : IEventHandler<OrderCreatedEvent>,
      IEventHandler<OrderPaidEvent>,
      IEventHandler<OrderShippedEvent>,
      IEventHandler<OrderCancelledEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            var readModel = new OrderReadModel
            {
                Id = evt.OrderId,
                CustomerId = evt.CustomerId,
                Total = evt.Total,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            await readRepository.InsertAsync(readModel, ct);
            
            logger.LogInformation(
                "Created read model for order {OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to create read model for order {OrderId}", evt.OrderId);
        }
    }
    
    public async ValueTask HandleAsync(
        OrderPaidEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await readRepository.UpdateStatusAsync(
                evt.OrderId, 
                OrderStatus.Paid, 
                evt.PaidAt, 
                ct);
            
            logger.LogInformation(
                "Updated read model for order {OrderId} to Paid", evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to update read model for order {OrderId}", evt.OrderId);
        }
    }
    
    public async ValueTask HandleAsync(
        OrderShippedEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await readRepository.UpdateStatusAsync(
                evt.OrderId, 
                OrderStatus.Shipped, 
                evt.ShippedAt, 
                ct);
            
            await readRepository.UpdateTrackingNumberAsync(
                evt.OrderId, 
                evt.TrackingNumber, 
                ct);
            
            logger.LogInformation(
                "Updated read model for order {OrderId} to Shipped", evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to update read model for order {OrderId}", evt.OrderId);
        }
    }
    
    public async ValueTask HandleAsync(
        OrderCancelledEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await readRepository.UpdateStatusAsync(
                evt.OrderId, 
                OrderStatus.Cancelled, 
                DateTime.UtcNow, 
                ct);
            
            logger.LogInformation(
                "Updated read model for order {OrderId} to Cancelled", evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Failed to update read model for order {OrderId}", evt.OrderId);
        }
    }
}
```


## ⚠️ 关键注意事项

### 1. 依赖管理注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 在核心库中依赖具体实现
// File: src/Catga/Catga.csproj
<ItemGroup>
  <PackageReference Include="MemoryPack" /> ❌ 不要这样做
  <PackageReference Include="DistributedLock.WaitHandles" /> ❌ 不要这样做
</ItemGroup>

// 错误 2: 在核心类型上添加序列化特性
// File: src/Catga/Flow/FlowPosition.cs
[MemoryPackable] ❌ 不要这样做
public partial class FlowPosition
{
    // ...
}

// 错误 3: 在用户代码中不添加序列化特性
// File: MyApp/Commands/CreateOrderCommand.cs
public record CreateOrderCommand(...) : IRequest<OrderCreatedResult> ❌ 缺少 [MemoryPackable]
{
    public long MessageId { get; init; }
}
```

#### ✅ 正确做法

```csharp
// 正确 1: 核心库只依赖抽象
// File: src/Catga/Catga.csproj
<ItemGroup>
  <PackageReference Include="DistributedLock.Core" /> ✅ 只依赖抽象
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  <PackageReference Include="Polly" />
</ItemGroup>

// 正确 2: 核心类型保持纯 POCO
// File: src/Catga/Flow/FlowPosition.cs
public sealed class FlowPosition ✅ 纯 POCO
{
    public string FlowId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public int Version { get; set; }
}

// 正确 3: 序列化库使用反射处理核心类型
// File: src/Catga.Serialization.MemoryPack/MemoryPackSerializer.cs
public byte[] Serialize<T>(T value)
{
    return MemoryPackSerializer.Serialize(typeof(T), value); ✅ 使用反射
}

// 正确 4: 用户代码添加序列化特性
// File: MyApp/Commands/CreateOrderCommand.cs
[MemoryPackable] ✅ 添加特性
public partial record CreateOrderCommand(...) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}
```

### 2. 消息设计注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 忘记 MessageId
public record CreateOrderCommand(string CustomerId) : IRequest<OrderCreatedResult>
{
    // ❌ 缺少 MessageId
}

// 错误 2: 使用可变类型
public class CreateOrderCommand : IRequest<OrderCreatedResult>
{
    public string CustomerId { get; set; } // ❌ 可变
    public long MessageId { get; set; } // ❌ 可变
}

// 错误 3: 忘记 partial 关键字
[MemoryPackable]
public record CreateOrderCommand(...) : IRequest<OrderCreatedResult> // ❌ 缺少 partial
{
    public long MessageId { get; init; }
}

// 错误 4: 在消息中包含行为
[MemoryPackable]
public partial record CreateOrderCommand(...) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
    
    public decimal CalculateTotal() // ❌ 不要在消息中添加行为
    {
        return Items.Sum(i => i.Price * i.Quantity);
    }
}

// 错误 5: 使用复杂的继承
[MemoryPackable]
public abstract partial record BaseCommand : IRequest<Result> // ❌ 避免复杂继承
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record CreateOrderCommand(...) : BaseCommand // ❌ 序列化可能有问题
{
}
```

#### ✅ 正确做法

```csharp
// 正确 1: 包含 MessageId
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId, 
    List<OrderItem> Items) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; } ✅
}

// 正确 2: 使用不可变 record
[MemoryPackable]
public partial record CreateOrderCommand(...) : IRequest<OrderCreatedResult> ✅
{
    public long MessageId { get; init; } ✅
}

// 正确 3: 添加 partial 关键字
[MemoryPackable]
public partial record CreateOrderCommand(...) : IRequest<OrderCreatedResult> ✅
{
    public long MessageId { get; init; }
}

// 正确 4: 消息只包含数据
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId, 
    List<OrderItem> Items) : IRequest<OrderCreatedResult> ✅ 只有数据
{
    public long MessageId { get; init; }
}

// 正确 5: 使用简单的类型结构
[MemoryPackable]
public partial record CreateOrderCommand(...) : IRequest<OrderCreatedResult> ✅ 简单结构
{
    public long MessageId { get; init; }
}
```

### 3. 处理器实现注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 不处理异常
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    var order = await repository.SaveAsync(cmd); // ❌ 异常会传播
    return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.Id));
}

// 错误 2: 阻塞异步操作
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    var order = repository.SaveAsync(cmd).Result; // ❌ 阻塞
    return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.Id));
}

// 错误 3: 忽略 CancellationToken
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    await Task.Delay(1000); // ❌ 没有传递 ct
    var order = await repository.SaveAsync(cmd); // ❌ 没有传递 ct
    return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.Id));
}

// 错误 4: 抛出异常表示业务失败
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    if (string.IsNullOrEmpty(cmd.CustomerId))
        throw new ArgumentException("Customer ID required"); // ❌ 不要抛出异常
    
    // ...
}

// 错误 5: 直接调用其他处理器
public sealed class CreateOrderHandler(
    PayOrderHandler payHandler) // ❌ 不要依赖其他处理器
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, 
        CancellationToken ct = default)
    {
        // ...
        await payHandler.HandleAsync(new PayOrderCommand(orderId), ct); // ❌ 不要这样做
        // ...
    }
}

// 错误 6: 处理器不是 sealed
public class CreateOrderHandler // ❌ 应该是 sealed
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    // ...
}
```

#### ✅ 正确做法

```csharp
// 正确 1: 捕获并处理异常
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    try
    {
        var order = await repository.SaveAsync(cmd, ct); ✅
        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(order.Id));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create order");
        return CatgaResult<OrderCreatedResult>.Failure(
            $"Failed to create order: {ex.Message}"); ✅
    }
}

// 正确 2: 使用 await
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    var order = await repository.SaveAsync(cmd, ct); ✅ 使用 await
    return CatgaResult<OrderCreatedResult>.Success(
        new OrderCreatedResult(order.Id));
}

// 正确 3: 传递 CancellationToken
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    await Task.Delay(1000, ct); ✅ 传递 ct
    var order = await repository.SaveAsync(cmd, ct); ✅ 传递 ct
    return CatgaResult<OrderCreatedResult>.Success(
        new OrderCreatedResult(order.Id));
}

// 正确 4: 返回 CatgaResult 表示失败
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
    CreateOrderCommand cmd, 
    CancellationToken ct = default)
{
    if (string.IsNullOrEmpty(cmd.CustomerId))
        return CatgaResult<OrderCreatedResult>.Failure(
            "Customer ID is required"); ✅ 返回失败结果
    
    // ...
}

// 正确 5: 通过 Mediator 发送命令
public sealed class CreateOrderHandler(
    ICatgaMediator mediator) ✅ 依赖 Mediator
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, 
        CancellationToken ct = default)
    {
        // ...
        await mediator.SendAsync(new PayOrderCommand(orderId), ct); ✅ 通过 Mediator
        // ...
    }
}

// 正确 6: 处理器是 sealed
public sealed class CreateOrderHandler ✅ sealed
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    // ...
}
```

### 4. 事件处理注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 事件处理器抛出异常
public sealed class OrderNotificationHandler 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        await emailService.SendAsync(evt.CustomerId); // ❌ 异常会中断其他处理器
    }
}

// 错误 2: 事件处理器返回结果
public sealed class OrderNotificationHandler 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask<bool> HandleAsync( // ❌ 不应该返回结果
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        await emailService.SendAsync(evt.CustomerId);
        return true; // ❌ 事件处理器不返回结果
    }
}

// 错误 3: 事件处理器之间有依赖
public sealed class OrderAnalyticsHandler(
    OrderNotificationHandler notificationHandler) // ❌ 不要依赖其他处理器
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        await notificationHandler.HandleAsync(evt, ct); // ❌ 不要这样做
        // ...
    }
}
```

#### ✅ 正确做法

```csharp
// 正确 1: 捕获异常，不影响其他处理器
public sealed class OrderNotificationHandler(
    IEmailService emailService,
    ILogger<OrderNotificationHandler> logger) 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await emailService.SendAsync(evt.CustomerId, ct); ✅
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification"); ✅ 记录错误
            // 不抛出异常，让其他处理器继续执行 ✅
        }
    }
}

// 正确 2: 事件处理器返回 ValueTask
public sealed class OrderNotificationHandler 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync( ✅ 返回 ValueTask
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        await emailService.SendAsync(evt.CustomerId, ct);
    }
}

// 正确 3: 事件处理器独立
public sealed class OrderAnalyticsHandler(
    IAnalyticsService analytics, ✅ 依赖服务，不依赖其他处理器
    ILogger<OrderAnalyticsHandler> logger) 
    : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(
        OrderCreatedEvent evt, 
        CancellationToken ct = default)
    {
        try
        {
            await analytics.TrackAsync(evt, ct); ✅
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to track analytics");
        }
    }
}
```


### 5. 配置和注册注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 忘记注册处理器
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga().UseMemoryPack().UseInMemory();
builder.Services.AddInMemoryTransport();
// ❌ 忘记调用 AddCatgaHandlers()

// 错误 2: 配置顺序错误
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInMemoryTransport(); // ❌ 应该先配置 Catga
builder.Services.AddCatga().UseMemoryPack().UseInMemory();

// 错误 3: 重复配置
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga().UseMemoryPack().UseInMemory();
builder.Services.AddCatga(); // ❌ 重复配置

// 错误 4: 混用不同的后端
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .UseRedis("localhost:6379"); // ❌ 不要混用持久化后端

// 错误 5: 忘记配置序列化
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga() // ❌ 没有配置序列化
    .UseInMemory();
```

#### ✅ 正确做法

```csharp
// 正确 1: 完整的配置流程
var builder = WebApplication.CreateBuilder(args);

// 1. 配置 Catga 和序列化
var catga = builder.Services.AddCatga()
    .UseMemoryPack(); ✅

// 2. 配置持久化
if (builder.Environment.IsDevelopment())
{
    catga.UseInMemory(); ✅
}
else
{
    catga.UseRedis(builder.Configuration.GetConnectionString("Redis")!); ✅
}

// 3. 配置传输
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddInMemoryTransport(); ✅
}
else
{
    builder.Services.AddRedisTransport(
        builder.Configuration.GetConnectionString("Redis")!); ✅
}

// 4. 注册处理器
builder.Services.AddCatgaHandlers(); ✅

// 5. 注册应用服务
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

var app = builder.Build();
app.Run();

// 正确 2: 生产环境配置
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatga()
    .UseMemoryPack()
    .UseRedis(builder.Configuration.GetConnectionString("Redis")!); ✅

builder.Services.AddRedisTransport(
    builder.Configuration.GetConnectionString("Redis")!); ✅

builder.Services.AddCatgaHandlers(); ✅

// 正确 3: NATS 配置
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatga()
    .UseMemoryPack()
    .UseNats(); ✅

builder.Services.AddNatsConnection(
    builder.Configuration.GetConnectionString("Nats")!); ✅
builder.Services.AddNatsTransport(
    builder.Configuration.GetConnectionString("Nats")!); ✅

builder.Services.AddCatgaHandlers(); ✅
```

### 6. 测试注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 不使用 Mock
[Fact]
public async Task CreateOrder_ValidCommand_ReturnsSuccess()
{
    var handler = new CreateOrderHandler(null!, null!); // ❌ 传递 null
    // ...
}

// 错误 2: 测试实现细节
[Fact]
public async Task CreateOrder_CallsRepositorySave()
{
    // ❌ 测试实现细节而不是行为
    var repository = Substitute.For<IOrderRepository>();
    var handler = new CreateOrderHandler(repository, null!);
    
    await handler.HandleAsync(new CreateOrderCommand("c1", items));
    
    repository.Received(1).SaveAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
}

// 错误 3: 不测试失败场景
[Fact]
public async Task CreateOrder_ValidCommand_ReturnsSuccess()
{
    // ✅ 测试成功场景
    // ❌ 但没有测试失败场景
}

// 错误 4: 测试中使用真实的外部依赖
[Fact]
public async Task CreateOrder_ValidCommand_ReturnsSuccess()
{
    var emailService = new SmtpEmailService(); // ❌ 使用真实的 SMTP
    // ...
}
```

#### ✅ 正确做法

```csharp
// 正确 1: 使用 Mock
[Fact]
public async Task CreateOrder_ValidCommand_ReturnsSuccess()
{
    // Arrange
    var repository = Substitute.For<IOrderRepository>(); ✅
    var mediator = Substitute.For<ICatgaMediator>(); ✅
    var logger = Substitute.For<ILogger<CreateOrderHandler>>(); ✅
    
    var handler = new CreateOrderHandler(repository, mediator, logger);
    
    var command = new CreateOrderCommand(
        "customer-1",
        new List<OrderItem> { new("p1", "Product", 1, 99.99m) });
    
    // Act
    var result = await handler.HandleAsync(command);
    
    // Assert
    result.IsSuccess.Should().BeTrue(); ✅
    result.Value.Should().NotBeNull();
    result.Value!.Total.Should().Be(99.99m);
}

// 正确 2: 测试行为而不是实现
[Fact]
public async Task CreateOrder_ValidCommand_CreatesOrderSuccessfully()
{
    // Arrange
    var repository = Substitute.For<IOrderRepository>();
    var mediator = Substitute.For<ICatgaMediator>();
    var logger = Substitute.For<ILogger<CreateOrderHandler>>();
    var handler = new CreateOrderHandler(repository, mediator, logger);
    
    var command = new CreateOrderCommand(
        "customer-1",
        new List<OrderItem> { new("p1", "Product", 1, 99.99m) });
    
    // Act
    var result = await handler.HandleAsync(command);
    
    // Assert - 测试行为结果 ✅
    result.IsSuccess.Should().BeTrue();
    result.Value.Should().NotBeNull();
    result.Value!.OrderId.Should().NotBeNullOrEmpty();
    result.Value!.Total.Should().Be(99.99m);
    
    // 验证事件发布 ✅
    await mediator.Received(1).PublishAsync(
        Arg.Is<OrderCreatedEvent>(e => 
            e.OrderId == result.Value!.OrderId && 
            e.Total == 99.99m),
        Arg.Any<CancellationToken>());
}

// 正确 3: 测试失败场景
[Theory]
[InlineData("", "Customer ID is required")]
[InlineData(null, "Customer ID is required")]
public async Task CreateOrder_InvalidCustomerId_ReturnsFailure(
    string customerId, 
    string expectedError)
{
    // Arrange
    var handler = new CreateOrderHandler(null!, null!, null!);
    var command = new CreateOrderCommand(customerId, new List<OrderItem>());
    
    // Act
    var result = await handler.HandleAsync(command);
    
    // Assert
    result.IsSuccess.Should().BeFalse(); ✅
    result.Error.Should().Contain(expectedError); ✅
}

[Fact]
public async Task CreateOrder_EmptyItems_ReturnsFailure()
{
    // Arrange
    var handler = new CreateOrderHandler(null!, null!, null!);
    var command = new CreateOrderCommand("customer-1", new List<OrderItem>());
    
    // Act
    var result = await handler.HandleAsync(command);
    
    // Assert
    result.IsSuccess.Should().BeFalse(); ✅
    result.Error.Should().Contain("at least one item"); ✅
}

// 正确 4: Mock 外部依赖
[Fact]
public async Task OrderNotificationHandler_SendsEmail()
{
    // Arrange
    var emailService = Substitute.For<IEmailService>(); ✅ Mock
    var logger = Substitute.For<ILogger<OrderNotificationHandler>>(); ✅
    var handler = new OrderNotificationHandler(emailService, logger);
    
    var evt = new OrderCreatedEvent("order-1", "customer-1", 99.99m);
    
    // Act
    await handler.HandleAsync(evt);
    
    // Assert
    await emailService.Received(1).SendOrderConfirmationAsync(
        "customer-1", "order-1", 99.99m, Arg.Any<CancellationToken>()); ✅
}
```

### 7. 性能优化注意事项

#### ❌ 常见错误

```csharp
// 错误 1: 在循环中发送命令
public async Task ProcessOrdersAsync(List<string> orderIds)
{
    foreach (var orderId in orderIds)
    {
        await mediator.SendAsync(new ProcessOrderCommand(orderId)); // ❌ 串行处理
    }
}

// 错误 2: 不使用 ValueTask
public async Task<CatgaResult> HandleAsync( // ❌ 应该用 ValueTask
    Command cmd, 
    CancellationToken ct = default)
{
    // ...
}

// 错误 3: 过度使用异步
public async ValueTask<CatgaResult> HandleAsync(
    Command cmd, 
    CancellationToken ct = default)
{
    var result = ValidateCommand(cmd); // 同步操作
    return await Task.FromResult(result); // ❌ 不必要的异步
}

// 错误 4: 不使用 ConfigureAwait
public async ValueTask<CatgaResult> HandleAsync(
    Command cmd, 
    CancellationToken ct = default)
{
    await SomeAsyncOperation(); // ❌ 在库代码中应该使用 ConfigureAwait(false)
    // ...
}
```

#### ✅ 正确做法

```csharp
// 正确 1: 并行处理
public async Task ProcessOrdersAsync(List<string> orderIds)
{
    var tasks = orderIds.Select(orderId => 
        mediator.SendAsync(new ProcessOrderCommand(orderId))); ✅
    
    var results = await Task.WhenAll(tasks); ✅
}

// 正确 2: 使用 ValueTask
public async ValueTask<CatgaResult> HandleAsync( ✅ ValueTask
    Command cmd, 
    CancellationToken ct = default)
{
    // ...
}

// 正确 3: 同步路径优化
public ValueTask<CatgaResult> HandleAsync(
    Command cmd, 
    CancellationToken ct = default)
{
    // 快速路径 - 同步返回 ✅
    if (!IsValid(cmd))
        return ValueTask.FromResult(CatgaResult.Failure("Invalid command"));
    
    // 慢速路径 - 异步处理 ✅
    return HandleAsyncCore(cmd, ct);
}

private async ValueTask<CatgaResult> HandleAsyncCore(
    Command cmd, 
    CancellationToken ct)
{
    // 异步操作
    await repository.SaveAsync(cmd, ct);
    return CatgaResult.Success();
}

// 正确 4: 在库代码中使用 ConfigureAwait(false)
public async ValueTask<CatgaResult> HandleAsync(
    Command cmd, 
    CancellationToken ct = default)
{
    await SomeAsyncOperation().ConfigureAwait(false); ✅
    // ...
}
```


## 📝 代码模板和检查清单

### 命令模板

```csharp
using MemoryPack;

namespace MyApp.Commands;

/// <summary>
/// [命令描述]
/// </summary>
[MemoryPackable]
public partial record [CommandName](
    [参数列表]) : IRequest<[ResultType]>
{
    public long MessageId { get; init; }
}

/// <summary>
/// [结果描述]
/// </summary>
[MemoryPackable]
public partial record [ResultType](
    [结果字段]);
```

### 命令处理器模板

```csharp
namespace MyApp.Handlers;

/// <summary>
/// [处理器描述]
/// </summary>
public sealed class [CommandName]Handler(
    [依赖注入参数],
    ILogger<[CommandName]Handler> logger) 
    : IRequestHandler<[CommandName], [ResultType]>
{
    public async ValueTask<CatgaResult<[ResultType]>> HandleAsync(
        [CommandName] cmd, 
        CancellationToken ct = default)
    {
        // 1. 验证输入
        if ([验证条件])
        {
            logger.LogWarning("[警告消息]");
            return CatgaResult<[ResultType]>.Failure("[错误消息]");
        }
        
        // 2. 执行业务逻辑
        try
        {
            // 业务逻辑
            
            logger.LogInformation("[成功消息]");
            
            // 3. 发布事件（如果需要）
            await mediator.PublishAsync(new [EventName](...), ct);
            
            // 4. 返回结果
            return CatgaResult<[ResultType]>.Success(new [ResultType](...));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[错误消息]");
            return CatgaResult<[ResultType]>.Failure($"[错误消息]: {ex.Message}");
        }
    }
}
```

### 事件模板

```csharp
using MemoryPack;

namespace MyApp.Events;

/// <summary>
/// [事件描述]
/// </summary>
[MemoryPackable]
public partial record [EventName](
    [事件字段]) : IEvent
{
    public long MessageId { get; init; }
}
```

### 事件处理器模板

```csharp
namespace MyApp.Handlers;

/// <summary>
/// [处理器描述]
/// </summary>
public sealed class [EventName]Handler(
    [依赖注入参数],
    ILogger<[EventName]Handler> logger) 
    : IEventHandler<[EventName]>
{
    public async ValueTask HandleAsync(
        [EventName] evt, 
        CancellationToken ct = default)
    {
        try
        {
            // 处理事件
            
            logger.LogInformation("[成功消息]");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[错误消息]");
            // 不抛出异常，让其他处理器继续执行
        }
    }
}
```

### 查询模板

```csharp
using MemoryPack;

namespace MyApp.Queries;

/// <summary>
/// [查询描述]
/// </summary>
[MemoryPackable]
public partial record [QueryName](
    [查询参数]) : IRequest<[ResultType]>
{
    public long MessageId { get; init; }
}
```

### 查询处理器模板

```csharp
namespace MyApp.Handlers;

/// <summary>
/// [处理器描述]
/// </summary>
public sealed class [QueryName]Handler(
    [依赖注入参数],
    ILogger<[QueryName]Handler> logger) 
    : IRequestHandler<[QueryName], [ResultType]>
{
    public async ValueTask<CatgaResult<[ResultType]>> HandleAsync(
        [QueryName] query, 
        CancellationToken ct = default)
    {
        try
        {
            // 执行查询
            var result = await repository.GetAsync(..., ct);
            
            if (result == null)
            {
                logger.LogWarning("[未找到消息]");
                return CatgaResult<[ResultType]>.Success(null);
            }
            
            return CatgaResult<[ResultType]>.Success(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[错误消息]");
            return CatgaResult<[ResultType]>.Failure($"[错误消息]: {ex.Message}");
        }
    }
}
```

## ✅ 代码审查检查清单

### 消息类型检查

- [ ] 添加了 `[MemoryPackable]` 特性
- [ ] 使用了 `partial` 关键字
- [ ] 包含 `MessageId` 属性
- [ ] 使用 `record` 类型（不可变）
- [ ] 实现了正确的接口（`IRequest<T>` 或 `IEvent`）
- [ ] 使用简单的数据类型（避免复杂继承）
- [ ] 只包含数据，不包含行为
- [ ] 命名符合约定（命令：动词，事件：过去式）

### 处理器检查

- [ ] 类是 `sealed`
- [ ] 使用构造函数注入依赖
- [ ] 方法签名正确（`HandleAsync`）
- [ ] 返回类型正确（`ValueTask<CatgaResult<T>>` 或 `ValueTask`）
- [ ] 包含 `CancellationToken` 参数
- [ ] 验证输入参数
- [ ] 捕获并处理异常
- [ ] 返回 `CatgaResult` 而不是抛出异常
- [ ] 传递 `CancellationToken` 到异步调用
- [ ] 添加了日志记录
- [ ] 事件处理器不抛出异常

### 配置检查

- [ ] 调用了 `AddCatga()`
- [ ] 配置了序列化（`UseMemoryPack()`）
- [ ] 配置了持久化（`UseInMemory()` / `UseRedis()` / `UseNats()`）
- [ ] 配置了传输（`AddInMemoryTransport()` / `AddRedisTransport()` / `AddNatsTransport()`）
- [ ] 调用了 `AddCatgaHandlers()`
- [ ] 配置顺序正确
- [ ] 没有重复配置
- [ ] 没有混用不同的后端

### 依赖管理检查

- [ ] 核心库不依赖具体实现
- [ ] 核心类型不添加序列化特性
- [ ] 序列化库使用反射处理核心类型
- [ ] 实现库依赖具体实现
- [ ] 用户代码添加序列化特性

### 测试检查

- [ ] 使用 Mock 而不是真实依赖
- [ ] 测试行为而不是实现细节
- [ ] 包含成功场景测试
- [ ] 包含失败场景测试
- [ ] 包含边界条件测试
- [ ] 验证返回结果
- [ ] 验证事件发布（如果适用）
- [ ] 使用有意义的测试名称

### 性能检查

- [ ] 使用 `ValueTask` 而不是 `Task`
- [ ] 避免不必要的异步操作
- [ ] 并行处理独立操作
- [ ] 在库代码中使用 `ConfigureAwait(false)`
- [ ] 避免在循环中发送命令
- [ ] 使用批处理（如果适用）

## 🚀 快速参考

### 常用命令

```bash
# 创建新项目
dotnet new webapi -n MyApp
cd MyApp

# 添加 Catga 包
dotnet add package Catga
dotnet add package Catga.Serialization.MemoryPack
dotnet add package Catga.Persistence.InMemory
dotnet add package Catga.Transport.InMemory

# 构建项目
dotnet build

# 运行项目
dotnet run

# 运行测试
dotnet test

# 发布 AOT
dotnet publish -c Release
```

### 常用配置

```csharp
// 开发环境
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseInMemory();
builder.Services.AddInMemoryTransport();
builder.Services.AddCatgaHandlers();

// 生产环境 - Redis
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseRedis("localhost:6379");
builder.Services.AddRedisTransport("localhost:6379");
builder.Services.AddCatgaHandlers();

// 生产环境 - NATS
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseNats();
builder.Services.AddNatsConnection("nats://localhost:4222");
builder.Services.AddNatsTransport("nats://localhost:4222");
builder.Services.AddCatgaHandlers();
```

### 常用接口

```csharp
// 发送命令/查询
var result = await mediator.SendAsync<TRequest, TResponse>(request, ct);

// 发布事件
await mediator.PublishAsync(evt, ct);

// 返回成功结果
return CatgaResult<T>.Success(value);

// 返回失败结果
return CatgaResult<T>.Failure("Error message");
```

## 📚 学习资源

### 示例项目

- `examples/OrderSystem/` - 完整的订单系统示例
  - CQRS 模式
  - 事件溯源
  - 多种后端配置
  - Web UI
  - API 测试

### 文档

- `docs/AI-GUIDE.md` - AI 开发指南
- `docs/architecture/` - 架构文档
- `docs/guides/` - 使用指南
- `docs/patterns/` - 设计模式

### 测试

- `tests/Catga.Tests/` - 单元测试示例
- `examples/OrderSystem/test-api.ps1` - API 测试脚本

## 🎓 总结

### 核心原则

1. **依赖管理**: 核心库只依赖抽象，具体实现在实现库
2. **类型设计**: 核心类型是纯 POCO，用户类型添加序列化特性
3. **不可变性**: 使用 record 类型，避免可变状态
4. **错误处理**: 返回 CatgaResult，不抛出异常
5. **异步优先**: 使用 ValueTask，传递 CancellationToken
6. **日志记录**: 记录关键操作和错误
7. **测试优先**: 编写全面的单元测试

### 最佳实践

1. ✅ 始终添加 `[MemoryPackable]` 和 `partial`（用户代码）
2. ✅ 始终包含 `MessageId` 属性
3. ✅ 使用 `sealed` 类
4. ✅ 捕获并处理异常
5. ✅ 传递 `CancellationToken`
6. ✅ 添加日志记录
7. ✅ 编写测试
8. ✅ 遵循命名约定

### 避免的错误

1. ❌ 在核心类型上添加序列化特性
2. ❌ 核心库依赖具体实现
3. ❌ 忘记 `MessageId` 属性
4. ❌ 使用可变类型
5. ❌ 抛出异常表示业务失败
6. ❌ 阻塞异步操作
7. ❌ 忽略 `CancellationToken`
8. ❌ 事件处理器抛出异常

---

**记住**: Catga 的设计理念是简单、高性能、类型安全。遵循这些原则和最佳实践，你将能够构建出高质量的 CQRS 应用程序。

如有疑问，请参考 `examples/OrderSystem/` 示例项目或查阅详细文档。
