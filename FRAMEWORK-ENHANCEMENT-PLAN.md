# Catga 框架完善计划

> **当前状态**: v1.0.0 基础功能完整
> **目标**: 成为完整的企业级分布式 CQRS 框架
> **制定时间**: 2025-10-14

---

## 🎯 当前框架分析

### ✅ 已实现核心功能

1. **CQRS 基础**
   - ✅ Command/Query/Event 模式
   - ✅ Mediator 模式
   - ✅ Handler 自动注册 (Source Generator)
   - ✅ Pipeline Behaviors (Logging, Validation, Retry, Idempotency)

2. **分布式基础设施**
   - ✅ NATS 传输层
   - ✅ Redis 持久化 (Cache, Lock, Idempotency)
   - ✅ InMemory 实现 (开发/测试)
   - ✅ QoS 保证 (0/1/2)

3. **性能优化**
   - ✅ 100% AOT 兼容
   - ✅ 零反射设计
   - ✅ Snowflake ID 生成器
   - ✅ ArrayPool 优化

4. **开发体验**
   - ✅ Roslyn 分析器
   - ✅ ASP.NET Core 集成
   - ✅ 可观测性 (Tracing, Metrics, Logging)

---

## ❌ 缺失的关键功能

### 1. **事件溯源 (Event Sourcing)** ⭐⭐⭐⭐⭐

**问题**:
- 当前只有简单的 Event 发布
- 缺少事件存储和重放机制
- 无法实现完整的 CQRS/ES 模式

**需要实现**:
```csharp
// 聚合根基类 (已有但功能不完整)
public abstract class AggregateRoot<TId>
{
    public TId Id { get; protected set; }
    public long Version { get; protected set; }
    private readonly List<IEvent> _uncommittedEvents = new();

    // 需要增强
    protected void RaiseEvent(IEvent @event);
    public IReadOnlyList<IEvent> GetUncommittedEvents();
    public void MarkEventsAsCommitted();
    public void LoadFromHistory(IEnumerable<IEvent> history);
}

// 事件存储接口 (已有但未实现)
public interface IEventStore
{
    Task SaveEventsAsync<TAggregate>(TAggregate aggregate, CancellationToken ct);
    Task<TAggregate> LoadAggregateAsync<TAggregate>(string id, CancellationToken ct);
    Task<IEnumerable<IEvent>> GetEventsAsync(string aggregateId, long fromVersion, CancellationToken ct);
}

// 快照支持
public interface ISnapshotStore
{
    Task SaveSnapshotAsync<TAggregate>(TAggregate aggregate, CancellationToken ct);
    Task<TAggregate?> LoadSnapshotAsync<TAggregate>(string id, CancellationToken ct);
}
```

**优先级**: P0 (核心功能)

---

### 2. **分布式工作流 (Distributed Workflow)** ⭐⭐⭐⭐⭐

**设计理念**: 
- ❌ **不使用传统 Saga** - 需要中心化编排器，复杂且难以扩展
- ✅ **事件驱动工作流** - 完全去中心化，自动补偿，零编排
- ✅ **声明式定义** - 像写代码一样定义工作流
- ✅ **自动重试和幂等** - 利用现有的 Catga 能力

**核心优势**:
1. **零编排器** - 无需中心化的 Saga 引擎
2. **事件溯源** - 工作流状态完全由事件决定
3. **自动补偿** - 通过事件自动触发补偿逻辑
4. **完全异步** - 天然支持高并发
5. **易于测试** - 每个步骤都是独立的 Handler

**需要实现**:
```csharp
// 工作流定义 (声明式)
public class OrderWorkflow : IWorkflow
{
    // 1. 定义工作流步骤 (通过事件链)
    public static void Configure(IWorkflowBuilder builder)
    {
        builder
            // 步骤 1: 订单创建 → 触发库存预留
            .When<OrderCreated>()
                .Then<ReserveInventoryCommand>((evt, cmd) => 
                {
                    cmd.OrderId = evt.OrderId;
                    cmd.Items = evt.Items;
                })
                .OnFailure<ReleaseInventoryCommand>() // 自动补偿
                
            // 步骤 2: 库存预留成功 → 触发支付
            .When<InventoryReserved>()
                .Then<ProcessPaymentCommand>((evt, cmd) =>
                {
                    cmd.OrderId = evt.OrderId;
                    cmd.Amount = evt.TotalAmount;
                })
                .OnFailure<RefundPaymentCommand>() // 自动补偿
                
            // 步骤 3: 支付成功 → 触发发货
            .When<PaymentProcessed>()
                .Then<CreateShipmentCommand>((evt, cmd) =>
                {
                    cmd.OrderId = evt.OrderId;
                    cmd.Address = evt.ShippingAddress;
                })
                .OnFailure<CancelShipmentCommand>() // 自动补偿
                
            // 步骤 4: 发货成功 → 订单完成
            .When<ShipmentCreated>()
                .Then<CompleteOrderCommand>((evt, cmd) =>
                {
                    cmd.OrderId = evt.OrderId;
                });
    }
}

// 工作流构建器
public interface IWorkflowBuilder
{
    IWorkflowStepBuilder<TEvent> When<TEvent>() where TEvent : IEvent;
}

public interface IWorkflowStepBuilder<TEvent> where TEvent : IEvent
{
    // 成功路径
    IWorkflowStepBuilder<TEvent> Then<TCommand>(
        Action<TEvent, TCommand> configure) 
        where TCommand : ICommand, new();
    
    // 并行执行
    IWorkflowStepBuilder<TEvent> ThenAll(
        params Action<IWorkflowStepBuilder<TEvent>>[] steps);
    
    // 条件分支
    IWorkflowStepBuilder<TEvent> ThenIf(
        Func<TEvent, bool> condition,
        Action<IWorkflowStepBuilder<TEvent>> ifTrue,
        Action<IWorkflowStepBuilder<TEvent>> ifFalse = null);
    
    // 失败补偿
    IWorkflowStepBuilder<TEvent> OnFailure<TCompensateCommand>(
        Action<TEvent, TCompensateCommand> configure = null)
        where TCompensateCommand : ICommand, new();
    
    // 超时处理
    IWorkflowStepBuilder<TEvent> WithTimeout(
        TimeSpan timeout,
        Action<IWorkflowStepBuilder<TEvent>> onTimeout);
}

// 工作流状态追踪 (通过事件溯源)
public class WorkflowState
{
    public string WorkflowId { get; set; }
    public string CurrentStep { get; set; }
    public WorkflowStatus Status { get; set; }
    public Dictionary<string, object> Data { get; set; }
    public List<WorkflowEvent> History { get; set; }
    
    // 从事件流重建状态
    public static WorkflowState FromEvents(IEnumerable<IEvent> events)
    {
        var state = new WorkflowState();
        foreach (var @event in events)
        {
            state.Apply(@event);
        }
        return state;
    }
}

// 实际使用示例
public class OrderService
{
    private readonly ICatgaMediator _mediator;
    
    public async Task<CatgaResult<OrderCreated>> CreateOrderAsync(
        CreateOrderCommand command,
        CancellationToken ct)
    {
        // 1. 创建订单 (发布事件)
        var orderCreated = new OrderCreated
        {
            OrderId = command.OrderId,
            Items = command.Items,
            TotalAmount = command.Amount
        };
        
        // 2. 发布事件 → 自动触发工作流
        await _mediator.PublishAsync(orderCreated, ct);
        
        // 3. 工作流自动执行:
        //    - OrderCreated → ReserveInventoryCommand
        //    - InventoryReserved → ProcessPaymentCommand
        //    - PaymentProcessed → CreateShipmentCommand
        //    - ShipmentCreated → CompleteOrderCommand
        
        return CatgaResult<OrderCreated>.Success(orderCreated);
    }
}

// 补偿自动触发 (通过事件)
public class InventoryReservationFailedHandler 
    : IEventHandler<InventoryReservationFailed>
{
    private readonly ICatgaMediator _mediator;
    
    public async ValueTask HandleAsync(
        InventoryReservationFailed @event,
        CancellationToken ct)
    {
        // 自动触发补偿命令
        await _mediator.SendAsync(new CancelOrderCommand
        {
            OrderId = @event.OrderId,
            Reason = "库存不足"
        }, ct);
    }
}
```

**与传统 Saga 对比**:

| 特性 | 传统 Saga | Catga 工作流 |
|------|----------|-------------|
| **编排方式** | 中心化编排器 | 事件驱动，去中心化 |
| **状态管理** | 需要 SagaStore | 事件溯源自动管理 |
| **补偿逻辑** | 手动定义补偿步骤 | 事件自动触发补偿 |
| **并发支持** | 复杂 | 天然支持 |
| **测试难度** | 需要 Mock 编排器 | 每个步骤独立测试 |
| **扩展性** | 编排器瓶颈 | 无限扩展 |
| **代码复杂度** | 高 (状态机) | 低 (声明式) |

**实现优势**:
1. ✅ **零额外组件** - 完全基于现有 CQRS/Event
2. ✅ **自动重试** - 利用 Catga 的 QoS 和 Retry
3. ✅ **自动幂等** - 利用 Catga 的 Idempotency
4. ✅ **完整追踪** - 事件溯源提供完整历史
5. ✅ **易于理解** - 像写普通代码一样

**优先级**: P0 (核心功能，但实现更简单)

---

### 3. **读模型投影 (Read Model Projection)** ⭐⭐⭐⭐

**问题**:
- 当前只有 Query 接口
- 缺少从事件流构建读模型的机制
- 无法实现 CQRS 的读写分离

**需要实现**:
```csharp
// 投影基类
public abstract class Projection<TReadModel> where TReadModel : class
{
    protected abstract Task HandleAsync(IEvent @event, CancellationToken ct);
    protected abstract Task<TReadModel?> GetAsync(string id, CancellationToken ct);
    protected abstract Task SaveAsync(TReadModel model, CancellationToken ct);
}

// 投影管理器
public interface IProjectionManager
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task RebuildAsync<TProjection>(CancellationToken ct) where TProjection : IProjection;
    Task<ProjectionStatus> GetStatusAsync<TProjection>() where TProjection : IProjection;
}

// 示例: 订单读模型投影
public class OrderReadModelProjection : Projection<OrderReadModel>
{
    private readonly IOrderReadModelStore _store;

    protected override async Task HandleAsync(IEvent @event, CancellationToken ct)
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
                    order.ShippedAt = e.ShippedAt;
                    await _store.SaveAsync(order, ct);
                }
                break;
        }
    }
}
```

**优先级**: P0 (核心功能)

---

### 4. **流处理 (Stream Processing)** ⭐⭐⭐⭐

**问题**:
- 当前事件处理是单个处理
- 缺少流式处理能力
- 无法处理高吞吐量场景

**需要实现**:
```csharp
// 流处理器
public interface IStreamProcessor<TEvent> where TEvent : IEvent
{
    IAsyncEnumerable<TEvent> ProcessAsync(
        IAsyncEnumerable<TEvent> events,
        CancellationToken ct);
}

// 流操作符
public static class StreamExtensions
{
    public static IAsyncEnumerable<TEvent> Buffer<TEvent>(
        this IAsyncEnumerable<TEvent> source,
        int count,
        TimeSpan window);

    public static IAsyncEnumerable<TResult> Select<TEvent, TResult>(
        this IAsyncEnumerable<TEvent> source,
        Func<TEvent, TResult> selector);

    public static IAsyncEnumerable<TEvent> Where<TEvent>(
        this IAsyncEnumerable<TEvent> source,
        Func<TEvent, bool> predicate);

    public static IAsyncEnumerable<IGrouping<TKey, TEvent>> GroupBy<TEvent, TKey>(
        this IAsyncEnumerable<TEvent> source,
        Func<TEvent, TKey> keySelector);
}

// 示例: 实时统计
public class OrderStatisticsProcessor : IStreamProcessor<OrderCreated>
{
    public async IAsyncEnumerable<OrderCreated> ProcessAsync(
        IAsyncEnumerable<OrderCreated> events,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var batch in events.Buffer(100, TimeSpan.FromSeconds(5)))
        {
            var totalAmount = batch.Sum(e => e.Amount);
            var avgAmount = batch.Average(e => e.Amount);

            await _metricsService.RecordAsync(new OrderMetrics
            {
                Count = batch.Count,
                TotalAmount = totalAmount,
                AverageAmount = avgAmount
            }, ct);

            foreach (var @event in batch)
            {
                yield return @event;
            }
        }
    }
}
```

**优先级**: P1 (重要功能)

---

### 5. **策略模式增强** ⭐⭐⭐

**问题**:
- 当前 Retry 策略比较简单
- 缺少断路器 (Circuit Breaker)
- 缺少限流 (Rate Limiting)
- 缺少超时控制

**需要实现**:
```csharp
// 断路器
public interface ICircuitBreaker
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CircuitBreakerOptions options,
        CancellationToken ct);
}

// 限流器
public interface IRateLimiter
{
    Task<bool> TryAcquireAsync(string key, CancellationToken ct);
    Task<RateLimitStatus> GetStatusAsync(string key, CancellationToken ct);
}

// 超时策略
public class TimeoutBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var timeout = GetTimeout(request);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await next().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Request timed out after {timeout}");
        }
    }
}
```

**优先级**: P1 (重要功能)

---

### 6. **消息路由增强** ⭐⭐⭐

**问题**:
- 当前路由比较简单
- 缺少内容路由 (Content-Based Routing)
- 缺少消息过滤
- 缺少消息转换

**需要实现**:
```csharp
// 路由规则
public interface IRoutingRule<TMessage>
{
    bool Match(TMessage message);
    string GetDestination(TMessage message);
}

// 消息过滤器
public interface IMessageFilter<TMessage>
{
    bool ShouldProcess(TMessage message);
}

// 消息转换器
public interface IMessageTransformer<TIn, TOut>
{
    TOut Transform(TIn message);
}

// 路由配置
services.AddCatga()
    .ConfigureRouting(routing =>
    {
        routing.ForMessage<OrderCreated>()
            .When(msg => msg.Amount > 1000)
            .RouteTo("high-value-orders")
            .Transform<OrderCreated, HighValueOrderNotification>()
            .Filter(msg => msg.CustomerId != null);

        routing.ForMessage<OrderCreated>()
            .When(msg => msg.Amount <= 1000)
            .RouteTo("standard-orders");
    });
```

**优先级**: P2 (增强功能)

---

### 7. **测试工具** ⭐⭐⭐

**问题**:
- 缺少测试辅助工具
- 缺少 Mock 支持
- 缺少集成测试框架

**需要实现**:
```csharp
// 测试 Mediator
public class TestMediator : ICatgaMediator
{
    private readonly List<object> _publishedMessages = new();

    public IReadOnlyList<object> PublishedMessages => _publishedMessages;

    public void VerifyPublished<TMessage>(Action<TMessage> assert)
    {
        var messages = _publishedMessages.OfType<TMessage>().ToList();
        messages.Should().NotBeEmpty();
        foreach (var msg in messages)
        {
            assert(msg);
        }
    }
}

// 测试构建器
public class CatgaTestBuilder
{
    public CatgaTestBuilder WithHandler<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler);

    public CatgaTestBuilder WithBehavior<TRequest, TResponse>(
        IPipelineBehavior<TRequest, TResponse> behavior);

    public CatgaTestBuilder WithMockTransport();

    public ICatgaMediator Build();
}

// 使用示例
[Fact]
public async Task CreateOrder_ShouldPublishOrderCreatedEvent()
{
    // Arrange
    var mediator = new CatgaTestBuilder()
        .WithHandler(new CreateOrderHandler())
        .WithMockTransport()
        .Build();

    // Act
    await mediator.SendAsync(new CreateOrder("ORD-001", 99.99m));

    // Assert
    mediator.VerifyPublished<OrderCreated>(e =>
    {
        e.OrderId.Should().Be("ORD-001");
        e.Amount.Should().Be(99.99m);
    });
}
```

**优先级**: P2 (增强功能)

---

### 8. **监控和诊断增强** ⭐⭐⭐

**问题**:
- 当前只有基础的 Tracing/Metrics
- 缺少健康检查详情
- 缺少性能分析工具

**需要实现**:
```csharp
// 健康检查详情
public class CatgaHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct)
    {
        var data = new Dictionary<string, object>
        {
            ["transport"] = await CheckTransportAsync(ct),
            ["eventStore"] = await CheckEventStoreAsync(ct),
            ["sagaStore"] = await CheckSagaStoreAsync(ct),
            ["messageQueue"] = await CheckMessageQueueAsync(ct)
        };

        return HealthCheckResult.Healthy("All systems operational", data);
    }
}

// 性能分析
public interface IPerformanceAnalyzer
{
    Task<PerformanceReport> AnalyzeAsync(TimeSpan period, CancellationToken ct);
}

public class PerformanceReport
{
    public Dictionary<string, CommandStats> CommandStats { get; set; }
    public Dictionary<string, EventStats> EventStats { get; set; }
    public List<SlowQuery> SlowQueries { get; set; }
    public List<FailedMessage> FailedMessages { get; set; }
}
```

**优先级**: P2 (增强功能)

---

## 📋 实施计划

### Phase 1: 核心功能完善 (P0) - 预计 2-3 周

1. **事件溯源 (Event Sourcing)**
   - [ ] 增强 `AggregateRoot` 基类
   - [ ] 实现 `IEventStore` (InMemory + Redis)
   - [ ] 实现 `ISnapshotStore`
   - [ ] 添加事件重放机制
   - [ ] 编写完整文档和示例

2. **分布式工作流**
   - [ ] 设计 `IWorkflowBuilder` API (声明式)
   - [ ] 实现事件 → 命令自动触发
   - [ ] 实现自动补偿机制 (基于事件)
   - [ ] 实现工作流状态追踪 (基于事件溯源)
   - [ ] 实现并行步骤和条件分支
   - [ ] 编写完整文档和示例 (对比传统 Saga)

3. **读模型投影**
   - [ ] 设计 Projection API
   - [ ] 实现 `IProjectionManager`
   - [ ] 实现投影重建
   - [ ] 实现投影状态追踪
   - [ ] 编写完整文档和示例

### Phase 2: 重要功能增强 (P1) - 预计 2 周

4. **流处理**
   - [ ] 实现 `IStreamProcessor`
   - [ ] 实现流操作符 (Buffer, Select, Where, GroupBy)
   - [ ] 实现背压控制
   - [ ] 编写文档和示例

5. **策略模式增强**
   - [ ] 实现断路器 (Circuit Breaker)
   - [ ] 实现限流器 (Rate Limiter)
   - [ ] 实现超时控制
   - [ ] 集成到 Pipeline Behaviors
   - [ ] 编写文档和示例

### Phase 3: 增强功能 (P2) - 预计 1-2 周

6. **消息路由增强**
   - [ ] 实现内容路由
   - [ ] 实现消息过滤
   - [ ] 实现消息转换
   - [ ] 编写文档和示例

7. **测试工具**
   - [ ] 实现 `TestMediator`
   - [ ] 实现 `CatgaTestBuilder`
   - [ ] 创建 `Catga.Testing` NuGet 包
   - [ ] 编写测试指南

8. **监控和诊断增强**
   - [ ] 增强健康检查
   - [ ] 实现性能分析器
   - [ ] 创建诊断仪表板
   - [ ] 编写运维指南

---

## 🎯 成功标准

### 功能完整性
- ✅ 支持完整的 CQRS/ES 模式
- ✅ 支持 Saga 编排
- ✅ 支持读写分离
- ✅ 支持流处理
- ✅ 完善的弹性策略

### 性能指标
- ✅ Event Sourcing 写入 < 5ms
- ✅ Saga 步骤执行 < 10ms
- ✅ 投影延迟 < 100ms
- ✅ 流处理吞吐量 > 10K msg/s

### 开发体验
- ✅ 完整的文档
- ✅ 丰富的示例
- ✅ 测试工具支持
- ✅ 良好的错误提示

---

## 📚 参考资料

### 类似框架
- **Axon Framework** (Java) - Event Sourcing + CQRS
- **EventStore** - Event Sourcing 数据库
- **NServiceBus** (.NET) - 企业服务总线
- **MassTransit** (.NET) - 分布式应用框架
- **Akka.NET** - Actor 模型框架

### 设计模式
- Event Sourcing Pattern
- Saga Pattern
- CQRS Pattern
- Projection Pattern
- Stream Processing Pattern

---

## 🚀 下一步行动

1. **立即开始**: Phase 1 - 事件溯源实现
2. **创建分支**: `feature/event-sourcing`
3. **设计 API**: 先设计接口，再实现
4. **编写测试**: TDD 方式开发
5. **文档同步**: 边开发边写文档

---

<div align="center">

**🎉 让 Catga 成为 .NET 最强的分布式 CQRS 框架！**

[返回主页](./README.md) · [开始实施](#-实施计划)

</div>

