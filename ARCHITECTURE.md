# 🏗️ Catga 框架架构

## 📊 功能分层

Catga 采用**分层架构**，从核心到高级逐步增强，用户可以按需引入功能。

---

## 🎯 核心层（Core Layer）- 必需

> **定位**: CQRS + Mediator 模式的核心实现  
> **复杂度**: ⭐⭐  
> **学习时间**: 30 分钟

### Catga

**包含功能**:
- ✅ CQRS 抽象（ICommand, IQuery, IEvent）
- ✅ Mediator 模式
- ✅ Pipeline Behaviors
- ✅ Result<T> 模式
- ✅ 本地消息总线

**使用场景**:
- 单体应用
- 微服务内部
- 需要 CQRS 模式

**示例**:
```csharp
services.AddCatga();

// 发送命令
var result = await mediator.SendAsync(new CreateOrderCommand(...));

// 发送查询
var result = await mediator.SendAsync(new GetOrderQuery(...));

// 发布事件
await mediator.PublishAsync(new OrderCreatedEvent(...));
```

---

## 🌐 分布式层（Distributed Layer）- 推荐

> **定位**: 分布式微服务通信  
> **复杂度**: ⭐⭐⭐  
> **学习时间**: 1 小时

### Catga.Nats

**包含功能**:
- ✅ NATS 消息传输
- ✅ Request-Reply 模式
- ✅ Pub-Sub 模式
- ✅ 队列组（负载均衡）

**使用场景**:
- 微服务架构
- 分布式系统
- 跨服务通信

**示例**:
```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222");

// 跨服务调用
var result = await mediator.SendAsync(new CreateOrderCommand(...));  // 自动路由到 OrderService
```

### Catga.Redis

**包含功能**:
- ✅ Redis 状态存储
- ✅ 幂等性存储
- ✅ Outbox/Inbox 持久化
- ✅ Saga 状态持久化

**使用场景**:
- 需要持久化状态
- 分布式幂等性
- Saga 事务

**示例**:
```csharp
services.AddCatga()
    .AddRedisCatgaStore("localhost:6379");
```

---

## 🔄 可靠性层（Reliability Layer）- 推荐

> **定位**: 确保消息可靠投递和处理  
> **复杂度**: ⭐⭐⭐  
> **学习时间**: 1-2 小时

### Outbox/Inbox 模式

**包含功能**:
- ✅ Outbox 模式（确保消息发送）
- ✅ Inbox 模式（确保幂等处理）
- ✅ 内存实现
- ✅ Redis 实现

**使用场景**:
- 关键业务流程
- 需要消息不丢失
- 需要幂等性保证

**示例**:
```csharp
services.AddCatga()
    .AddRedisOutbox()    // 可靠消息发送
    .AddRedisInbox();    // 幂等消息处理
```

### Saga 分布式事务

**包含功能**:
- ✅ Saga 编排
- ✅ 补偿机制
- ✅ 状态持久化
- ✅ 重试和超时

**使用场景**:
- 分布式事务
- 跨服务业务流程
- 需要补偿机制

**示例**:
```csharp
var saga = new OrderSaga();
saga.AddStep<CreateOrderCommand, OrderCreatedEvent>()
    .Compensate<CancelOrderCommand>()
    .WithRetry(3);

await saga.ExecuteAsync(new CreateOrderCommand(...));
```

---

## 🛡️ 弹性层（Resilience Layer）- 推荐

> **定位**: 提高系统稳定性  
> **复杂度**: ⭐⭐  
> **学习时间**: 30 分钟

**包含功能**:
- ✅ 熔断器
- ✅ 重试机制
- ✅ 限流控制
- ✅ 死信队列

**使用场景**:
- 生产环境
- 需要高可用性
- 外部服务调用

**示例**:
```csharp
services.AddCatga()
    .AddPipelineBehavior<CircuitBreakerBehavior>()
    .AddPipelineBehavior<RetryBehavior>();
```

---

## 🔍 高级层（Advanced Layer）- 可选

> **定位**: 高级分布式能力  
> **复杂度**: ⭐⭐⭐⭐  
> **学习时间**: 2-3 小时

### 服务发现

**包含功能**:
- ✅ 统一抽象（IServiceDiscovery）
- ✅ Memory 实现（开发/测试）
- ✅ DNS 实现（Kubernetes 基础）
- ✅ Consul 实现（企业级）
- ✅ YARP 实现（反向代理）
- ✅ Kubernetes API 实现（云原生）

**使用场景**:
- 大规模微服务
- 动态服务发现
- Kubernetes 环境

**示例**:
```csharp
// Kubernetes 环境
services.AddKubernetesServiceDiscovery();

// Consul 环境
services.AddConsulServiceDiscovery("http://consul:8500");
```

### 流处理

**包含功能**:
- ✅ 10+ 流操作符
- ✅ LINQ 风格 API
- ✅ 异步流处理
- ✅ 批处理、窗口、去重等

**使用场景**:
- 实时数据处理
- 事件流处理
- 数据管道

**示例**:
```csharp
var pipeline = StreamProcessor.From(eventStream)
    .Where(e => e.Type == "Order")
    .Select(e => Transform(e))
    .Batch(100)
    .Do(batch => ProcessBatch(batch));

await pipeline.RunAsync();
```

---

## 🧪 实验性层（Experimental Layer）- 实验性

> **定位**: 新增功能，API 可能变化  
> **复杂度**: ⭐⭐⭐⭐⭐  
> **状态**: 🚧 实验性

### 配置中心

**包含功能**:
- ✅ 统一抽象（IConfigurationCenter）
- ✅ Memory 实现
- ⚠️ Consul KV（待实现）
- ⚠️ Nacos（待实现）

**状态**: 🚧 实验性 - API 可能变化

**替代方案**: 使用 Microsoft.Extensions.Configuration

### 事件溯源

**包含功能**:
- ✅ EventStore 抽象
- ✅ Memory 实现
- ✅ 快照机制
- ✅ 投影管理
- ⚠️ 持久化实现（待完善）

**状态**: 🚧 实验性 - 功能不完整

**适用场景**: 需要完整审计日志的场景

---

## 📦 推荐组合

### 1. 单体应用

```csharp
services.AddCatga();  // 只需要核心
```

**包含**:
- ✅ CQRS 核心
- ✅ 本地消息总线

**复杂度**: ⭐⭐

---

### 2. 微服务（基础）

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379");
```

**包含**:
- ✅ CQRS 核心
- ✅ NATS 分布式消息
- ✅ Redis 状态存储

**复杂度**: ⭐⭐⭐

---

### 3. 微服务（生产级）⭐ 推荐

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379")
    .AddRedisOutbox()      // 可靠消息
    .AddRedisInbox()       // 幂等性
    .AddPipelineBehavior<CircuitBreakerBehavior>()  // 熔断
    .AddPipelineBehavior<RetryBehavior>();          // 重试
```

**包含**:
- ✅ CQRS 核心
- ✅ NATS 分布式消息
- ✅ Redis 状态存储
- ✅ Outbox/Inbox 模式
- ✅ 弹性设计

**复杂度**: ⭐⭐⭐⭐

---

### 4. 大规模微服务（完整）

```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisCatgaStore("localhost:6379")
    .AddRedisOutbox()
    .AddRedisInbox()
    .AddPipelineBehavior<CircuitBreakerBehavior>()
    .AddPipelineBehavior<RetryBehavior>();

// 服务发现（按需）
services.AddKubernetesServiceDiscovery();  // 或 Consul
```

**包含**: 所有核心 + 高级功能

**复杂度**: ⭐⭐⭐⭐⭐

---

## 🎯 选择建议

### 我应该使用哪些功能？

**如果你是新手** → 从核心层开始
```csharp
services.AddCatga();  // 单体应用
```

**如果你需要微服务** → 添加分布式层
```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222");
```

**如果你需要生产级可靠性** → 添加可靠性层
```csharp
services.AddCatga()
    .AddNatsCatga("nats://localhost:4222")
    .AddRedisOutbox()
    .AddRedisInbox();
```

**如果你需要高级功能** → 按需添加
```csharp
services.AddKubernetesServiceDiscovery();  // 服务发现
// 或
var pipeline = StreamProcessor.From(...);  // 流处理
```

**避免使用实验性功能** → 除非你知道自己在做什么
```csharp
// ⚠️ 实验性 - 慎用
services.AddMemoryConfigurationCenter();
services.AddEventSourcing();
```

---

## 📊 功能矩阵

| 功能 | 层级 | 状态 | 复杂度 | 推荐度 |
|-----|------|------|--------|--------|
| CQRS 核心 | 核心 | ✅ 稳定 | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| NATS 传输 | 分布式 | ✅ 稳定 | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Redis 存储 | 分布式 | ✅ 稳定 | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| Outbox/Inbox | 可靠性 | ✅ 稳定 | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Saga 事务 | 可靠性 | ✅ 稳定 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| 弹性设计 | 弹性 | ✅ 稳定 | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| 服务发现 | 高级 | ✅ 稳定 | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| 流处理 | 高级 | ✅ 稳定 | ⭐⭐⭐ | ⭐⭐⭐ |
| 配置中心 | 实验 | 🚧 实验 | ⭐⭐⭐ | ⭐ |
| 事件溯源 | 实验 | 🚧 实验 | ⭐⭐⭐⭐⭐ | ⭐ |

---

## 🎊 总结

### 核心理念
- **渐进增强**: 从简单到复杂，按需引入
- **清晰分层**: 核心、分布式、可靠性、高级、实验
- **生产就绪**: 核心功能都经过验证
- **实验隔离**: 实验性功能明确标记

### 推荐路径
1. **第1天**: 学习核心层（Catga）
2. **第2-3天**: 添加分布式层（NATS + Redis）
3. **第4-5天**: 添加可靠性层（Outbox/Inbox）
4. **第2周**: 探索高级功能（按需）
5. **避免**: 实验性功能（除非特定需求）

### 快速决策
- ✅ **一定要用**: 核心 + 分布式 + 可靠性
- 🟡 **可以考虑**: 高级功能（按场景）
- ⚠️ **谨慎使用**: 实验性功能
