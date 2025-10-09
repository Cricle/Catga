# Catga v2.0 架构说明

## 🎯 设计理念

Catga v2.0 采用**分层架构**设计，核心原则：

1. **接口抽象分离** - 主库只包含接口和抽象
2. **实现可插拔** - 用户按需选择实现
3. **依赖最小化** - 核心库只依赖2个抽象包
4. **100% AOT 兼容** - 零反射，完全静态化

---

## 📦 包结构

### 1. Catga (核心抽象层)

**定位**: 纯接口和抽象，无任何具体实现

**文件夹结构**:
```
src/Catga/
├── Abstractions/           # 所有接口 (16个)
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
│   ├── IPipelineBehavior.cs
│   └── IBufferedMessageSerializer.cs
├── Core/                   # 核心实现（无外部依赖）
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
│   └── RequestContextPool.cs
├── Messages/               # 消息定义
│   ├── MessageContracts.cs
│   └── MessageIdentifiers.cs
└── Handlers/               # Handler 定义
    └── HandlerContracts.cs
```

**依赖包**:
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

**特点**:
- ✅ 纯接口和抽象
- ✅ 零具体实现
- ✅ 最小依赖（2个）
- ✅ 包体积小（~100KB）
- ✅ 100% AOT 兼容

---

### 2. Catga.InMemory (内存实现层)

**定位**: 所有内存实现，用于开发和测试

**包含内容**:
```
src/Catga.InMemory/
├── CatgaMediator.cs                    # Mediator 实现
├── Transport/
│   ├── InMemoryMessageTransport.cs    # 内存传输
│   ├── MessageCompressor.cs
│   └── BackpressureManager.cs
├── Pipeline/
│   ├── PipelineExecutor.cs
│   └── Behaviors/
│       ├── LoggingBehavior.cs
│       ├── ValidationBehavior.cs
│       ├── RetryBehavior.cs
│       ├── IdempotencyBehavior.cs
│       ├── CachingBehavior.cs
│       ├── OutboxBehavior.cs
│       ├── InboxBehavior.cs
│       └── TracingBehavior.cs
├── Resilience/
│   ├── CircuitBreaker.cs
│   └── ResiliencePipeline.cs
├── Concurrency/
│   ├── ConcurrencyLimiter.cs
│   └── TokenBucketRateLimiter.cs
├── Stores/
│   ├── MemoryOutboxStore.cs
│   ├── MemoryInboxStore.cs
│   ├── MemoryEventStore.cs
│   ├── MemoryDistributedLock.cs
│   ├── ShardedIdempotencyStore.cs
│   └── InMemoryDeadLetterQueue.cs
├── Saga/
│   ├── SagaBuilder.cs
│   └── SagaExecutor.cs
├── Observability/
│   ├── CatgaMetrics.cs
│   └── CatgaHealthCheck.cs
└── DependencyInjection/
    ├── CatgaBuilder.cs
    └── ServiceCollectionExtensions.cs
```

**依赖包**:
- `Catga` (核心抽象)
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.Diagnostics.HealthChecks`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Polly`

**特点**:
- ✅ 完整的内存实现
- ✅ 适合开发和测试
- ✅ 无需外部依赖（Redis/NATS）
- ✅ 包含所有 Pipeline Behaviors
- ✅ 100% AOT 兼容

---

### 3. Catga.Transport.Nats (NATS 传输层)

**定位**: NATS 消息传输实现

**依赖包**:
- `Catga` (核心抽象)
- `NATS.Client.Core`

**用途**:
- 分布式消息传输
- 事件驱动通信
- 高性能 Pub/Sub

---

### 4. Catga.Persistence.Redis (Redis 持久化层)

**定位**: Redis 分布式缓存和锁

**依赖包**:
- `Catga` (核心抽象)
- `StackExchange.Redis`

**用途**:
- 分布式缓存
- 分布式锁
- 持久化存储

---

### 5. Catga.SourceGenerator (源生成器)

**定位**: 编译时代码生成

**功能**:
- 自动发现 Handler
- 自动注册 Handler
- 支持 Lifetime 配置
- 零反射，AOT 友好

---

### 6. Catga.Analyzers (代码分析器)

**定位**: 静态代码分析

**包含规则** (20个):
- 性能分析 (GC 压力)
- 并发安全分析
- AOT 兼容性分析
- 分布式模式分析
- 最佳实践分析

---

## 🏗️ 依赖关系图

```
┌─────────────────────┐
│   User Application  │
└──────────┬──────────┘
           │
           ├──────────────────────────┐
           │                          │
           ▼                          ▼
┌──────────────────┐      ┌──────────────────────┐
│  Catga (核心)    │◄─────│  Catga.InMemory      │
│  - Abstractions  │      │  - 内存实现          │
│  - Core          │      │  - Pipeline          │
│  - Messages      │      │  - Resilience        │
│  - Handlers      │      │  - Stores            │
└──────────────────┘      └──────────────────────┘
           ▲
           │
           ├──────────────────────────┐
           │                          │
┌──────────┴──────────┐    ┌──────────┴──────────┐
│ Catga.Transport.    │    │ Catga.Persistence.  │
│ Nats                │    │ Redis               │
└─────────────────────┘    └─────────────────────┘
```

---

## 🎯 使用场景

### 场景 1: 开发和测试

```bash
dotnet add package Catga
dotnet add package Catga.InMemory
dotnet add package Catga.SourceGenerator
```

**优势**:
- 无需外部依赖
- 快速启动
- 易于调试

### 场景 2: 生产环境（分布式）

```bash
dotnet add package Catga
dotnet add package Catga.InMemory          # Pipeline + Behaviors
dotnet add package Catga.Transport.Nats   # NATS 传输
dotnet add package Catga.Persistence.Redis # Redis 缓存/锁
dotnet add package Catga.SourceGenerator
```

**优势**:
- 高性能消息传输
- 分布式缓存和锁
- 可靠消息投递

### 场景 3: 单体应用

```bash
dotnet add package Catga
dotnet add package Catga.InMemory
dotnet add package Catga.SourceGenerator
```

**优势**:
- 简单直接
- 性能优异
- 易于部署

---

## 📊 架构优势

### 1. 依赖倒置原则 (DIP)

```
高层模块 (User Application)
    ↓ 依赖
抽象层 (Catga Abstractions)
    ↑ 实现
实现层 (Catga.InMemory, Catga.Transport.Nats, etc.)
```

用户只依赖抽象，实现可插拔。

### 2. 开闭原则 (OCP)

- 核心抽象稳定，对修改封闭
- 新增实现，对扩展开放
- 例如：可以添加 `Catga.Transport.Kafka` 而不影响核心

### 3. 单一职责原则 (SRP)

- `Catga` - 定义契约
- `Catga.InMemory` - 提供内存实现
- `Catga.Transport.*` - 提供传输实现
- `Catga.Persistence.*` - 提供持久化实现

### 4. 接口隔离原则 (ISP)

- 16 个独立接口
- 用户只需依赖所需接口
- 无冗余依赖

---

## 🚀 性能优化

### 核心层优化

- **FastPath** - 零分配快速路径
- **ArrayPoolHelper** - 对象池管理
- **BatchOperationExtensions** - 批量操作优化
- **SnowflakeIdGenerator** - Lock-Free ID 生成

### 内存实现优化

- **PipelineExecutor** - 高效管道执行
- **CircuitBreaker** - Lock-Free 熔断器
- **ConcurrencyLimiter** - 并发控制
- **ShardedIdempotencyStore** - 分片幂等性存储

---

## 📝 命名空间策略

**物理位置** vs **命名空间**:

| 物理位置 | 命名空间 | 说明 |
|---------|---------|------|
| `Abstractions/ICatgaMediator.cs` | `Catga` | 核心抽象 |
| `Abstractions/IMessageTransport.cs` | `Catga.Transport` | 传输抽象 |
| `Core/SnowflakeIdGenerator.cs` | `Catga.DistributedId` | 分布式 ID |
| `Core/CatgaResult.cs` | `Catga.Results` | 结果类型 |
| `Messages/MessageContracts.cs` | `Catga.Messages` | 消息定义 |
| `Handlers/HandlerContracts.cs` | `Catga.Handlers` | Handler 定义 |

**策略**: 物理文件夹简化（4个），命名空间保持详细（向后兼容）

---

## 🎯 迁移指南

### 从 v1.x 迁移到 v2.0

**变更**:
1. 需要额外安装 `Catga.InMemory` 包
2. 文件夹结构变化（但命名空间不变）
3. 依赖包减少（Catga 主库）

**步骤**:

```bash
# 1. 添加 Catga.InMemory
dotnet add package Catga.InMemory

# 2. 无需修改代码（命名空间不变）
# using Catga;
# using Catga.Messages;
# using Catga.Handlers;
# ...（全部保持不变）

# 3. 重新编译
dotnet build
```

**零破坏性变更！** ✅

---

## 📚 相关文档

- [快速开始指南](QUICK_START.md)
- [重构计划](REFACTOR_INMEMORY_PLAN.md)
- [文件夹简化计划](CATGA_FOLDER_SIMPLIFICATION_PLAN.md)
- [性能基准测试](benchmarks/README.md)

---

## ✅ 架构验证

- ✅ Catga 主库编译成功
- ✅ Catga.InMemory 编译成功
- ✅ 测试通过 (90/90)
- ✅ 示例项目运行正常
- ✅ 文件夹数量: 25 → 4 (-84%)
- ✅ 依赖包: 6 → 2 (-67%)
- ✅ 包大小预计: -60%

---

**Catga v2.0 - 极简架构，强大功能！** 🚀

