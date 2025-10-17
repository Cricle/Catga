# 🚧 架构重构状态报告

## ✅ Phase 1: 完成 (100%)

**目标**：提升核心组件到 Catga 库

**完成内容**：
- ✅ 移动 CatgaMediator → `src/Catga/Mediator/`
- ✅ 移动 HandlerCache → `src/Catga/Handlers/`
- ✅ 移动 TypedSubscribers → `src/Catga/Handlers/`
- ✅ 移动 PipelineExecutor → `src/Catga/Pipeline/`
- ✅ 移动 8个 Pipeline.Behaviors → `src/Catga/Pipeline/Behaviors/`
- ✅ 移动 SerializationHelper → `src/Catga/Serialization/`
- ✅ 更新项目文件和依赖
- ✅ 修复命名空间和可见性
- ✅ 所有测试通过 (194/194)

**提交**: `20c6c4f`

---

## ⚠️ Phase 2-6: 发现重大架构问题

### 问题 1: Transport 和 Persistence 强耦合 🔴

**位置**: `InMemoryMessageTransport.cs:15`

```csharp
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();
    //                ^^^^^^^^^^^^^^^^^^^^^^^
    //                Persistence 组件直接嵌入 Transport
}
```

**问题**：
- `InMemoryMessageTransport` 内部创建 `InMemoryIdempotencyStore`
- 无法简单拆分为 `Catga.Transport.InMemory` 和 `Catga.Persistence.InMemory`
- 需要重新设计为依赖注入模式

**影响**：
- ❌ 无法独立使用 Transport (总是带着一个 InMemory 的 Idempotency Store)
- ❌ 无法替换 Idempotency Store 实现
- ❌ 违反单一职责原则和依赖倒置原则

---

### 问题 2: DI 扩展方法的复杂依赖 🟡

**位置**: `src/Catga.InMemory/DependencyInjection/`

当前 DI 扩展方法（如 `AddCatgaInMemory()`）同时注册了：
- Transport (InMemoryMessageTransport)
- Stores (InMemoryEventStore, InMemoryReadModelStore, etc.)
- Mediator (CatgaMediator)
- Behaviors (all pipeline behaviors)

**问题**：
- 如果拆分为 `Transport.InMemory` 和 `Persistence.InMemory`，现有的 `AddCatgaInMemory()` 会被破坏
- 所有示例和用户代码都依赖这个方法

**解决方案选项**：
1. 保留 `Catga.InMemory` 作为 Facade，内部引用 `Transport.InMemory` + `Persistence.InMemory`
2. 破坏性更改：用户需要调用 `AddCatgaTransportInMemory()` + `AddCatgaPersistenceInMemory()`

---

### 问题 3: Stores 之间的共享代码 🟡

**位置**: `BaseMemoryStore.cs`, `CatgaJsonSerializerContext.cs`, `SerializationBufferPool.cs`

这些文件被多个 Store 共享：
- `InMemoryEventStore`
- `InMemoryReadModelStore`
- `InMemoryIdempotencyStore`
- `InMemoryDeadLetterQueue`

**问题**：
- 如果拆分，这些共享代码应该放在哪里？
  - 选项 A: Catga 核心库（但这是实现细节，不应该暴露）
  - 选项 B: 创建 `Catga.InMemory.Common`（增加复杂性）
  - 选项 C: 在每个库中复制（违反 DRY）

---

## 🎯 建议方案

### 方案 A: 渐进式重构（推荐） ✅

**Phase 1: ✅ 已完成**
- 核心组件提升到 Catga

**Phase 2-5: 暂停 ⏸️**
- 保持 `Catga.InMemory` 不拆分
- 专注于其他优化（性能、文档、示例）

**Phase 6 (未来): 可选**
- 当有真实需求时再考虑拆分
- 例如：用户希望只使用 InMemory Transport 但用 Redis Persistence

**优势**：
- ✅ Phase 1 已经带来了显著的架构改进
- ✅ 代码保持工作状态
- ✅ 避免破坏性更改
- ✅ 可以专注于其他高价值工作

---

### 方案 B: 继续强行重构（不推荐） ⚠️

**要求**：
1. 重新设计 `InMemoryMessageTransport` 使用 DI 注入 IdempotencyStore
2. 创建 `Catga.InMemory.Common` 共享库
3. 拆分 `Catga.InMemory` → `Transport.InMemory` + `Persistence.InMemory`
4. 更新所有 DI 扩展方法
5. 创建 `Catga.InMemory` Facade 保持向后兼容
6. 修复所有示例和文档
7. 对齐 NATS 和 Redis

**预计工作量**：
- ⏱️ 4-6 小时
- 📝 50+ 文件修改
- ⚠️ 高风险（可能在过程中无法编译）
- 🐛 可能引入新 bug

**收益**：
- 🤔 理论上更清晰（但实际需求不明确）
- 🤔 可以单独使用 Transport 或 Persistence（但目前没有这个需求）

**劣势**：
- ❌ 大量时间投入
- ❌ 破坏性更改风险
- ❌ 收益不明确

---

## 📊 当前状态总结

| Phase | 状态 | 完成度 | 备注 |
|-------|------|--------|------|
| Phase 1: 核心组件提升 | ✅ 完成 | 100% | 编译通过，所有测试通过 |
| Phase 2: Transport.InMemory | ⏸️ 阻塞 | 10% | 发现架构耦合问题 |
| Phase 3: Persistence.InMemory | ⏸️ 未开始 | 0% | 依赖 Phase 2 |
| Phase 4: InMemory Facade | ⏸️ 未开始 | 0% | 依赖 Phase 2-3 |
| Phase 5: 对齐 NATS/Redis | ⏸️ 未开始 | 0% | 依赖 Phase 2-4 |
| Phase 6: 更新文档 | ⏸️ 未开始 | 0% | 依赖所有 |

---

## 🚦 建议的下一步

### 推荐：接受 Phase 1 的成果，继续其他工作 ✅

Phase 1 已经带来了显著的价值：
- ✅ 核心组件 (Mediator, Handlers, Pipeline, Behaviors) 独立于实现
- ✅ 清晰的依赖层次
- ✅ 为未来的扩展打下基础
- ✅ 0 警告，0 错误，所有测试通过

**可以继续的高价值工作**：
1. 性能优化（SIMD, ArrayPool, Span - 已经有详细计划）
2. 完善文档和示例
3. 添加更多单元测试和集成测试
4. 优化 OrderSystem 示例
5. Jaeger 集成优化

### 备选：继续强行重构 ⚠️

如果用户坚持完成完整的重构计划，需要：
1. 确认愿意投入 4-6 小时
2. 接受破坏性更改风险
3. 接受可能在过程中遇到更多意外问题

---

## ❓ 请明确指示

**选项 A**: 接受 Phase 1 的成果，继续其他高价值工作（推荐）

**选项 B**: 继续完成完整的架构重构（需要 4-6 小时，有风险）

**选项 C**: 其他方案（请说明）

