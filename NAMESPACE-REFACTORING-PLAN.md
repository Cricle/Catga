# Catga 命名空间重构计划

> **状态**: 📋 计划中  
> **优先级**: 中  
> **预计时间**: 2-3 小时  
> **影响范围**: 所有项目

---

## 🎯 目标

统一和规范化 Catga 项目的所有命名空间，使其符合 .NET 最佳实践和项目架构。

---

## 📋 问题分析

### 当前问题

1. **命名空间不一致**
   - 部分文件使用细分命名空间（如 `Catga.Idempotency`, `Catga.Inbox`, `Catga.Outbox`）
   - 部分文件使用通用命名空间（如 `Catga.Core`, `Catga.Abstractions`）
   - 导致 `using` 语句混乱

2. **文件夹与命名空间不匹配**
   - `src/Catga/Abstractions` 包含多种命名空间
   - `src/Catga/Core` 包含多种命名空间

3. **接口与实现分离不清晰**
   - 接口散落在不同命名空间
   - 查找困难

---

## 🎯 命名空间规范

### 核心原则

1. **简洁性**: 避免过度嵌套
2. **一致性**: 相同功能使用相同命名空间
3. **可发现性**: 接口和实现在逻辑上接近
4. **AOT 友好**: 命名空间不影响 AOT 编译

### 规范层次

```
Catga                           # 核心抽象和接口
├── Catga.Abstractions          # (废弃，合并到 Catga)
├── Catga.Core                  # 核心实现
├── Catga.Handlers              # 处理器
├── Catga.Messages              # 消息定义
├── Catga.Pipeline              # 管道
│   └── Catga.Pipeline.Behaviors
├── Catga.Mediator              # 中介者
├── Catga.Serialization         # 序列化
├── Catga.Pooling               # 内存池
├── Catga.Observability         # 可观测性
├── Catga.Transport             # 传输层
├── Catga.Persistence           # 持久化层
├── Catga.EventSourcing         # 事件溯源
├── Catga.Rpc                   # RPC
├── Catga.Http                  # HTTP
└── Catga.DependencyInjection   # DI 扩展
```

---

## 📝 重构计划

### Phase 1: 核心抽象层统一 (高优先级)

#### 1.1 合并到 `Catga` 命名空间

**目标**: 所有核心接口使用 `Catga` 根命名空间

| 当前命名空间 | 目标命名空间 | 文件数 | 说明 |
|-------------|-------------|--------|------|
| `Catga.Abstractions` | `Catga` | 1 | IMessageSerializer |
| `Catga.Idempotency` | `Catga` | 1 | IIdempotencyStore |
| `Catga.Inbox` | `Catga` | 1 | IInboxStore |
| `Catga.Outbox` | `Catga` | 1 | IOutboxStore |
| `Catga.EventSourcing` | `Catga` | 2 | IEventStore, EventStoreRepository |
| `Catga.Caching` | `Catga` | 1 | IDistributedCache |
| `Catga.DistributedLock` | `Catga` | 1 | IDistributedLock |
| `Catga.DistributedId` | `Catga` | 1 | IDistributedIdGenerator |
| `Catga.DeadLetter` | `Catga` | 1 | IDeadLetterQueue |
| `Catga.HealthCheck` | `Catga` | 1 | IHealthCheck |
| `Catga.Transport` | `Catga` | 1 | IMessageTransport |
| `Catga.Rpc` | `Catga` | 2 | IRpcClient, IRpcServer |
| `Catga.Exceptions` | `Catga` | 1 | CatgaException |
| `Catga.Configuration` | `Catga` | 1 | CatgaOptions |
| `Catga.Projections` | `Catga` | 1 | ProjectionBase |

**文件清单**:
```
src/Catga/Abstractions/
├── IMessageSerializer.cs       → namespace Catga
├── IBufferedMessageSerializer.cs → namespace Catga.Serialization (保持)
├── IPooledMessageSerializer.cs → namespace Catga.Serialization (保持)
├── IIdempotencyStore.cs        → namespace Catga
├── IInboxStore.cs              → namespace Catga
├── IOutboxStore.cs             → namespace Catga
├── IEventStore.cs              → namespace Catga
├── IDistributedCache.cs        → namespace Catga
├── IDistributedLock.cs         → namespace Catga
├── IDistributedIdGenerator.cs  → namespace Catga
├── IDeadLetterQueue.cs         → namespace Catga
├── IHealthCheck.cs             → namespace Catga
├── IMessageTransport.cs        → namespace Catga
├── IRpcClient.cs               → namespace Catga
├── IRpcServer.cs               → namespace Catga
├── ICatgaMediator.cs           → namespace Catga (已正确)
└── IPipelineBehavior.cs        → namespace Catga (从 Catga.Pipeline 移动)

src/Catga/Core/
├── CatgaException.cs           → namespace Catga
├── CatgaOptions.cs             → namespace Catga
├── CatgaResult.cs              → namespace Catga
├── QualityOfService.cs         → namespace Catga (已正确)
├── DeliveryMode.cs             → namespace Catga
├── AggregateRoot.cs            → namespace Catga
├── EventStoreRepository.cs     → namespace Catga
├── ProjectionBase.cs           → namespace Catga
├── SnowflakeIdGenerator.cs     → namespace Catga (从 Catga.DistributedId)
├── SnowflakeBitLayout.cs       → namespace Catga (从 Catga.DistributedId)
├── DistributedIdOptions.cs     → namespace Catga (从 Catga.DistributedId)
└── CatgaTransactionBase.cs     → namespace Catga (从 Catga.DistributedTransaction)
```

---

### Phase 2: 功能命名空间保持 (保持不变)

**目标**: 功能性命名空间保持当前状态（已经合理）

| 命名空间 | 说明 | 保持原因 |
|---------|------|---------|
| `Catga.Core` | 核心工具类 | 清晰的功能分组 |
| `Catga.Handlers` | 处理器相关 | 独立模块 |
| `Catga.Messages` | 消息定义 | 独立模块 |
| `Catga.Pipeline` | 管道执行器 | 独立模块 |
| `Catga.Pipeline.Behaviors` | 管道行为 | 逻辑子模块 |
| `Catga.Mediator` | 中介者实现 | 独立模块 |
| `Catga.Serialization` | 序列化 | 独立模块 |
| `Catga.Pooling` | 内存池 | 独立模块 |
| `Catga.Observability` | 可观测性 | 独立模块 |
| `Catga.Rpc` | RPC 实现 | 独立模块 |
| `Catga.Http` | HTTP 相关 | 独立模块 |
| `Catga.DependencyInjection` | DI 扩展 | 独立模块 |

---

### Phase 3: Transport 和 Persistence 项目 (低优先级)

**目标**: 统一 Transport 和 Persistence 项目的命名空间

#### 3.1 Transport 项目

| 项目 | 当前命名空间 | 目标命名空间 | 说明 |
|------|-------------|-------------|------|
| `Catga.Transport.InMemory` | `Catga.Transport` | 保持 | 已正确 |
| `Catga.Transport.Nats` | `Catga.Transport.Nats` | 保持 | 已正确 |
| `Catga.Transport.Redis` | `Catga.Transport.Redis` | 保持 | 已正确 |

#### 3.2 Persistence 项目

| 项目 | 当前命名空间 | 目标命名空间 | 说明 |
|------|-------------|-------------|------|
| `Catga.Persistence.InMemory` | 多个 | `Catga.Persistence.InMemory` | 需统一 |
| `Catga.Persistence.Nats` | `Catga.Persistence.Nats` | 保持 | 已正确 |
| `Catga.Persistence.Redis` | `Catga.Persistence.Redis` | 保持 | 已正确 |

**Catga.Persistence.InMemory 需要修复**:
```
DependencyInjection/*  → namespace Catga.Persistence.InMemory.DependencyInjection
Stores/*               → namespace Catga.Persistence.InMemory.Stores
其他文件               → namespace Catga.Persistence.InMemory
```

---

### Phase 4: 辅助项目 (低优先级)

#### 4.1 AspNetCore 项目

| 文件 | 当前命名空间 | 目标命名空间 |
|------|-------------|-------------|
| `CatgaDiagnosticsEndpoint.cs` | `Catga.AspNetCore` | 保持 |
| `CatgaApplicationBuilderExtensions.cs` | `Microsoft.Extensions.DependencyInjection` | 保持（约定） |
| `Middleware/*` | `Catga.AspNetCore.Middleware` | 保持 |
| `Extensions/*` | `Catga.AspNetCore.Extensions` | 保持 |
| `Rpc/*` | `Catga.AspNetCore.Rpc` | 保持 |

#### 4.2 Hosting.Aspire 项目

| 文件 | 当前命名空间 | 目标命名空间 |
|------|-------------|-------------|
| `CatgaHealthCheck.cs` | `Aspire.Hosting` | 保持（Aspire 约定） |
| `CatgaResourceExtensions.cs` | `Aspire.Hosting` | 保持（Aspire 约定） |

#### 4.3 Serialization 项目

| 项目 | 命名空间 | 说明 |
|------|---------|------|
| `Catga.Serialization.Json` | `Catga.Serialization.Json` | 已正确 |
| `Catga.Serialization.MemoryPack` | `Catga.Serialization.MemoryPack` | 已正确 |

---

## 🔧 实施步骤

### Step 1: 准备阶段 (5分钟)

1. 创建新分支
   ```bash
   git checkout -b refactor/namespace-unification
   ```

2. 备份当前状态
   ```bash
   git tag backup-before-namespace-refactoring
   ```

### Step 2: Phase 1 实施 (60分钟)

**批量修改命名空间**:

```bash
# 示例：批量替换
# 注意：需要手动检查每个文件的 using 语句

# 1. IIdempotencyStore.cs
namespace Catga.Idempotency; → namespace Catga;

# 2. IInboxStore.cs
namespace Catga.Inbox; → namespace Catga;

# 3. IOutboxStore.cs
namespace Catga.Outbox; → namespace Catga;

# 4. IEventStore.cs
namespace Catga.EventSourcing; → namespace Catga;

# 5. EventStoreRepository.cs
namespace Catga.EventSourcing; → namespace Catga;

# 6. IDistributedCache.cs
namespace Catga.Caching; → namespace Catga;

# 7. IDistributedLock.cs
namespace Catga.DistributedLock; → namespace Catga;

# 8. IDistributedIdGenerator.cs
namespace Catga.DistributedId; → namespace Catga;

# 9. SnowflakeIdGenerator.cs
namespace Catga.DistributedId; → namespace Catga;

# 10. SnowflakeBitLayout.cs
namespace Catga.DistributedId; → namespace Catga;

# 11. DistributedIdOptions.cs
namespace Catga.DistributedId; → namespace Catga;

# 12. IDeadLetterQueue.cs
namespace Catga.DeadLetter; → namespace Catga;

# 13. IHealthCheck.cs
namespace Catga.HealthCheck; → namespace Catga;

# 14. IMessageTransport.cs
namespace Catga.Transport; → namespace Catga;

# 15. IRpcClient.cs
namespace Catga.Rpc; → namespace Catga;

# 16. IRpcServer.cs
namespace Catga.Rpc; → namespace Catga;

# 17. CatgaException.cs
namespace Catga.Exceptions; → namespace Catga;

# 18. CatgaOptions.cs
namespace Catga.Configuration; → namespace Catga;

# 19. ProjectionBase.cs
namespace Catga.Projections; → namespace Catga;

# 20. CatgaTransactionBase.cs
namespace Catga.DistributedTransaction; → namespace Catga;

# 21. IPipelineBehavior.cs
namespace Catga.Pipeline; → namespace Catga;

# 22. DeliveryMode.cs
namespace Catga.Core; → namespace Catga;

# 23. AggregateRoot.cs
namespace Catga.Core; → namespace Catga;

# 24. IMessageSerializer.cs
namespace Catga.Abstractions; → namespace Catga;
```

### Step 3: 更新所有 using 语句 (30分钟)

**自动化工具**:
```bash
# 使用 Visual Studio 的 "Remove and Sort Usings"
# 或者使用 dotnet format

dotnet format --verify-no-changes
```

**手动检查重点**:
- Transport 项目
- Persistence 项目
- Pipeline Behaviors
- Tests 项目

### Step 4: 编译验证 (10分钟)

```bash
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

### Step 5: 修复编译错误 (20分钟)

**常见问题**:
1. 命名空间冲突
2. Using 语句缺失
3. 类型查找失败

### Step 6: Phase 2 & 3 实施 (可选，30分钟)

根据时间和优先级决定是否实施。

### Step 7: 文档更新 (10分钟)

更新以下文档：
- `README.md`
- `docs/architecture.md`
- `AI-LEARNING-GUIDE.md`
- 代码示例

### Step 8: 提交和验证 (5分钟)

```bash
git add -A
git commit -m "refactor(namespace): Unify core abstractions to Catga namespace

♻️ 命名空间重构

Phase 1: 核心抽象层统一
• 24 个接口/类移至 Catga 根命名空间
• 统一接口命名空间
• 简化 using 语句

影响文件: ~150 个
修改类型: 
  • 命名空间声明
  • Using 语句
  • XML 文档

兼容性: 
  ⚠️ 破坏性变更
  需要更新引用

编译状态: ✅ 通过
测试状态: ✅ 通过"
```

---

## 📊 影响评估

### 破坏性变更

**影响范围**:
- ✅ 内部代码: 150+ 文件
- ⚠️ 公共 API: 24 个接口
- ⚠️ 外部引用: 需要更新 using

**迁移成本**:
- 小型项目: 5-10 分钟（查找替换）
- 中型项目: 15-30 分钟
- 大型项目: 30-60 分钟

### 迁移指南

**自动迁移**:
```csharp
// 旧命名空间
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Outbox;
using Catga.EventSourcing;
using Catga.Caching;
using Catga.DistributedLock;
using Catga.DistributedId;
using Catga.DeadLetter;
using Catga.HealthCheck;
using Catga.Transport;
using Catga.Exceptions;
using Catga.Configuration;
using Catga.Projections;
using Catga.DistributedTransaction;

// 新命名空间（全部统一）
using Catga;
```

---

## ✅ 验证清单

### 编译验证
- [ ] `dotnet build --configuration Release` 通过
- [ ] 无编译警告
- [ ] 无编译错误

### 测试验证
- [ ] `dotnet test --configuration Release` 通过
- [ ] 所有单元测试通过
- [ ] 所有集成测试通过

### 功能验证
- [ ] 示例项目正常运行
- [ ] Benchmark 项目正常运行
- [ ] 文档代码示例正确

### 文档验证
- [ ] README.md 更新
- [ ] Architecture 文档更新
- [ ] API 文档更新
- [ ] AI Learning Guide 更新

---

## 🎯 预期收益

### 开发体验提升
- ✅ **简化 using 语句**: 从 15 个减少到 1-2 个
- ✅ **提升可发现性**: 所有核心接口在 `Catga` 命名空间
- ✅ **减少命名空间混乱**: 统一的命名规范
- ✅ **更好的 IntelliSense**: 更清晰的类型提示

### 代码质量提升
- ✅ **一致性**: 统一的命名规范
- ✅ **可维护性**: 更清晰的代码结构
- ✅ **可读性**: 更少的 using 语句

### 性能影响
- ✅ **编译时间**: 无影响
- ✅ **运行时性能**: 无影响
- ✅ **AOT 兼容性**: 无影响

---

## 📅 时间估算

| 阶段 | 预计时间 | 说明 |
|------|---------|------|
| **准备** | 5 分钟 | 创建分支、备份 |
| **Phase 1** | 60 分钟 | 核心抽象层统一 |
| **Using 更新** | 30 分钟 | 自动化 + 手动检查 |
| **编译验证** | 10 分钟 | 构建 + 测试 |
| **错误修复** | 20 分钟 | 修复编译错误 |
| **文档更新** | 10 分钟 | 更新文档 |
| **提交验证** | 5 分钟 | 提交代码 |
| **总计** | **2-2.5 小时** | Phase 1 完整实施 |

**可选**:
- Phase 2 & 3: +30 分钟
- 全面文档更新: +30 分钟

---

## 🚀 执行建议

### 推荐方案: 分阶段实施

**第一批 (Phase 1)**: 立即执行
- 核心抽象层统一
- 影响最大，收益最高
- 预计时间: 2-2.5 小时

**第二批 (Phase 2 & 3)**: 可选
- 功能命名空间优化
- 影响较小
- 预计时间: 0.5-1 小时

### 回滚方案

如果遇到问题：
```bash
# 方案 1: 恢复分支
git checkout master
git branch -D refactor/namespace-unification

# 方案 2: 恢复标签
git reset --hard backup-before-namespace-refactoring
```

---

## 📝 注意事项

1. **破坏性变更**: 需要在 CHANGELOG.md 中明确标注
2. **版本号**: 建议升级到 v2.0.0 (Major version bump)
3. **迁移文档**: 提供详细的迁移指南
4. **发布说明**: 在 Release Notes 中突出显示
5. **向后兼容**: 不提供，清晰地标记为破坏性变更

---

**最后更新**: 2024-01-20  
**状态**: 待执行  
**优先级**: 中  
**风险**: 中（破坏性变更）

