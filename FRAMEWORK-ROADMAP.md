# Catga 框架路线图

> **当前版本**: v1.0.0
> **目标**: 企业级分布式 CQRS 框架
> **更新时间**: 2025-10-14

---

## 📋 当前状态

### ✅ 已完成核心功能

1. **CQRS 基础**
   - Command/Query/Event 模式
   - Mediator 模式 (本地 + 分布式)
   - Handler 自动注册 (Source Generator)
   - Pipeline Behaviors

2. **分布式能力**
   - NATS 消息传输
   - Redis 持久化
   - QoS 保证 (0/1/2)
   - 节点发现 (K8s)

3. **性能优化**
   - 100% AOT 兼容
   - 零反射设计
   - 高性能序列化 (MemoryPack)

4. **开发体验**
   - Roslyn 分析器
   - 可观测性 (OpenTelemetry)
   - ASP.NET Core 集成

---

## 🎯 核心缺失功能

### 1. Event Sourcing (事件溯源) - P0

**现状**: 只有简单的事件发布，缺少持久化和重放

**需要**:
- `IEventStore` 接口实现 (NATS JetStream / Redis Streams)
- `AggregateRoot` 增强 (事件重放、版本管理)
- `ISnapshotStore` 快照支持 (性能优化)
- 事件迁移工具 (版本升级)

**实现方式**:
```csharp
// 聚合根
public abstract class AggregateRoot<TId>
{
    public TId Id { get; protected set; }
    public long Version { get; protected set; }

    private readonly List<IEvent> _uncommittedEvents = new();

    protected void RaiseEvent(IEvent @event)
    {
        ApplyEvent(@event);
        _uncommittedEvents.Add(@event);
        Version++;
    }

    protected abstract void ApplyEvent(IEvent @event);

    public void LoadFromHistory(IEnumerable<IEvent> history)
    {
        foreach (var @event in history)
        {
            ApplyEvent(@event);
            Version++;
        }
    }

    public IReadOnlyList<IEvent> GetUncommittedEvents() => _uncommittedEvents;
    public void MarkEventsAsCommitted() => _uncommittedEvents.Clear();
}

// Event Store
public interface IEventStore
{
    Task AppendAsync(string streamId, IEvent @event, CancellationToken ct = default);
    Task AppendAsync(string streamId, IEnumerable<IEvent> events, long expectedVersion, CancellationToken ct = default);
    Task<IEnumerable<IEvent>> GetEventsAsync(string streamId, long fromVersion = 0, CancellationToken ct = default);
}

// 使用示例
public class Order : AggregateRoot<string>
{
    public OrderStatus Status { get; private set; }
    public List<OrderItem> Items { get; private set; } = new();

    // 命令处理
    public void Create(string orderId, List<OrderItem> items)
    {
        RaiseEvent(new OrderCreated(orderId, items, DateTime.UtcNow));
    }

    public void Ship(string trackingNumber)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException("订单未支付");

        RaiseEvent(new OrderShipped(Id, trackingNumber, DateTime.UtcNow));
    }

    // 事件应用
    protected override void ApplyEvent(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                Id = e.OrderId;
                Items = e.Items;
                Status = OrderStatus.Created;
                break;
            case OrderShipped e:
                Status = OrderStatus.Shipped;
                break;
        }
    }
}
```

---

### 2. Read Model Projection (读模型投影) - P0

**现状**: 只有 Query 接口，缺少从事件流构建读模型的机制

**需要**:
- `IProjection` 接口
- `IProjectionManager` 管理器 (启动、停止、重建)
- 投影状态跟踪 (checkpoint)
- 多个读模型支持

**实现方式**:
```csharp
// 投影接口
public interface IProjection
{
    string Name { get; }
    Task HandleAsync(IEvent @event, CancellationToken ct);
}

// 投影管理器
public interface IProjectionManager
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task RebuildAsync(string projectionName, CancellationToken ct);
    Task<ProjectionStatus> GetStatusAsync(string projectionName);
}

// 使用示例
public class OrderReadModelProjection : IProjection
{
    private readonly IOrderReadModelStore _store;

    public string Name => "OrderReadModel";

    public async Task HandleAsync(IEvent @event, CancellationToken ct)
    {
        switch (@event)
        {
            case OrderCreated e:
                await _store.SaveAsync(new OrderReadModel
                {
                    OrderId = e.OrderId,
                    Status = "Created",
                    CreatedAt = e.CreatedAt
                }, ct);
                break;

            case OrderShipped e:
                var order = await _store.GetAsync(e.OrderId, ct);
                if (order != null)
                {
                    order.Status = "Shipped";
                    await _store.SaveAsync(order, ct);
                }
                break;
        }
    }
}
```

---

### 3. Distributed Saga (分布式事务) - P1

**现状**: 缺少跨服务的长事务协调机制

**设计原则**:
- 不引入复杂的编排器
- 基于事件驱动
- 利用现有的 Event Sourcing
- 简单、透明、易调试

**实现方式**: **基于事件的自动补偿**

```csharp
// 补偿配置 (声明式)
public static class OrderSagaCompensation
{
    public static readonly Dictionary<Type, Type> Compensations = new()
    {
        [typeof(InventoryReserved)] = typeof(ReleaseInventory),
        [typeof(PaymentProcessed)] = typeof(RefundPayment),
        [typeof(ShipmentCreated)] = typeof(CancelShipment)
    };
}

// Saga 协调器 (框架提供)
public class SagaCoordinator
{
    private readonly ICatgaMediator _mediator;
    private readonly IEventStore _eventStore;

    public async Task<CatgaResult> ExecuteAsync(
        string sagaId,
        Func<Task> action,
        Dictionary<Type, Type> compensations)
    {
        try
        {
            // 执行业务逻辑
            await action();
            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            // 自动补偿
            await CompensateAsync(sagaId, compensations);
            return CatgaResult.Failure(ex.Message);
        }
    }

    private async Task CompensateAsync(string sagaId, Dictionary<Type, Type> compensations)
    {
        // 获取已发布的事件
        var events = await _eventStore.GetEventsAsync(sagaId);

        // 按相反顺序补偿
        foreach (var @event in events.Reverse())
        {
            if (compensations.TryGetValue(@event.GetType(), out var compensationType))
            {
                var compensation = Activator.CreateInstance(compensationType, sagaId);
                await _mediator.SendAsync(compensation);
            }
        }
    }
}

// 使用示例
public class OrderService
{
    private readonly SagaCoordinator _saga;
    private readonly ICatgaMediator _mediator;

    public async Task<CatgaResult> CreateOrderAsync(CreateOrderCommand cmd)
    {
        return await _saga.ExecuteAsync(
            sagaId: cmd.OrderId,
            action: async () =>
            {
                // 步骤 1: 预留库存
                await _mediator.SendAsync(new ReserveInventory(cmd.OrderId, cmd.Items));

                // 步骤 2: 处理支付
                await _mediator.SendAsync(new ProcessPayment(cmd.OrderId, cmd.Amount));

                // 步骤 3: 创建发货
                await _mediator.SendAsync(new CreateShipment(cmd.OrderId, cmd.Address));
            },
            compensations: OrderSagaCompensation.Compensations
        );
    }
}
```

---

### 4. Process Manager (流程管理器) - P2

**现状**: 缺少复杂业务流程的协调机制

**需要**:
- 流程状态管理
- 超时处理
- 人工审批支持
- 流程可视化

**实现方式**: **基于状态机 + Event Sourcing**

```csharp
// 流程状态
public abstract class ProcessState
{
    public string ProcessId { get; set; }
    public string CurrentStep { get; set; }
    public ProcessStatus Status { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}

// 流程管理器
public abstract class ProcessManager<TState> where TState : ProcessState, new()
{
    protected TState State { get; private set; } = new();

    protected abstract Task<bool> CanHandleAsync(IEvent @event);
    protected abstract Task HandleAsync(IEvent @event);

    protected async Task TransitionTo(string nextStep)
    {
        State.CurrentStep = nextStep;
        await SaveStateAsync();
    }

    protected abstract Task SaveStateAsync();
}

// 使用示例
public class OrderProcessManager : ProcessManager<OrderProcessState>
{
    protected override async Task<bool> CanHandleAsync(IEvent @event)
    {
        return @event is OrderCreated or InventoryReserved or PaymentProcessed;
    }

    protected override async Task HandleAsync(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                await TransitionTo("ReserveInventory");
                await SendCommand(new ReserveInventory(e.OrderId, e.Items));
                break;

            case InventoryReserved e:
                await TransitionTo("ProcessPayment");
                await SendCommand(new ProcessPayment(e.OrderId, State.Data["Amount"]));
                break;

            case PaymentProcessed e:
                await TransitionTo("CreateShipment");
                await SendCommand(new CreateShipment(e.OrderId, State.Data["Address"]));
                break;
        }
    }
}
```

---

## 🚀 实施计划

### Phase 1: Event Sourcing (2-3 周)

**Week 1-2**: 核心实现
- [ ] `AggregateRoot` 增强
- [ ] `IEventStore` 接口和实现 (NATS JetStream)
- [ ] `ISnapshotStore` 接口和实现 (Redis)
- [ ] 单元测试

**Week 3**: 集成和文档
- [ ] 与现有 Mediator 集成
- [ ] 示例项目 (订单系统)
- [ ] 文档和最佳实践

---

### Phase 2: Read Model Projection (1-2 周)

**Week 1**: 核心实现
- [ ] `IProjection` 接口
- [ ] `IProjectionManager` 实现
- [ ] Checkpoint 管理
- [ ] 单元测试

**Week 2**: 集成和文档
- [ ] 与 Event Store 集成
- [ ] 示例项目更新
- [ ] 文档

---

### Phase 3: Distributed Saga (2-3 周)

**Week 1-2**: 核心实现
- [ ] `SagaCoordinator` 实现
- [ ] 补偿机制
- [ ] 超时和重试
- [ ] 单元测试

**Week 3**: 集成和文档
- [ ] 示例项目更新
- [ ] 文档和最佳实践

---

### Phase 4: Process Manager (3-4 周)

**Week 1-2**: 核心实现
- [ ] `ProcessManager` 基类
- [ ] 状态管理
- [ ] 事件处理
- [ ] 单元测试

**Week 3-4**: 高级功能
- [ ] 超时处理
- [ ] 人工审批
- [ ] 流程可视化
- [ ] 文档

---

## 📊 优先级说明

- **P0 (必须)**: Event Sourcing, Read Model Projection
- **P1 (重要)**: Distributed Saga
- **P2 (可选)**: Process Manager

---

## 🎯 设计原则

1. **简单优先** - 不过度设计，保持框架简单易用
2. **性能优先** - 保持 AOT 兼容，零反射，高性能
3. **透明优先** - 代码清晰，易于调试，没有魔法
4. **实用优先** - 解决实际问题，不追求完美
5. **渐进增强** - 功能可选，不强制使用

---

## 📝 技术决策

### Event Store 选择
- **NATS JetStream** (推荐): 高性能，内置持久化，分布式
- **Redis Streams**: 简单，易部署，适合中小规模
- **EventStoreDB**: 专业 Event Sourcing，适合大规模

### Saga 实现方式
- **不使用编排器**: 避免复杂性
- **基于事件驱动**: 利用现有 Event Sourcing
- **声明式补偿**: 简单、清晰、易维护

### Process Manager 实现方式
- **基于状态机**: 清晰的状态转换
- **Event Sourcing**: 完整的审计日志
- **可选功能**: 不强制使用

---

## 🔧 Source Generator 优化 (渐进式)

### 原则
- ✅ **只生成重复代码** - 不改变用户代码结构
- ✅ **可选使用** - 用户可以手动编写
- ✅ **透明可见** - 生成的代码可以查看和调试
- ✅ **编译时错误** - 问题在编译时发现

---

### 1. Event Apply 方法生成 (减少 switch/case)

**问题**: 每个聚合根都需要写大量的 switch/case

**手动写法** (当前):
```csharp
public class Order : AggregateRoot<string>
{
    public OrderStatus Status { get; private set; }
    public List<OrderItem> Items { get; private set; }
    
    protected override void ApplyEvent(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                Id = e.OrderId;
                Items = e.Items;
                Status = OrderStatus.Created;
                break;
            case OrderPaid e:
                Status = OrderStatus.Paid;
                break;
            case OrderShipped e:
                Status = OrderStatus.Shipped;
                break;
            // ... 更多事件
        }
    }
}
```

**Source Generator 优化** (可选):
```csharp
// 用户只需要写具体的 Apply 方法
public partial class Order : AggregateRoot<string>
{
    public OrderStatus Status { get; private set; }
    public List<OrderItem> Items { get; private set; }
    
    // 标记方法，Source Generator 会自动生成 ApplyEvent
    private void Apply(OrderCreated e)
    {
        Id = e.OrderId;
        Items = e.Items;
        Status = OrderStatus.Created;
    }
    
    private void Apply(OrderPaid e)
    {
        Status = OrderStatus.Paid;
    }
    
    private void Apply(OrderShipped e)
    {
        Status = OrderStatus.Shipped;
    }
}

// Source Generator 自动生成 (在 Order.g.cs)
public partial class Order
{
    protected override void ApplyEvent(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                Apply(e);
                break;
            case OrderPaid e:
                Apply(e);
                break;
            case OrderShipped e:
                Apply(e);
                break;
            default:
                throw new InvalidOperationException($"Unknown event type: {@event.GetType().Name}");
        }
    }
}
```

**优势**:
- ✅ 减少重复的 switch/case
- ✅ 类型安全 (编译时检查)
- ✅ 易于调试 (Apply 方法可以打断点)
- ✅ 可选使用 (不用 partial 就手动写)

---

### 2. Projection 注册生成 (减少手动注册)

**问题**: 每个 Projection 都需要手动注册

**手动写法** (当前):
```csharp
// Startup.cs
services.AddProjection<OrderReadModelProjection>();
services.AddProjection<OrderStatisticsProjection>();
services.AddProjection<CustomerOrderHistoryProjection>();
// ... 更多 Projection
```

**Source Generator 优化** (可选):
```csharp
// 用户只需要标记 Projection
[Projection] // 👈 Source Generator 会自动发现
public class OrderReadModelProjection : IProjection
{
    public string Name => "OrderReadModel";
    
    public async Task HandleAsync(IEvent @event, CancellationToken ct)
    {
        // ... 投影逻辑
    }
}

// Source Generator 自动生成扩展方法 (在 ProjectionExtensions.g.cs)
public static class GeneratedProjectionExtensions
{
    public static IServiceCollection AddAllProjections(this IServiceCollection services)
    {
        services.AddProjection<OrderReadModelProjection>();
        services.AddProjection<OrderStatisticsProjection>();
        services.AddProjection<CustomerOrderHistoryProjection>();
        return services;
    }
}

// 使用
services.AddAllProjections(); // 👈 一行代码注册所有
```

**优势**:
- ✅ 减少手动注册代码
- ✅ 不会遗漏 Projection
- ✅ 编译时发现问题
- ✅ 可选使用 (可以手动注册)

---

### 3. Saga 补偿映射生成 (减少手动配置)

**问题**: 补偿映射需要手动维护

**手动写法** (当前):
```csharp
public static class OrderSagaCompensation
{
    public static readonly Dictionary<Type, Type> Compensations = new()
    {
        [typeof(InventoryReserved)] = typeof(ReleaseInventory),
        [typeof(PaymentProcessed)] = typeof(RefundPayment),
        [typeof(ShipmentCreated)] = typeof(CancelShipment)
    };
}
```

**Source Generator 优化** (可选):
```csharp
// 用户只需要标记补偿关系
[Compensate(typeof(ReleaseInventory))] // 👈 声明补偿命令
public record InventoryReserved(string OrderId, List<OrderItem> Items) : IEvent;

[Compensate(typeof(RefundPayment))]
public record PaymentProcessed(string OrderId, decimal Amount) : IEvent;

[Compensate(typeof(CancelShipment))]
public record ShipmentCreated(string OrderId, string TrackingNumber) : IEvent;

// Source Generator 自动生成补偿映射 (在 CompensationMap.g.cs)
public static class GeneratedCompensationMap
{
    public static readonly Dictionary<Type, Type> OrderSagaCompensations = new()
    {
        [typeof(InventoryReserved)] = typeof(ReleaseInventory),
        [typeof(PaymentProcessed)] = typeof(RefundPayment),
        [typeof(ShipmentCreated)] = typeof(CancelShipment)
    };
}

// 使用
await _saga.ExecuteAsync(
    sagaId: cmd.OrderId,
    action: async () => { /* ... */ },
    compensations: GeneratedCompensationMap.OrderSagaCompensations // 👈 自动生成
);
```

**优势**:
- ✅ 补偿关系就在事件定义旁边 (清晰)
- ✅ 减少手动维护
- ✅ 编译时检查补偿命令是否存在
- ✅ 可选使用 (可以手动配置)

---

### 4. Event Handler 路由生成 (减少反射)

**问题**: 当前使用反射查找 Handler，性能不佳

**手动写法** (当前):
```csharp
// 运行时反射查找 Handler
var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
var handlers = serviceProvider.GetServices(handlerType);
foreach (var handler in handlers)
{
    await ((dynamic)handler).HandleAsync((dynamic)@event, ct);
}
```

**Source Generator 优化** (自动):
```csharp
// Source Generator 自动生成静态路由表 (在 EventRouter.g.cs)
public static class GeneratedEventRouter
{
    private static readonly Dictionary<Type, Func<IServiceProvider, IEvent, CancellationToken, Task>> Routes = new()
    {
        [typeof(OrderCreated)] = async (sp, e, ct) =>
        {
            var handlers = sp.GetServices<IEventHandler<OrderCreated>>();
            foreach (var handler in handlers)
            {
                await handler.HandleAsync((OrderCreated)e, ct);
            }
        },
        [typeof(OrderPaid)] = async (sp, e, ct) =>
        {
            var handlers = sp.GetServices<IEventHandler<OrderPaid>>();
            foreach (var handler in handlers)
            {
                await handler.HandleAsync((OrderPaid)e, ct);
            }
        }
        // ... 自动生成所有事件类型
    };
    
    public static Task RouteAsync(IServiceProvider sp, IEvent @event, CancellationToken ct)
    {
        if (Routes.TryGetValue(@event.GetType(), out var route))
        {
            return route(sp, @event, ct);
        }
        throw new InvalidOperationException($"No handler for event: {@event.GetType().Name}");
    }
}

// 使用 (零反射)
await GeneratedEventRouter.RouteAsync(serviceProvider, @event, ct);
```

**优势**:
- ✅ 零反射 (编译时生成)
- ✅ 性能提升 (直接调用)
- ✅ AOT 友好
- ✅ 自动使用 (无需配置)

---

### 5. Aggregate 工厂生成 (减少反射创建)

**问题**: 从事件流恢复聚合根需要反射创建实例

**手动写法** (当前):
```csharp
public async Task<TAggregate> LoadAggregateAsync<TAggregate>(string id)
    where TAggregate : AggregateRoot, new()
{
    var aggregate = new TAggregate(); // 👈 需要 new() 约束
    var events = await _eventStore.GetEventsAsync(id);
    aggregate.LoadFromHistory(events);
    return aggregate;
}
```

**Source Generator 优化** (自动):
```csharp
// Source Generator 自动生成工厂 (在 AggregateFactory.g.cs)
public static class GeneratedAggregateFactory
{
    private static readonly Dictionary<Type, Func<object>> Factories = new()
    {
        [typeof(Order)] = () => new Order(),
        [typeof(Customer)] = () => new Customer(),
        [typeof(Product)] = () => new Product()
        // ... 自动生成所有聚合根
    };
    
    public static TAggregate Create<TAggregate>() where TAggregate : AggregateRoot
    {
        if (Factories.TryGetValue(typeof(TAggregate), out var factory))
        {
            return (TAggregate)factory();
        }
        throw new InvalidOperationException($"No factory for: {typeof(TAggregate).Name}");
    }
}

// 使用 (零反射，无需 new() 约束)
public async Task<TAggregate> LoadAggregateAsync<TAggregate>(string id)
    where TAggregate : AggregateRoot
{
    var aggregate = GeneratedAggregateFactory.Create<TAggregate>(); // 👈 零反射
    var events = await _eventStore.GetEventsAsync(id);
    aggregate.LoadFromHistory(events);
    return aggregate;
}
```

**优势**:
- ✅ 零反射 (编译时生成)
- ✅ 无需 new() 约束
- ✅ AOT 友好
- ✅ 自动使用 (无需配置)

---

## 📊 Source Generator 优先级

| 功能 | 优先级 | 价值 | 复杂度 |
|------|--------|------|--------|
| **Event Apply 生成** | P1 | 高 (减少大量 switch) | 低 |
| **Projection 注册生成** | P1 | 中 (减少手动注册) | 低 |
| **Event Router 生成** | P0 | 高 (性能提升) | 中 |
| **Aggregate 工厂生成** | P0 | 高 (AOT 友好) | 低 |
| **Saga 补偿映射生成** | P2 | 中 (减少配置) | 低 |

---

## 🎯 实施原则

1. **渐进式** - 一个一个功能添加，不一次性全部实现
2. **可选性** - 用户可以选择不使用 Source Generator
3. **透明性** - 生成的代码可以查看和调试
4. **简单性** - 只生成重复代码，不改变架构
5. **性能优先** - 优先实现性能相关的生成器 (Event Router, Aggregate Factory)

---

## 🔄 后续规划

### v1.1.0 (Q1 2026)
- Event Sourcing
- Read Model Projection
- **Source Generator**: Event Router, Aggregate Factory (P0)

### v1.2.0 (Q2 2026)
- Distributed Saga
- **Source Generator**: Event Apply, Projection 注册 (P1)
- 性能优化

### v1.3.0 (Q3 2026)
- Process Manager
- **Source Generator**: Saga 补偿映射 (P2)
- 可视化工具

### v2.0.0 (Q4 2026)
- 云原生增强
- 多语言支持

---

## 📚 参考资料

- [Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Saga Pattern](https://microservices.io/patterns/data/saga.html)
- [Process Manager Pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html)

