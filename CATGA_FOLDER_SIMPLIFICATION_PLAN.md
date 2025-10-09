# Catga 主库文件夹简化计划

## 🎯 目标
将 Catga 主库从 20+ 个文件夹精简到 5 个核心文件夹

## 📊 当前状态
```
src/Catga/
├── Caching/                  (1个接口)
├── Common/                   (3个工具类)
├── Concurrency/              (空)
├── Configuration/            (4个配置类)
├── DeadLetter/               (1个接口)
├── DependencyInjection/      (空)
├── DistributedId/            (4个类)
├── DistributedLock/          (1个接口)
├── EventSourcing/            (2个类)
├── Exceptions/               (1个异常类)
├── Handlers/                 (1个文件)
├── HealthCheck/              (1个接口)
├── Idempotency/              (1个接口)
├── Inbox/                    (1个接口)
├── Messages/                 (2个文件)
├── Observability/            (空)
├── Outbox/                   (1个接口)
├── Performance/              (2个类)
├── Pipeline/                 (2个文件 + Behaviors/)
├── RateLimiting/             (空)
├── Resilience/               (空)
├── Results/                  (1个类)
├── Saga/                     (1个接口)
├── Serialization/            (2个接口)
├── ServiceDiscovery/         (1个接口)
└── Transport/                (1个接口)

总计: 25 个文件夹，很多只有 1 个文件！
```

## ✅ 简化后结构

```
src/Catga/
├── Abstractions/         # 所有接口 (10个接口文件)
│   ├── ICatgaMediator.cs
│   ├── IMessageTransport.cs
│   ├── IMessageSerializer.cs
│   ├── IDistributedIdGenerator.cs
│   ├── IDistributedLock.cs
│   ├── IDistributedCache.cs
│   ├── IEventStore.cs
│   ├── IOutboxStore.cs
│   ├── IInboxStore.cs
│   ├── IIdempotencyStore.cs
│   ├── IDeadLetterQueue.cs
│   ├── IHealthCheck.cs
│   ├── IServiceDiscovery.cs
│   ├── ISaga.cs
│   └── IPipelineBehavior.cs
├── Messages/             # 消息定义 (2个文件)
│   ├── MessageContracts.cs
│   └── MessageIdentifiers.cs
├── Handlers/             # Handler 定义 (1个文件)
│   └── HandlerContracts.cs
├── Core/                 # 核心实现类 (不依赖外部包)
│   ├── SnowflakeIdGenerator.cs
│   ├── SnowflakeBitLayout.cs
│   ├── AggregateRoot.cs
│   ├── BaseBehavior.cs
│   ├── CatgaResult.cs
│   ├── CatgaException.cs
│   ├── CatgaOptions.cs
│   ├── DistributedIdOptions.cs
│   ├── ArrayPoolHelper.cs
│   ├── BatchOperationExtensions.cs
│   ├── MessageHelper.cs
│   ├── FastPath.cs
│   ├── RequestContextPool.cs
│   ├── CatgaOptionsValidator.cs
│   ├── SmartDefaults.cs
│   └── ThreadPoolOptions.cs
└── Catga.csproj

总计: 4 个文件夹，清晰明了！
```

## 📝 移动方案

### 步骤 1: 创建 Abstractions 文件夹，移动所有接口
```
ICatgaMediator.cs                           → Abstractions/
Transport/IMessageTransport.cs              → Abstractions/
Serialization/IMessageSerializer.cs         → Abstractions/
Serialization/IBufferedMessageSerializer.cs → Abstractions/
DistributedId/IDistributedIdGenerator.cs    → Abstractions/
DistributedLock/IDistributedLock.cs         → Abstractions/
Caching/IDistributedCache.cs                → Abstractions/
EventSourcing/IEventStore.cs                → Abstractions/
Outbox/IOutboxStore.cs                      → Abstractions/
Inbox/IInboxStore.cs                        → Abstractions/
Idempotency/IIdempotencyStore.cs            → Abstractions/
DeadLetter/IDeadLetterQueue.cs              → Abstractions/
HealthCheck/IHealthCheck.cs                 → Abstractions/
ServiceDiscovery/IServiceDiscovery.cs       → Abstractions/
Saga/ISaga.cs                               → Abstractions/
Pipeline/IPipelineBehavior.cs               → Abstractions/
```

### 步骤 2: 创建 Core 文件夹，移动核心实现
```
DistributedId/SnowflakeIdGenerator.cs       → Core/
DistributedId/SnowflakeBitLayout.cs         → Core/
DistributedId/DistributedIdOptions.cs       → Core/
EventSourcing/AggregateRoot.cs              → Core/
Pipeline/Behaviors/BaseBehavior.cs          → Core/
Results/CatgaResult.cs                      → Core/
Exceptions/CatgaException.cs                → Core/
Configuration/CatgaOptions.cs               → Core/
Configuration/CatgaOptionsValidator.cs      → Core/
Configuration/SmartDefaults.cs              → Core/
Configuration/ThreadPoolOptions.cs          → Core/
Common/ArrayPoolHelper.cs                   → Core/
Common/BatchOperationExtensions.cs          → Core/
Common/MessageHelper.cs                     → Core/
Performance/FastPath.cs                     → Core/
Performance/RequestContextPool.cs           → Core/
```

### 步骤 3: 保留现有文件夹
```
Messages/                                    → 保留
Handlers/                                    → 保留
```

### 步骤 4: 删除空文件夹
```
Caching/                                     → 删除
Common/                                      → 删除
Concurrency/                                 → 删除
Configuration/                               → 删除
DeadLetter/                                  → 删除
DependencyInjection/                         → 删除
DistributedId/                               → 删除
DistributedLock/                             → 删除
EventSourcing/                               → 删除
Exceptions/                                  → 删除
HealthCheck/                                 → 删除
Idempotency/                                 → 删除
Inbox/                                       → 删除
Observability/                               → 删除
Outbox/                                      → 删除
Performance/                                 → 删除
Pipeline/                                    → 删除
RateLimiting/                                → 删除
Resilience/                                  → 删除
Results/                                     → 删除
Saga/                                        → 删除
Serialization/                               → 删除
ServiceDiscovery/                            → 删除
Transport/                                   → 删除
```

## 📊 简化效果

```
之前: 25 个文件夹
之后: 4 个文件夹
减少: 84%

清晰度: ⭐⭐⭐⭐⭐
维护性: ⭐⭐⭐⭐⭐
可读性: ⭐⭐⭐⭐⭐
```

## 🎯 命名空间策略

**保持命名空间不变**，只移动物理文件位置：
- `Catga.Abstractions` - 所有接口
- `Catga.Messages` - 消息定义
- `Catga.Handlers` - Handler 定义
- `Catga.Core` - 核心实现
- 原有的详细命名空间（如 `Catga.DistributedId`, `Catga.Transport` 等）保留在代码中

这样用户代码无需修改 using 语句！

## ✅ 优势

1. **极简文件夹结构** - 只有 4 个文件夹
2. **清晰的职责划分** - 接口 / 消息 / Handler / 核心实现
3. **易于导航** - 不再需要在 25 个文件夹中查找
4. **向后兼容** - 命名空间不变
5. **利于理解** - 新用户一眼就能看懂结构

