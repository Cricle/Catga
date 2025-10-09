# Catga 主库精简计划 - 分离内存实现

## 🎯 目标

将 Catga 主库精简为**纯接口和抽象类型**，创建 `Catga.InMemory` 库来承载所有内存实现。

## 📊 当前问题

- Catga 主库包含了接口、抽象类、内存实现、具体实现
- 依赖包过多（Polly, HealthChecks, Hosting 等）
- 用户即使不需要内存实现，也会引入这些依赖

## ✅ 解决方案

### 方案 1: Catga (核心抽象库)

**保留内容**（只保留接口和抽象类）：

#### 核心接口
- `ICatgaMediator` - Mediator 接口
- `IMessageTransport` - 传输接口
- `IMessageSerializer` - 序列化接口
- `IDistributedIdGenerator` - 分布式 ID 接口
- `IDistributedLock` - 分布式锁接口
- `IDistributedCache` - 分布式缓存接口
- `IEventStore` - 事件存储接口
- `IOutboxStore` / `IInboxStore` - Outbox/Inbox 接口
- `IIdempotencyStore` - 幂等性存储接口
- `IDeadLetterQueue` - 死信队列接口
- `IHealthCheck` - 健康检查接口
- `IServiceDiscovery` - 服务发现接口
- `ISaga` - Saga 接口
- `IRequestHandler` / `IEventHandler` - 处理器接口
- `IPipelineBehavior` - 管道接口

#### 抽象类和核心类型
- `MessageContracts` (IMessage, ICommand, IQuery, IEvent, MessageBase, EventBase)
- `HandlerContracts` (IRequestHandler, IEventHandler)
- `CatgaResult` - 结果类型
- `CatgaException` - 异常类型
- `CatgaOptions` - 配置选项
- `AggregateRoot` - 事件溯源聚合根基类
- `BaseBehavior` - Pipeline Behavior 基类

#### 纯算法实现（无依赖）
- `SnowflakeIdGenerator` - Snowflake 算法实现（纯算法，无外部依赖）
- `SnowflakeBitLayout` - Bit 布局（纯算法）

#### 辅助工具（无依赖）
- `ArrayPoolHelper` - ArrayPool 辅助
- `MessageHelper` - 消息辅助
- `FastPath` - 快速路径优化
- `RequestContextPool` - 对象池

**依赖包**（最小化）：
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

---

### 方案 2: Catga.InMemory (内存实现库)

**新建库**，包含所有内存实现：

#### 内存实现类
- `CatgaMediator` - Mediator 实现
- `InMemoryMessageTransport` - 内存传输
- `MemoryDistributedLock` - 内存分布式锁
- `MemoryEventStore` - 内存事件存储
- `MemoryOutboxStore` - 内存 Outbox
- `MemoryInboxStore` - 内存 Inbox
- `MemoryServiceDiscovery` - 内存服务发现
- `InMemoryDeadLetterQueue` - 内存死信队列
- `ShardedIdempotencyStore` - 分片幂等性存储
- `BaseMemoryStore` - 内存存储基类

#### Pipeline 实现
- `PipelineExecutor` - Pipeline 执行器
- 所有 Behavior 实现：
  - `LoggingBehavior`
  - `ValidationBehavior`
  - `RetryBehavior`
  - `IdempotencyBehavior`
  - `CachingBehavior`
  - `OutboxBehavior`
  - `InboxBehavior`
  - `TracingBehavior`

#### 弹性实现
- `CircuitBreaker` - 熔断器
- `ConcurrencyLimiter` - 并发限制器
- `TokenBucketRateLimiter` - 令牌桶限流器
- `ResiliencePipeline` - 弹性管道

#### Saga 实现
- `SagaBuilder` - Saga 构建器
- `SagaExecutor` - Saga 执行器

#### 其他实现
- `OutboxPublisher` - Outbox 发布器
- `HealthCheckService` - 健康检查服务
- `CatgaHealthCheck` - Catga 健康检查
- `HandlerCache` - Handler 缓存
- `CatgaMetrics` - 指标收集
- `MessageCompressor` - 消息压缩
- `BackpressureManager` - 背压管理

#### 依赖注入扩展
- `CatgaBuilder` - 构建器
- `CatgaBuilderExtensions` - 扩展方法
- 所有 `ServiceCollectionExtensions`

**依赖包**：
```xml
<ProjectReference Include="..\Catga\Catga.csproj" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
<PackageReference Include="Microsoft.Extensions.Logging" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
<PackageReference Include="Polly" />
```

---

## 📦 移动文件清单

### 从 Catga 移动到 Catga.InMemory

#### 核心实现
- `CatgaMediator.cs`
- `Transport/InMemoryMessageTransport.cs`
- `Transport/MessageCompressor.cs`
- `Transport/BackpressureManager.cs`

#### 内存存储
- `Common/BaseMemoryStore.cs`
- `DeadLetter/InMemoryDeadLetterQueue.cs`
- `DistributedLock/MemoryDistributedLock.cs`
- `EventSourcing/MemoryEventStore.cs`
- `Idempotency/ShardedIdempotencyStore.cs`
- `Inbox/MemoryInboxStore.cs`
- `Outbox/MemoryOutboxStore.cs`
- `Outbox/OutboxPublisher.cs`
- `ServiceDiscovery/MemoryServiceDiscovery.cs`

#### Pipeline 实现
- `Pipeline/PipelineExecutor.cs`
- `Pipeline/Behaviors/*` (所有 Behavior 实现)

#### 弹性和性能
- `Resilience/CircuitBreaker.cs`
- `Resilience/ResiliencePipeline.cs`
- `Concurrency/ConcurrencyLimiter.cs`
- `RateLimiting/TokenBucketRateLimiter.cs`
- `Performance/HandlerCache.cs`

#### Saga
- `Saga/SagaBuilder.cs`
- `Saga/SagaExecutor.cs`

#### 健康检查和可观测性
- `HealthCheck/HealthCheckService.cs`
- `HealthCheck/CatgaHealthCheck.cs` (可能重复)
- `Observability/CatgaMetrics.cs`
- `Observability/ObservabilityExtensions.cs`

#### 依赖注入
- `DependencyInjection/CatgaBuilder.cs`
- `DependencyInjection/CatgaBuilderExtensions.cs`
- `DependencyInjection/TransitServiceCollectionExtensions.cs`
- `DependencyInjection/TransportServiceCollectionExtensions.cs`
- `DependencyInjection/ServiceDiscoveryExtensions.cs`
- `DistributedId/DistributedIdServiceCollectionExtensions.cs`
- `DistributedLock/MemoryDistributedLockServiceCollectionExtensions.cs`
- `EventSourcing/EventSourcingServiceCollectionExtensions.cs`
- `HealthCheck/HealthCheckServiceCollectionExtensions.cs`
- `Saga/SagaServiceCollectionExtensions.cs`
- `Caching/DistributedCacheServiceCollectionExtensions.cs`

#### 序列化（如果有具体实现）
- `Serialization/CatgaJsonSerializerContext.cs`
- `Serialization/SerializationBufferPool.cs`
- `Common/SerializationHelper.cs`

---

### 保留在 Catga（接口和抽象）

#### 核心接口
- `ICatgaMediator.cs`
- `Transport/IMessageTransport.cs`
- `Serialization/IMessageSerializer.cs`
- `Serialization/IBufferedMessageSerializer.cs`
- `DistributedId/IDistributedIdGenerator.cs`
- `DistributedLock/IDistributedLock.cs`
- `Caching/IDistributedCache.cs`
- `EventSourcing/IEventStore.cs`
- `Outbox/IOutboxStore.cs`
- `Inbox/IInboxStore.cs`
- `Idempotency/IIdempotencyStore.cs`
- `DeadLetter/IDeadLetterQueue.cs`
- `HealthCheck/IHealthCheck.cs`
- `ServiceDiscovery/IServiceDiscovery.cs`
- `Saga/ISaga.cs`
- `Pipeline/IPipelineBehavior.cs`

#### 消息和处理器
- `Messages/MessageContracts.cs`
- `Messages/MessageIdentifiers.cs`
- `Handlers/HandlerContracts.cs`

#### 抽象类
- `EventSourcing/AggregateRoot.cs`
- `Pipeline/Behaviors/BaseBehavior.cs`

#### 结果和异常
- `Results/CatgaResult.cs`
- `Exceptions/CatgaException.cs`

#### 配置
- `Configuration/CatgaOptions.cs`
- `Configuration/CatgaOptionsValidator.cs`
- `Configuration/ThreadPoolOptions.cs`
- `Configuration/SmartDefaults.cs`
- `DistributedId/DistributedIdOptions.cs`

#### 纯算法（无依赖）
- `DistributedId/SnowflakeIdGenerator.cs`
- `DistributedId/SnowflakeBitLayout.cs`

#### 辅助工具（无依赖）
- `Common/ArrayPoolHelper.cs`
- `Common/MessageHelper.cs`
- `Common/BatchOperationExtensions.cs`
- `Performance/FastPath.cs`
- `Performance/RequestContextPool.cs`

---

## 🔄 命名空间保持不变

所有类型的命名空间保持不变，例如：
- `Catga` - 核心命名空间
- `Catga.Messages` - 消息
- `Catga.Handlers` - 处理器
- `Catga.Transport` - 传输
- 等等

这样用户代码无需修改 `using` 语句。

---

## 📝 更新说明

### Catga.csproj
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
</ItemGroup>
```

### Catga.InMemory.csproj
```xml
<ItemGroup>
  <ProjectReference Include="..\Catga\Catga.csproj" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  <PackageReference Include="Microsoft.Extensions.Logging" />
  <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
  <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  <PackageReference Include="Polly" />
</ItemGroup>
```

---

## ✅ 预期结果

1. **Catga** - 纯接口和抽象，最小依赖
2. **Catga.InMemory** - 完整的内存实现
3. **用户可选** - 根据需要引用 Catga.InMemory 或其他实现（Redis, NATS 等）
4. **向后兼容** - 命名空间不变，只需添加 `Catga.InMemory` 包引用

---

## 📊 依赖关系

```
Catga (核心抽象)
  ├─ Microsoft.Extensions.DependencyInjection.Abstractions
  └─ Microsoft.Extensions.Logging.Abstractions

Catga.InMemory
  ├─ Catga (核心抽象)
  ├─ Microsoft.Extensions.DependencyInjection
  ├─ Microsoft.Extensions.Logging
  ├─ Microsoft.Extensions.Diagnostics.HealthChecks
  ├─ Microsoft.Extensions.Hosting.Abstractions
  └─ Polly

Catga.Transport.Nats
  ├─ Catga (核心抽象)
  └─ NATS.Client.Core

Catga.Persistence.Redis
  ├─ Catga (核心抽象)
  └─ StackExchange.Redis
```

---

## 🚀 实施步骤

1. 创建 `Catga.InMemory` 项目
2. 移动所有实现类到 `Catga.InMemory`
3. 更新 `Catga.csproj`，移除不必要的依赖
4. 更新命名空间（保持不变）
5. 更新测试项目引用 `Catga.InMemory`
6. 更新示例项目引用 `Catga.InMemory`
7. 更新文档
8. 验证编译和测试

---

**预计影响**:
- Catga 包大小: -60%
- 依赖包数量: 6个 → 2个 (-67%)
- 用户可选性: +100%

