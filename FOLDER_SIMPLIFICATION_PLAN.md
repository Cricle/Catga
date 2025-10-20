# Catga 文件夹精简计划

## 📊 当前状态

**文件夹数量**: ~46 个（包括 bin/obj）
**核心源码文件夹**: 14 个

### 当前结构
```
src/Catga/
├── Abstractions/      (11 files) - 接口定义
├── Core/              (18 files) - 核心类
├── DependencyInjection/ (2 files) - DI扩展
├── Handlers/          (2 files) - Handler相关
├── Http/              (1 file)  - HTTP扩展
├── Mediator/          (1 file)  - Mediator实现
├── Messages/          (3 files) - 消息定义
├── Observability/     (4 files) - 监控
├── Pipeline/          (1 file + Behaviors/)
│   └── Behaviors/     (7 files) - 管道行为
├── Polyfills/         (2 files) - 兼容性
├── Pooling/           (2 files) - 内存池
├── Rpc/               (0 files) - 空文件夹 ❌
├── Serialization/     (1 file)  - 序列化
└── Common/            (空?)
```

---

## 🎯 精简策略

### Phase 1: 删除空文件夹

**删除** (2个):
- `Rpc/` - 空文件夹
- `Common/` - 空文件夹（如果为空）

---

### Phase 2: 合并单文件文件夹

**原则**: 只有1-2个文件的文件夹可以合并到父级或相关文件夹

#### 2.1 合并到根目录
- `Mediator/CatgaMediator.cs` → `CatgaMediator.cs` (根目录)
- `Serialization/Serialization.cs` → `Serialization.cs` (根目录)

#### 2.2 合并 Http/ 到 DependencyInjection/
- `Http/CorrelationIdDelegatingHandler.cs` → `DependencyInjection/`

#### 2.3 合并 Handlers/ 到核心
- `Handlers/HandlerCache.cs` → `Core/`
- `Handlers/HandlerContracts.cs` → `Abstractions/` (是接口定义)

---

### Phase 3: 重组核心文件夹

#### 3.1 将小文件夹合并到 Core/
- `Pooling/` (2 files) → `Core/Pooling/` 或直接放 `Core/`
- `Polyfills/` (2 files) → 保留（.NET 6兼容性）
- `DependencyInjection/` (3 files) → 保留（DI相关）

#### 3.2 合并 Messages/ 到 Abstractions/
- `Messages/MessageContracts.cs` → `Abstractions/`
- `Messages/MessageExtensions.cs` → `Core/`
- `Messages/MessageIdentifiers.cs` → `Abstractions/`

---

## 🎨 精简后结构

```
src/Catga/
├── Abstractions/          (15 files) ⬆️ +4
│   ├── ICatgaMediator.cs
│   ├── IDeadLetterQueue.cs
│   ├── IDistributedIdGenerator.cs
│   ├── IEventStore.cs
│   ├── IIdempotencyStore.cs
│   ├── IInboxStore.cs
│   ├── IMessageMetadata.cs
│   ├── IMessageSerializer.cs
│   ├── IMessageTransport.cs
│   ├── IOutboxStore.cs
│   ├── IPipelineBehavior.cs
│   ├── HandlerContracts.cs      (from Handlers/)
│   ├── MessageContracts.cs      (from Messages/)
│   └── MessageIdentifiers.cs    (from Messages/)
│
├── Core/                  (22 files) ⬆️ +4
│   ├── BaseBehavior.cs
│   ├── BatchOperationExtensions.cs
│   ├── BatchOperationHelper.cs
│   ├── CatgaException.cs
│   ├── CatgaOptions.cs
│   ├── CatgaResult.cs
│   ├── DeliveryMode.cs
│   ├── DistributedIdOptions.cs
│   ├── ErrorCodes.cs
│   ├── FastPath.cs
│   ├── GracefulRecovery.cs
│   ├── GracefulShutdown.cs
│   ├── MessageHelper.cs
│   ├── QualityOfService.cs
│   ├── SnowflakeBitLayout.cs
│   ├── SnowflakeIdGenerator.cs
│   ├── TypeNameCache.cs
│   ├── ValidationHelper.cs
│   ├── HandlerCache.cs          (from Handlers/)
│   ├── MessageExtensions.cs     (from Messages/)
│   ├── MemoryPoolManager.cs     (from Pooling/)
│   └── PooledBufferWriter.cs    (from Pooling/)
│
├── DependencyInjection/   (3 files) ⬆️ +1
│   ├── CatgaServiceBuilder.cs
│   ├── CatgaServiceCollectionExtensions.cs
│   └── CorrelationIdDelegatingHandler.cs (from Http/)
│
├── Observability/         (4 files) ✅ 保留
│   ├── ActivityPayloadCapture.cs
│   ├── CatgaActivitySource.cs
│   ├── CatgaDiagnostics.cs
│   └── CatgaLog.cs
│
├── Pipeline/              (1 file + Behaviors/) ✅ 保留
│   ├── PipelineExecutor.cs
│   └── Behaviors/         (7 files)
│       ├── DistributedTracingBehavior.cs
│       ├── IdempotencyBehavior.cs
│       ├── InboxBehavior.cs
│       ├── LoggingBehavior.cs
│       ├── OutboxBehavior.cs
│       ├── RetryBehavior.cs
│       └── ValidationBehavior.cs
│
├── Polyfills/             (2 files) ✅ 保留
│   ├── RequiredMemberAttribute.cs
│   └── RequiresDynamicCodeAttribute.cs
│
├── CatgaMediator.cs       (from Mediator/) ⬆️ 新增
├── Serialization.cs       (from Serialization/) ⬆️ 新增
├── Catga.csproj
└── README.md
```

---

## 📊 精简效果

| 指标 | Before | After | 改进 |
|------|--------|-------|------|
| **文件夹** | 14 个 | 6 个 | **-57%** |
| **单文件文件夹** | 5 个 | 0 个 | **-100%** |
| **空文件夹** | 2 个 | 0 个 | **-100%** |
| **核心文件** | 54 个 | 54 个 | ✅ 无变化 |

---

## 🔧 执行顺序

### Phase 1: 删除空文件夹
```bash
rm -rf src/Catga/Rpc
rm -rf src/Catga/Common  # 如果为空
```

### Phase 2: 移动单文件
```bash
mv src/Catga/Mediator/CatgaMediator.cs src/Catga/
mv src/Catga/Serialization/Serialization.cs src/Catga/
```

### Phase 3: 合并文件夹
```bash
# Handlers/ → Core/ & Abstractions/
mv src/Catga/Handlers/HandlerCache.cs src/Catga/Core/
mv src/Catga/Handlers/HandlerContracts.cs src/Catga/Abstractions/

# Http/ → DependencyInjection/
mv src/Catga/Http/CorrelationIdDelegatingHandler.cs src/Catga/DependencyInjection/

# Messages/ → Core/ & Abstractions/
mv src/Catga/Messages/MessageContracts.cs src/Catga/Abstractions/
mv src/Catga/Messages/MessageIdentifiers.cs src/Catga/Abstractions/
mv src/Catga/Messages/MessageExtensions.cs src/Catga/Core/

# Pooling/ → Core/
mv src/Catga/Pooling/MemoryPoolManager.cs src/Catga/Core/
mv src/Catga/Pooling/PooledBufferWriter.cs src/Catga/Core/
```

### Phase 4: 删除空文件夹
```bash
rmdir src/Catga/Mediator
rmdir src/Catga/Serialization
rmdir src/Catga/Handlers
rmdir src/Catga/Http
rmdir src/Catga/Messages
rmdir src/Catga/Pooling
```

### Phase 5: 更新命名空间
需要更新以下文件的命名空间：
- `CatgaMediator.cs`: `Catga.Mediator` → `Catga`
- `Serialization.cs`: `Catga.Serialization` → `Catga`
- 等等...

---

## ⚠️ 风险评估

**低风险**:
- ✅ 删除空文件夹
- ✅ 移动单文件到根目录

**中风险**:
- ⚠️ 命名空间变更（需要更新所有引用）
- ⚠️ 合并文件夹（需要更新项目文件）

---

## 🎯 建议

**推荐执行**: Phase 1 + Phase 2 (保守方案)
- 删除空文件夹
- 移动单文件到根目录
- **不变更命名空间**（保持兼容性）

**可选执行**: Phase 3 + Phase 4 (激进方案)
- 合并所有小文件夹
- 更新命名空间
- **破坏性变更**

---

## ❓ 用户选择

请选择执行方案：

**A. 保守方案** - 只删除空文件夹和移动单文件（推荐）
**B. 激进方案** - 完整合并，最大化精简
**C. 自定义** - 您指定哪些文件夹要合并

