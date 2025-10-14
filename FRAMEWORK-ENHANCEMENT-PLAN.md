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

### 2. **分布式流程 (Distributed Process)** ⭐⭐⭐⭐⭐

**设计理念**: 
- ❌ **不使用传统 Saga** - 编排器复杂，状态机难维护
- ✅ **像写普通代码一样** - 用 async/await 写分布式流程
- ✅ **自动持久化** - 每步自动保存，断电可恢复
- ✅ **零配置** - 不需要定义状态机或补偿逻辑
- ✅ **极致性能** - 零反射，零分配，AOT 友好

**核心优势**:
1. **写法像本地代码** - 用熟悉的 C# 语法
2. **自动容错** - 自动重试、自动补偿、自动恢复
3. **完整追踪** - 每步都有日志和指标
4. **易于调试** - 可以单步调试分布式流程
5. **高性能** - < 1ms 步骤切换

**需要实现**:
```csharp
// 方式 1: 像写本地代码一样 (推荐) ⭐⭐⭐⭐⭐
public class OrderProcess : CatgaProcess<OrderData>
{
    // 就像写普通的 async 方法！
    protected override async Task ExecuteAsync(OrderData data, CancellationToken ct)
    {
        // 步骤 1: 预留库存
        var inventory = await Step("预留库存", async () =>
        {
            var result = await SendAsync(new ReserveInventory(data.OrderId, data.Items));
            return result.Value;
        });
        
        // 步骤 2: 处理支付
        var payment = await Step("处理支付", async () =>
        {
            var result = await SendAsync(new ProcessPayment(data.OrderId, data.Amount));
            return result.Value;
        });
        
        // 步骤 3: 创建发货
        var shipment = await Step("创建发货", async () =>
        {
            var result = await SendAsync(new CreateShipment(data.OrderId, data.Address));
            return result.Value;
        });
        
        // 步骤 4: 完成订单
        await Step("完成订单", async () =>
        {
            await SendAsync(new CompleteOrder(data.OrderId));
        });
        
        // 就这么简单！自动持久化、自动重试、自动补偿
    }
    
    // 可选: 自定义补偿逻辑
    protected override async Task CompensateAsync(string failedStep, OrderData data, CancellationToken ct)
    {
        // 自动按相反顺序执行补偿
        switch (failedStep)
        {
            case "创建发货":
                await SendAsync(new CancelShipment(data.OrderId));
                goto case "处理支付";
            case "处理支付":
                await SendAsync(new RefundPayment(data.OrderId));
                goto case "预留库存";
            case "预留库存":
                await SendAsync(new ReleaseInventory(data.OrderId));
                break;
        }
    }
}

// 使用超级简单
public class OrderService
{
    private readonly IProcessExecutor _executor;
    
    public async Task<CatgaResult> CreateOrderAsync(CreateOrderCommand cmd)
    {
        // 1. 创建流程数据
        var data = new OrderData
        {
            OrderId = cmd.OrderId,
            Items = cmd.Items,
            Amount = cmd.Amount,
            Address = cmd.Address
        };
        
        // 2. 执行流程 (一行代码！)
        return await _executor.ExecuteAsync<OrderProcess>(data);
        
        // 自动处理:
        // - 每步自动保存状态
        // - 失败自动重试
        // - 超时自动补偿
        // - 断电自动恢复
    }
}

// 方式 2: 声明式 (更灵活) ⭐⭐⭐⭐
public class OrderProcess : CatgaProcess<OrderData>
{
    protected override void Configure(IProcessBuilder<OrderData> builder)
    {
        builder
            .Step("预留库存")
                .Do(async data => await SendAsync(new ReserveInventory(data.OrderId, data.Items)))
                .OnFailure(async data => await SendAsync(new ReleaseInventory(data.OrderId)))
                .WithRetry(3, TimeSpan.FromSeconds(1))
                .WithTimeout(TimeSpan.FromSeconds(30))
                
            .Step("处理支付")
                .Do(async data => await SendAsync(new ProcessPayment(data.OrderId, data.Amount)))
                .OnFailure(async data => await SendAsync(new RefundPayment(data.OrderId)))
                .WithRetry(5, TimeSpan.FromSeconds(2))
                
            .Step("创建发货")
                .Do(async data => await SendAsync(new CreateShipment(data.OrderId, data.Address)))
                .OnFailure(async data => await SendAsync(new CancelShipment(data.OrderId)))
                
            .Step("完成订单")
                .Do(async data => await SendAsync(new CompleteOrder(data.OrderId)));
    }
}

// 方式 3: 并行步骤 (高性能) ⭐⭐⭐⭐⭐
public class OrderProcess : CatgaProcess<OrderData>
{
    protected override async Task ExecuteAsync(OrderData data, CancellationToken ct)
    {
        // 步骤 1: 预留库存
        await Step("预留库存", async () =>
        {
            await SendAsync(new ReserveInventory(data.OrderId, data.Items));
        });
        
        // 步骤 2 和 3: 并行执行 (性能提升 50%)
        await StepAll("支付和通知", 
            async () => await SendAsync(new ProcessPayment(data.OrderId, data.Amount)),
            async () => await SendAsync(new SendNotification(data.CustomerId, "订单处理中"))
        );
        
        // 步骤 4: 条件分支
        if (data.Amount > 1000)
        {
            await Step("VIP处理", async () =>
            {
                await SendAsync(new ApplyVIPDiscount(data.OrderId));
            });
        }
        
        // 步骤 5: 创建发货
        await Step("创建发货", async () =>
        {
            await SendAsync(new CreateShipment(data.OrderId, data.Address));
        });
    }
}

// 流程监控和恢复
public class ProcessMonitor
{
    private readonly IProcessStore _store;
    
    // 查看所有运行中的流程
    public async Task<List<ProcessStatus>> GetRunningProcessesAsync()
    {
        return await _store.GetByStatusAsync(ProcessState.Running);
    }
    
    // 恢复失败的流程
    public async Task RecoverFailedProcessAsync(string processId)
    {
        var process = await _store.LoadAsync(processId);
        await process.ResumeAsync(); // 从上次失败的步骤继续
    }
    
    // 取消流程 (自动执行补偿)
    public async Task CancelProcessAsync(string processId)
    {
        var process = await _store.LoadAsync(processId);
        await process.CancelAsync(); // 自动补偿已完成的步骤
    }
}
```

**与传统 Saga 对比**:

| 特性 | 传统 Saga | Catga Process |
|------|----------|---------------|
| **写法** | 状态机定义 | 像写本地代码 |
| **学习曲线** | 陡峭 | 平缓 (就是 async/await) |
| **代码行数** | 200+ 行 | 30 行 |
| **调试** | 困难 (状态机) | 简单 (单步调试) |
| **性能** | 中等 | 极致 (< 1ms 切换) |
| **补偿** | 手动定义 | 自动或简单定义 |
| **恢复** | 复杂 | 自动 |
| **并发** | 复杂 | 一行代码 `StepAll()` |
| **测试** | 需要 Mock 引擎 | 普通单元测试 |

**实现优势**:
1. ✅ **极致友好** - 像写本地代码，零学习成本
2. ✅ **极致性能** - 零反射，零分配，< 1ms 步骤切换
3. ✅ **极致方便** - 自动持久化、重试、补偿、恢复
4. ✅ **完整追踪** - 每步都有日志、指标、分布式追踪
5. ✅ **易于调试** - 可以单步调试分布式流程
6. ✅ **AOT 友好** - 100% Native AOT 支持

**性能指标**:
- 步骤切换: < 1ms
- 状态持久化: < 2ms
- 并发步骤: 50% 性能提升
- 内存占用: < 1KB per process
- 吞吐量: > 10K processes/s

**优先级**: P0 (核心功能，用户最需要)

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

2. **分布式流程 (CatgaProcess)**
   - [ ] 实现 `CatgaProcess<TData>` 基类
   - [ ] 实现 `Step()` 方法 (自动持久化)
   - [ ] 实现 `StepAll()` 方法 (并行执行)
   - [ ] 实现自动补偿机制
   - [ ] 实现 `IProcessStore` (InMemory + Redis)
   - [ ] 实现 `IProcessExecutor` (执行引擎)
   - [ ] 实现流程恢复和取消
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

