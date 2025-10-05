# Catga 分布式框架架构

## 🎯 框架定位

**Catga** 是一个完整的**分布式应用框架**，不仅仅是一个 CQRS 库。它提供了构建分布式系统所需的全套基础设施：

```
┌─────────────────────────────────────────────────────────────┐
│                  Catga 分布式框架                            │
├─────────────────────────────────────────────────────────────┤
│  应用层                                                      │
│  ├─ CQRS 模式      (命令查询分离)                           │
│  ├─ Event Sourcing (事件溯源)                               │
│  └─ Saga 模式      (分布式事务)                             │
├─────────────────────────────────────────────────────────────┤
│  通信层                                                      │
│  ├─ 本地通信       (进程内消息总线)                         │
│  ├─ NATS 传输      (分布式消息队列)                         │
│  └─ 可扩展传输     (RabbitMQ, Kafka 等)                     │
├─────────────────────────────────────────────────────────────┤
│  持久化层                                                    │
│  ├─ Redis 存储     (状态、幂等性)                           │
│  ├─ 事件存储       (事件溯源)                               │
│  └─ 可扩展存储     (PostgreSQL, MongoDB 等)                 │
├─────────────────────────────────────────────────────────────┤
│  弹性层                                                      │
│  ├─ 熔断器         (Circuit Breaker)                        │
│  ├─ 重试机制       (Retry with Polly)                       │
│  ├─ 限流控制       (Rate Limiting)                          │
│  ├─ 并发控制       (Concurrency Limiting)                   │
│  └─ 死信队列       (Dead Letter Queue)                      │
├─────────────────────────────────────────────────────────────┤
│  可观测层                                                    │
│  ├─ 分布式追踪     (OpenTelemetry)                          │
│  ├─ 结构化日志     (Logging)                                │
│  └─ 指标收集       (Metrics)                                │
├─────────────────────────────────────────────────────────────┤
│  基础设施                                                    │
│  ├─ AOT 支持       (NativeAOT 兼容)                         │
│  ├─ 高性能         (零分配设计)                             │
│  └─ 类型安全       (强类型 API)                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 🏗️ 核心架构

### 1. 分布式消息总线 (Message Bus)

Catga 的核心是一个**分布式消息总线**，支持：

#### 本地模式 (Monolithic)
```csharp
services.AddCatga(); // 默认本地通信

// 单体应用内的消息传递
await mediator.SendAsync(new CreateOrderCommand(...));
await mediator.PublishAsync(new OrderCreatedEvent(...));
```

#### 分布式模式 (Distributed)
```csharp
// NATS 分布式通信
services.AddNatsCatga("nats://cluster");

// 跨服务通信
// Service A
await mediator.SendAsync(new ProcessPaymentCommand(...)); // → Service B

// Service B
public class ProcessPaymentHandler : IRequestHandler<...> { }
```

---

### 2. CQRS 架构层

#### 命令 (Commands) - 写操作
```csharp
public record CreateOrderCommand(string CustomerId, List<OrderItem> Items)
    : ICommand<OrderResult>;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async Task<CatgaResult<OrderResult>> HandleAsync(...)
    {
        // 写入数据库
        // 发布领域事件
        // 返回结果
    }
}
```

#### 查询 (Queries) - 读操作
```csharp
public record GetOrderQuery(string OrderId) : IQuery<OrderDto>;

public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public async Task<CatgaResult<OrderDto>> HandleAsync(...)
    {
        // 从读模型查询
        // 无副作用
    }
}
```

#### 事件 (Events) - 异步通知
```csharp
public record OrderCreatedEvent(string OrderId, decimal Amount) : IEvent;

// 多个订阅者
public class SendEmailHandler : IEventHandler<OrderCreatedEvent> { }
public class UpdateInventoryHandler : IEventHandler<OrderCreatedEvent> { }
public class SendNotificationHandler : IEventHandler<OrderCreatedEvent> { }
```

---

### 3. 分布式事务 (Saga/CatGa)

**CatGa** 是 Catga 框架的分布式事务协调器：

```csharp
// 定义 Saga 工作流
public class OrderSaga : ICatGaTransaction<OrderSagaData, OrderResult>
{
    public async Task<OrderResult> ExecuteAsync(OrderSagaData data)
    {
        // Step 1: 处理支付
        await PaymentService.ProcessAsync(data.PaymentInfo);

        // Step 2: 扣减库存
        await InventoryService.ReserveAsync(data.Items);

        // Step 3: 创建订单
        return await OrderService.CreateAsync(data);
    }

    public async Task CompensateAsync(OrderSagaData data)
    {
        // 补偿操作（回滚）
        await PaymentService.RefundAsync(data.PaymentId);
        await InventoryService.ReleaseAsync(data.Items);
    }
}

// 执行分布式事务
var result = await sagaExecutor.ExecuteAsync(
    transactionId: "order-123",
    data: orderData,
    saga: new OrderSaga()
);
```

**特性**:
- ✅ 自动补偿（失败时回滚）
- ✅ 状态持久化（Redis/数据库）
- ✅ 重试机制
- ✅ 超时控制
- ✅ 分布式追踪

---

### 4. 分布式通信层

#### NATS 集成
```csharp
// 配置 NATS 集群
services.AddNatsCatga("nats://node1:4222,nats://node2:4222");

// Request-Reply 模式（RPC）
var result = await mediator.SendAsync(new GetUserQuery(userId));

// Pub-Sub 模式（事件）
await mediator.PublishAsync(new UserCreatedEvent(userId));
```

#### 通信模式
1. **Request-Reply** (请求-响应)
   - 命令和查询使用
   - 点对点通信
   - 返回结果

2. **Pub-Sub** (发布-订阅)
   - 事件使用
   - 一对多通信
   - 异步处理

3. **本地通信** (In-Process)
   - 单体应用内
   - 零网络开销
   - 高性能

---

### 5. 持久化层

#### Redis 持久化
```csharp
services.AddRedisCatga(options =>
{
    options.ConnectionString = "localhost:6379";
    options.EnableSagaPersistence = true;    // Saga 状态
    options.EnableIdempotency = true;        // 幂等性
    options.EnableEventStore = true;         // 事件存储（可选）
});
```

**存储内容**:
- Saga 状态和上下文
- 幂等性记录
- 事件流（事件溯源）
- 分布式锁

#### 事件溯源 (Event Sourcing)
```csharp
// 存储事件
await eventStore.AppendAsync("order-123", new OrderCreatedEvent(...));
await eventStore.AppendAsync("order-123", new OrderPaidEvent(...));

// 重建状态
var events = await eventStore.ReadAsync("order-123");
var order = Order.FromEvents(events);
```

---

### 6. 弹性和可靠性

#### 熔断器 (Circuit Breaker)
```csharp
services.AddCatga(options =>
{
    options.EnableCircuitBreaker = true;
    options.CircuitBreakerFailureThreshold = 5;
    options.CircuitBreakerResetTimeoutSeconds = 60;
});

// 自动熔断保护
// 连续失败 5 次后，熔断器打开，快速失败
// 60 秒后自动尝试恢复
```

#### 重试机制
```csharp
services.AddCatga(options =>
{
    options.EnableRetry = true;
    options.MaxRetryAttempts = 3;
    options.RetryDelaySeconds = 2;
});

// 自动重试失败的操作
// 指数退避策略
```

#### 限流控制
```csharp
services.AddCatga(options =>
{
    options.EnableRateLimiting = true;
    options.RateLimitRequestsPerSecond = 100;
    options.RateLimitBurstCapacity = 200;
});

// 令牌桶算法
// 防止流量突刺
```

#### 并发控制
```csharp
services.AddCatga(options =>
{
    options.MaxConcurrentRequests = 100;
});

// 限制同时处理的请求数
// 保护下游服务
```

#### 死信队列 (DLQ)
```csharp
services.AddCatga(options =>
{
    options.EnableDeadLetterQueue = true;
});

// 失败消息自动进入 DLQ
// 后续可以重新处理或分析
```

---

### 7. 可观测性

#### 分布式追踪
```csharp
services.AddCatga(options =>
{
    options.EnableTracing = true;
    options.ServiceName = "order-service";
});

// 自动集成 OpenTelemetry
// 跨服务追踪请求链路
```

#### 结构化日志
```csharp
services.AddCatga(options =>
{
    options.EnableLogging = true;
});

// 每个消息自动记录：
// - MessageId, CorrelationId
// - 处理时间
// - 成功/失败状态
// - 异常信息
```

---

## 🌐 分布式场景

### 场景 1: 微服务架构

```
┌─────────────┐     NATS      ┌─────────────┐
│ Order       │ ──────────────>│ Payment     │
│ Service     │<────────────── │ Service     │
└─────────────┘                └─────────────┘
      │                               │
      │         NATS                  │
      ├──────────────────────────────>│
      │                          ┌─────────────┐
      │                          │ Inventory   │
      │                          │ Service     │
      │                          └─────────────┘
      │
      │         NATS
      └──────────────────────────────>
                                 ┌─────────────┐
                                 │Notification │
                                 │ Service     │
                                 └─────────────┘
```

### 场景 2: 事件驱动架构

```
┌─────────────┐
│   Domain    │
│   Service   │
└──────┬──────┘
       │ Publish Events
       ↓
┌─────────────┐
│   NATS      │
│ Event Broker│
└──────┬──────┘
       │ Subscribe
       ├────────────────┬────────────────┬
       ↓                ↓                ↓
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│   Email     │  │  Analytics  │  │   Audit     │
│  Service    │  │   Service   │  │  Service    │
└─────────────┘  └─────────────┘  └─────────────┘
```

### 场景 3: Saga 分布式事务

```
┌─────────────────────────────────────────────┐
│           Saga Coordinator                  │
│         (Order Saga Executor)               │
└──────┬──────────┬──────────┬────────────────┘
       │          │          │
   Step 1     Step 2     Step 3
       │          │          │
       ↓          ↓          ↓
┌──────────┐ ┌──────────┐ ┌──────────┐
│ Payment  │ │Inventory │ │  Order   │
│ Service  │ │ Service  │ │ Service  │
└──────────┘ └──────────┘ └──────────┘
       ↑          ↑          ↑
   Compensate Compensate  Compensate
   (if fail)  (if fail)   (if fail)
```

---

## 🔧 部署模式

### 1. 单体应用 (Monolithic)
```csharp
// Program.cs
services.AddCatga(); // 仅本地通信

// 所有功能在一个进程内
// 适合: 小型应用、快速开发
```

### 2. 模块化单体 (Modular Monolith)
```csharp
// Program.cs
services.AddCatga(); // 本地通信
services.AddModule<OrderModule>();
services.AddModule<PaymentModule>();
services.AddModule<InventoryModule>();

// 模块间通过消息总线解耦
// 适合: 中型应用、渐进式演进
```

### 3. 微服务 (Microservices)
```csharp
// OrderService/Program.cs
services.AddCatga();
services.AddNatsCatga("nats://cluster");

// PaymentService/Program.cs
services.AddCatga();
services.AddNatsCatga("nats://cluster");

// 每个服务独立部署
// 通过 NATS 通信
// 适合: 大型应用、团队协作
```

### 4. Serverless
```csharp
// Azure Function / AWS Lambda
services.AddCatga();
services.AddNatsCatga("nats://managed-cluster");

// 无状态函数
// 事件驱动
// 按需扩展
```

---

## 🎯 框架对比

### Catga vs MassTransit

| 特性 | Catga | MassTransit |
|------|-------|-------------|
| **定位** | 分布式框架 | 消息总线 |
| **CQRS** | ✅ 内置 | ❌ 需要额外实现 |
| **Saga** | ✅ 内置 (CatGa) | ✅ 内置 |
| **AOT 支持** | ✅ 100% | ❌ 有限 |
| **性能** | ⚡ 极致优化 | ⚡ 良好 |
| **零分配** | ✅ 关键路径 | ❌ 否 |
| **传输** | NATS, 可扩展 | RabbitMQ, Azure SB等 |
| **学习曲线** | 🟢 简单 | 🟡 中等 |

### Catga vs NServiceBus

| 特性 | Catga | NServiceBus |
|------|-------|-------------|
| **定位** | 分布式框架 | 企业服务总线 |
| **许可** | ✅ MIT 开源 | ⚠️ 商业许可 |
| **CQRS** | ✅ 内置 | ✅ 支持 |
| **Saga** | ✅ 内置 | ✅ 内置 |
| **AOT** | ✅ 100% | ❌ 否 |
| **性能** | ⚡ 极致 | ⚡ 良好 |
| **企业功能** | 🟡 发展中 | ✅ 完善 |

### Catga vs CAP

| 特性 | Catga | CAP |
|------|-------|-----|
| **定位** | 分布式框架 | 事件总线 |
| **CQRS** | ✅ 内置 | ❌ 需要额外 |
| **Saga** | ✅ 内置 | ❌ 需要额外 |
| **Outbox** | 🔄 计划中 | ✅ 内置 |
| **AOT** | ✅ 100% | ❌ 否 |
| **数据库** | Redis + 可扩展 | 多种支持 |
| **消息队列** | NATS + 可扩展 | RabbitMQ, Kafka等 |

---

## 📦 框架组件

### 核心组件 (Catga)
- **CatgaMediator** - 消息路由
- **Pipeline Behaviors** - 管道行为
- **Result 类型** - 错误处理
- **消息类型** - Command/Query/Event
- **Handler 接口** - 处理器抽象

### 分布式组件 (Catga.Nats)
- **NatsCatgaMediator** - NATS 分布式中介
- **Request-Reply** - 请求响应传输
- **Pub-Sub** - 发布订阅传输
- **订阅管理** - 自动订阅

### 持久化组件 (Catga.Redis)
- **RedisCatGaStore** - Saga 状态存储
- **RedisIdempotencyStore** - 幂等性存储
- **EventStore** - 事件存储（计划中）

### 弹性组件 (内置 Catga)
- **CircuitBreaker** - 熔断器
- **RetryBehavior** - 重试
- **RateLimiter** - 限流
- **ConcurrencyLimiter** - 并发控制
- **DeadLetterQueue** - 死信队列

### 可观测组件 (内置 Catga)
- **TracingBehavior** - 分布式追踪
- **LoggingBehavior** - 结构化日志
- **Metrics** - 指标收集（计划中）

---

## 🚀 未来路线图

### Phase 1 (已完成) ✅
- ✅ CQRS 核心
- ✅ Saga 支持
- ✅ NATS 集成
- ✅ Redis 持久化
- ✅ AOT 支持
- ✅ 性能优化

### Phase 2 (进行中) 🔄
- 🔄 Outbox/Inbox 模式
- 🔄 事件溯源完善
- 🔄 更多传输支持 (Kafka, RabbitMQ)
- 🔄 更多存储支持 (PostgreSQL, MongoDB)

### Phase 3 (计划中) 📋
- 📋 可视化监控面板
- 📋 Saga 设计器
- 📋 分布式调试工具
- 📋 性能分析工具

---

## 🎓 学习路径

1. **基础**: CQRS 模式 → 本地消息总线
2. **进阶**: 事件驱动 → 分布式通信
3. **高级**: Saga 事务 → 事件溯源
4. **专家**: 自定义传输 → 自定义存储

---

**Catga 不仅是一个 CQRS 库，而是一个完整的分布式应用框架！**

它提供了从单体到微服务的完整演进路径，帮助开发者构建可扩展、可靠、高性能的分布式系统。

