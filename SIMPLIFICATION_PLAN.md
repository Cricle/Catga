# 简化计划 - 减少过度设计

## 🎯 目标
- 删除未使用/过度抽象的功能
- 简化接口和类
- 保留核心功能
- 提高代码可维护性

---

## 📋 发现的过度设计

### 1. **未使用的抽象** (删除)

#### ❌ IDistributedCache + ICacheable
- **位置**: `src/Catga/Abstractions/IDistributedCache.cs`
- **问题**:
  - 抽象了缓存，但未在核心框架使用
  - `CachingBehavior` 依赖它，但这个 Behavior 也未被广泛使用
  - 用户可直接用 `IDistributedCache` (Microsoft.Extensions.Caching)
- **操作**: **删除** `IDistributedCache`, `ICacheable`, `CachingBehavior`

#### ❌ IDistributedLock + ILockHandle
- **位置**: `src/Catga/Abstractions/IDistributedLock.cs`
- **问题**:
  - 定义了接口，但核心框架未使用
  - InboxBehavior 有自己的锁逻辑（`TryLockMessageAsync`）
  - 用户可用现成的库（Redlock, StackExchange.Redis）
- **操作**: **删除** `IDistributedLock`, `ILockHandle`

#### ❌ IHealthCheck
- **位置**: `src/Catga/Abstractions/IHealthCheck.cs`
- **问题**:
  - 未使用
  - ASP.NET Core 有 `IHealthCheck`
- **操作**: **删除**

#### ❌ IRpcClient + IRpcServer + RPC 实现
- **位置**: `src/Catga/Abstractions/IRpcClient.cs`, `IRpcServer.cs`, `src/Catga/Rpc/`
- **问题**:
  - RPC 不是 CQRS/Event Sourcing 的核心功能
  - 增加复杂度
  - 用户可用 gRPC, REST
- **操作**: **删除** 所有 RPC 相关代码

---

### 2. **过度封装** (简化)

#### ⚠️ ResultMetadata
- **位置**: `src/Catga/Core/CatgaResult.cs`
- **问题**:
  - `ResultMetadata` 是个 `class`，会堆分配
  - 声称 "pooled for performance"，但没有池化
  - 使用率低
- **操作**: **删除** `ResultMetadata`，`CatgaResult` 不需要 Metadata

#### ⚠️ BaseBehavior
- **位置**: `src/Catga/Core/BaseBehavior.cs`
- **问题**:
  - 提供了一些辅助方法，但不是必须的
  - 每个 Behavior 可以直接实现 `IPipelineBehavior`
- **操作**: **简化** - 变为 static helper，而非 base class

#### ⚠️ SafeRequestHandler
- **位置**: `src/Catga/Core/SafeRequestHandler.cs`
- **问题**:
  - 自动捕获异常的 Handler 基类
  - 但我们已经有 `CatgaResult` 和 ErrorInfo
  - 不需要两层错误处理
- **操作**: **删除** `SafeRequestHandler`

---

### 3. **重复的 Behavior** (合并)

#### ⚠️ TracingBehavior + DistributedTracingBehavior
- **位置**: `src/Catga/Pipeline/Behaviors/TracingBehavior.cs`, `DistributedTracingBehavior.cs`
- **问题**:
  - 两个 Behavior 都做追踪
  - `DistributedTracingBehavior` 更完善
  - 功能重复
- **操作**: **删除** `TracingBehavior`，保留 `DistributedTracingBehavior`

---

### 4. **未使用的抽象类** (删除)

#### ❌ AggregateRoot
- **位置**: `src/Catga/Core/AggregateRoot.cs`
- **问题**:
  - DDD 概念，但 Catga 是 Mediator + CQRS 框架
  - 未在核心流程使用
  - 用户可自己定义
- **操作**: **删除**

#### ❌ ProjectionBase
- **位置**: `src/Catga/Core/ProjectionBase.cs`
- **问题**:
  - Event Sourcing 投影基类
  - 未在核心流程使用
  - 过于抽象
- **操作**: **删除**

#### ❌ CatgaTransactionBase
- **位置**: `src/Catga/Core/CatgaTransactionBase.cs`
- **问题**:
  - 事务抽象
  - 未在核心流程使用
- **操作**: **删除**

---

### 5. **不必要的复杂性** (简化)

#### ⚠️ FastPath
- **位置**: `src/Catga/Core/FastPath.cs`
- **问题**:
  - 优化 0 个 Behavior 的场景
  - 但实际项目总会有至少 1-2 个 Behavior
  - 维护成本 > 收益
- **操作**: **简化** - 只保留核心逻辑，删除过度优化

#### ⚠️ ErrorCodes (刚加的)
- **位置**: `src/Catga/Core/ErrorCodes.cs`
- **问题**:
  - 50+ 错误代码，但大部分场景用不到
  - 过度分类（1xxx, 2xxx, ...）
  - 增加学习成本
- **操作**: **简化** - 只保留 10 个核心错误代码

#### ⚠️ ErrorInfo
- **位置**: `src/Catga/Core/ErrorCodes.cs`
- **问题**:
  - `readonly record struct` + 多个工厂方法
  - 但使用场景简单，直接用字符串即可
- **操作**: **删除** `ErrorInfo`，`CatgaResult` 只需 `ErrorCode` 字符串

---

## 🔨 执行计划

### Phase 1: 删除未使用的抽象 (高优先级)
- [ ] 删除 `IDistributedCache`, `ICacheable`, `CachingBehavior`
- [ ] 删除 `IDistributedLock`, `ILockHandle`
- [ ] 删除 `IHealthCheck`
- [ ] 删除 `IRpcClient`, `IRpcServer`, `src/Catga/Rpc/`
- [ ] 删除 `AggregateRoot`, `ProjectionBase`, `CatgaTransactionBase`
- [ ] 删除 `SafeRequestHandler`

### Phase 2: 简化核心类 (中优先级)
- [ ] 删除 `ResultMetadata` 从 `CatgaResult`
- [ ] 简化 `ErrorCodes` - 只保留 10 个核心错误
- [ ] 删除 `ErrorInfo` - 直接用字符串错误代码
- [ ] 删除 `TracingBehavior`，只保留 `DistributedTracingBehavior`
- [ ] 简化 `BaseBehavior` - 改为 static helper

### Phase 3: 简化优化逻辑 (低优先级)
- [ ] 简化 `FastPath` - 删除过度优化
- [ ] 审查 `HandlerCache` - 确保不过度缓存

---

## 📊 影响评估

| 删除/简化项 | 代码行数 | 影响 | 风险 |
|------------|---------|------|------|
| RPC (全部) | ~500 行 | 删除整个 RPC 功能 | 低（未广泛使用） |
| IDistributedCache | ~100 行 | 删除缓存抽象 | 低 |
| IDistributedLock | ~50 行 | 删除锁抽象 | 低 |
| AggregateRoot/Projection | ~150 行 | 删除 DDD/ES 基类 | 低 |
| ResultMetadata | ~50 行 | CatgaResult 简化 | 中 |
| ErrorInfo | ~100 行 | 错误处理简化 | 中 |
| ErrorCodes (简化) | ~100 行 | 减少错误代码 | 低 |
| SafeRequestHandler | ~80 行 | 删除异常处理基类 | 低 |
| TracingBehavior | ~100 行 | 删除重复追踪 | 低 |
| **总计** | **~1230 行** | **简化约 30% 核心代码** | **整体低风险** |

---

## ✅ 保留的核心功能

### 必须保留:
1. ✅ `ICatgaMediator` - 核心接口
2. ✅ `IMessageTransport` - 传输抽象
3. ✅ `IMessageSerializer` - 序列化抽象
4. ✅ `IEventStore`, `IOutboxStore`, `IInboxStore` - 持久化
5. ✅ `IIdempotencyStore` - 幂等性
6. ✅ `IDeadLetterQueue` - 死信队列
7. ✅ `IPipelineBehavior` - 管道
8. ✅ `CatgaResult` - 结果类型
9. ✅ 核心 Behaviors: Logging, Inbox, Outbox, Idempotency, Retry, DistributedTracing

### 简化但保留:
1. ✅ `ErrorCodes` - 简化为 10 个核心代码
2. ✅ `CatgaResult` - 移除 `ResultMetadata`
3. ✅ `FastPath` - 简化优化逻辑

---

## 🎯 简化后的核心原则

1. **YAGNI (You Aren't Gonna Need It)** - 删除未使用的功能
2. **KISS (Keep It Simple, Stupid)** - 简化复杂抽象
3. **核心聚焦** - 只做 Mediator + CQRS + Event Sourcing
4. **用户自由** - 让用户选择缓存/锁/RPC 实现，不强加抽象

---

## 📝 预期结果

### Before (现在):
- 17 个接口
- 9 个 Behavior
- RPC 功能
- DDD/ES 基类
- 复杂的错误系统
- **~4000 行核心代码**

### After (简化后):
- 10 个核心接口
- 6 个核心 Behavior
- 无 RPC
- 无 DDD 基类
- 简单的错误代码
- **~2800 行核心代码 (-30%)**

---

## ⚠️ 破坏性变更

以下变更会破坏 API（但项目未发布，可接受）:

1. 删除 `ResultMetadata` - `CatgaResult` API 变更
2. 删除 `ErrorInfo` - 错误处理 API 变更
3. 删除 RPC - 整个功能移除
4. 删除 DDD 基类 - 用户需自行定义

---

## 🚀 执行顺序

1. **Phase 1** (1 小时) - 删除未使用抽象，风险低
2. **Phase 2** (1 小时) - 简化核心类，需要更新文档
3. **Phase 3** (30 分钟) - 简化优化逻辑
4. **测试** (30 分钟) - 运行所有测试
5. **文档更新** (30 分钟) - 更新 README 和文档

**总预计时间**: 3-4 小时

---

## 💡 Philosophy

**"Perfect is the enemy of good"**

- 不需要覆盖所有场景
- 不需要最完美的抽象
- 用户可以自己扩展
- 框架只做核心功能
- 简单 > 完美

**Catga = Mediator + CQRS + Event Sourcing，仅此而已！**

