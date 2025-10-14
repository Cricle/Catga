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

## 🔄 后续规划

### v1.1.0 (Q1 2026)
- Event Sourcing
- Read Model Projection

### v1.2.0 (Q2 2026)
- Distributed Saga
- 性能优化

### v1.3.0 (Q3 2026)
- Process Manager
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

