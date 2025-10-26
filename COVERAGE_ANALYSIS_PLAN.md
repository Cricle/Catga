# 📊 代码覆盖率提升计划：从 26% → 90%

## 📈 当前覆盖率基线 (2025-10-27)

### 总体覆盖率
- **行覆盖率**: 26.09% ❌
- **分支覆盖率**: 22.29% ❌
- **目标覆盖率**: 90% 🎯

### 各包覆盖率详情

| 包名 | 当前覆盖率 | 目标覆盖率 | 差距 | 优先级 |
|------|-----------|-----------|------|--------|
| **Catga (核心)** | 38.81% | 95% | +56.19% | 🔴 P0 (最高) |
| Catga.Transport.InMemory | 81.87% | 95% | +13.13% | 🟢 P3 (低) |
| Catga.Serialization.MemoryPack | 50% | 90% | +40% | 🟡 P2 (中) |
| Catga.Serialization.Json | 44% | 90% | +46% | 🟡 P2 (中) |
| Catga.Persistence.InMemory | 24.67% | 95% | +70.33% | 🔴 P0 (最高) |
| Catga.Persistence.Redis | 6.29% | 80% | +73.71% | 🟠 P1 (高) |
| Catga.Persistence.Nats | 0% | 75% | +75% | 🟠 P1 (高) |
| Catga.Transport.Redis | 0% | 75% | +75% | 🟠 P1 (高) |
| Catga.Transport.Nats | 0% | 75% | +75% | 🟠 P1 (高) |

---

## 🎯 第一阶段：核心组件覆盖 (P0) - 目标 60%

### 任务 1.1: Catga (核心) - 38.81% → 95%

#### 需要新增测试的组件：

**核心类 (src/Catga/Core/)**:
- [ ] `BatchOperationExtensions.cs` - 批量操作扩展方法
- [ ] `BatchOperationHelper.cs` - 批量操作辅助类
- [ ] `CatgaException.cs` - 自定义异常类型
- [ ] `CatgaOptions.cs` - 配置选项
- [ ] `FastPath.cs` - 快速路径优化
- [ ] `GracefulRecovery.cs` - 优雅恢复机制
- [ ] `GracefulShutdown.cs` - 优雅关闭机制
- [ ] `MemoryPoolManager.cs` - 内存池管理器
- [ ] `MessageExtensions.cs` - 消息扩展方法
- [ ] `MessageHelper.cs` - 消息辅助类
- [ ] `PooledBufferWriter.cs` - 池化缓冲区写入器
- [ ] `SerializationExtensions.cs` - 序列化扩展
- [ ] `TypeNameCache.cs` - 类型名称缓存
- [ ] `ValidationHelper.cs` - 验证辅助类
- [ ] `QualityOfService.cs` - 服务质量枚举
- [ ] `DeliveryMode.cs` - 传递模式枚举
- [ ] `ErrorCodes.cs` - 错误代码常量

**Pipeline (src/Catga/Pipeline/)**:
- [ ] `PipelineExecutor.cs` - Pipeline执行器
- [ ] `Behaviors/DistributedTracingBehavior.cs` - 分布式追踪行为
- [ ] `Behaviors/InboxBehavior.cs` - Inbox模式行为
- [ ] `Behaviors/OutboxBehavior.cs` - Outbox模式行为
- [ ] `Behaviors/ValidationBehavior.cs` - 验证行为

**DependencyInjection (src/Catga/DependencyInjection/)**:
- [ ] `CatgaServiceBuilder.cs` - 服务构建器
- [ ] `CorrelationIdDelegatingHandler.cs` - CorrelationId HTTP处理器

**Observability (src/Catga/Observability/)**:
- [ ] `ActivityPayloadCapture.cs` - Activity负载捕获
- [ ] `CatgaActivitySource.cs` - Activity源
- [ ] `CatgaDiagnostics.cs` - 诊断信息
- [ ] `CatgaLog.cs` - 日志记录

**Serialization**:
- [ ] `Serialization.cs` - 序列化基类

### 任务 1.2: Catga.Persistence.InMemory - 24.67% → 95%

**需要测试的类**:
- [ ] `InMemoryDeadLetterQueue.cs` - 内存死信队列
- [ ] `InMemoryEventStore.cs` - 内存事件存储
- [ ] `InMemoryIdempotencyStore.cs` - 内存幂等性存储
- [ ] `InMemoryInboxStore.cs` - 内存Inbox存储
- [ ] `InMemoryOutboxStore.cs` - 内存Outbox存储

---

## 🎯 第二阶段：序列化和传输 (P2) - 目标 75%

### 任务 2.1: Catga.Serialization.Json - 44% → 90%
- [ ] 测试序列化复杂对象
- [ ] 测试反序列化边界情况
- [ ] 测试序列化错误处理
- [ ] 测试AOT兼容性场景

### 任务 2.2: Catga.Serialization.MemoryPack - 50% → 90%
- [ ] 测试二进制序列化
- [ ] 测试大对象序列化性能
- [ ] 测试反序列化错误处理
- [ ] 测试内存优化场景

### 任务 2.3: Catga.Transport.InMemory - 81.87% → 95%
- [ ] 补充边界情况测试
- [ ] 测试并发订阅/取消订阅
- [ ] 测试消息传递失败场景

---

## 🎯 第三阶段：外部依赖组件 (P1) - 目标 80%

### 任务 3.1: Catga.Persistence.Redis - 6.29% → 80%
- [ ] 使用 Testcontainers 或 Mock 测试
- [ ] 测试基本的CRUD操作
- [ ] 测试连接失败场景
- [ ] 测试并发写入
- [ ] 测试事务场景

### 任务 3.2: Catga.Persistence.Nats - 0% → 75%
- [ ] 使用 Testcontainers 或 Mock 测试
- [ ] 测试JetStream基本操作
- [ ] 测试订阅和发布
- [ ] 测试事件存储

### 任务 3.3: Catga.Transport.Redis - 0% → 75%
- [ ] 使用 Testcontainers 或 Mock 测试
- [ ] 测试消息发送/接收
- [ ] 测试PubSub模式
- [ ] 测试连接重试

### 任务 3.4: Catga.Transport.Nats - 0% → 75%
- [ ] 使用 Testcontainers 或 Mock 测试
- [ ] 测试消息传输
- [ ] 测试订阅管理
- [ ] 测试错误恢复

---

## 📝 实施步骤

### Step 1: 核心组件测试 (预计 5-7 天)
1. 创建测试文件结构
2. 为每个核心类编写单元测试（至少 80% 覆盖）
3. 运行覆盖率验证

### Step 2: 序列化和内存传输 (预计 2-3 天)
1. 扩展序列化测试
2. 补充传输层测试
3. 验证覆盖率达到目标

### Step 3: 外部依赖测试 (预计 3-4 天)
1. 配置 Testcontainers 或 Mock
2. 实现 Redis 和 NATS 测试
3. 验证覆盖率目标

### Step 4: 边界情况和集成 (预计 1-2 天)
1. 补充边界情况测试
2. 确保所有包达到目标覆盖率
3. 生成最终覆盖率报告

---

## 📊 预期结果

### 最终目标覆盖率分布

| 包名 | 最终目标 | 当前 | 提升 |
|------|---------|------|------|
| Catga (核心) | 95% | 38.81% | +56.19% |
| Catga.Persistence.InMemory | 95% | 24.67% | +70.33% |
| Catga.Serialization.Json | 90% | 44% | +46% |
| Catga.Serialization.MemoryPack | 90% | 50% | +40% |
| Catga.Transport.InMemory | 95% | 81.87% | +13.13% |
| Catga.Persistence.Redis | 80% | 6.29% | +73.71% |
| Catga.Persistence.Nats | 75% | 0% | +75% |
| Catga.Transport.Redis | 75% | 0% | +75% |
| Catga.Transport.Nats | 75% | 0% | +75% |
| **总体** | **90%+** | **26.09%** | **+63.91%** |

---

## 🛠️ 工具和技术

1. **代码覆盖率工具**: Coverlet (XPlat Code Coverage)
2. **测试框架**: xUnit
3. **断言库**: FluentAssertions
4. **Mock 库**: NSubstitute / Moq
5. **容器测试**: Testcontainers (用于 Redis 和 NATS)
6. **覆盖率报告**: ReportGenerator

---

## ⚠️ 注意事项

1. **优先级**: 先完成 P0 核心组件，确保基础覆盖率达到 60%
2. **质量优先**: 测试质量比数量更重要，确保测试有意义
3. **避免虚假覆盖**: 不要为了覆盖率而写无意义的测试
4. **持续集成**: 每个阶段完成后运行覆盖率验证
5. **文档同步**: 更新测试文档和示例

---

## 📅 时间表

| 阶段 | 任务 | 预计时间 | 目标覆盖率 |
|------|------|---------|-----------|
| Phase 1 | 核心组件 (P0) | 5-7 天 | 60% |
| Phase 2 | 序列化传输 (P2) | 2-3 天 | 75% |
| Phase 3 | 外部依赖 (P1) | 3-4 天 | 85% |
| Phase 4 | 边界情况 | 1-2 天 | 90%+ |
| **总计** | | **11-16 天** | **90%+** |

---

## ✅ 验收标准

- [x] 总体行覆盖率 ≥ 90%
- [x] 核心包 (Catga) 覆盖率 ≥ 95%
- [x] 内存存储/传输 覆盖率 ≥ 95%
- [x] 序列化组件 覆盖率 ≥ 90%
- [x] 外部依赖组件 覆盖率 ≥ 75%
- [x] 所有测试必须通过
- [x] 没有虚假或无意义的测试
- [x] 测试文档完整

---

**生成时间**: 2025-10-27
**当前覆盖率**: 26.09%
**目标覆盖率**: 90%+
**提升需求**: +63.91%

