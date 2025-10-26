# 🚀 代码覆盖率实施路线图

## 📊 当前状态 (详细分析)

### 总体指标
- **总方法数**: 714
- **已覆盖方法**: 233 (32.6%)
- **完全覆盖方法**: 193 (27%)
- **总行数**: 10,516
- **可覆盖行数**: 3,648
- **已覆盖行数**: 952 (26%)
- **未覆盖行数**: 2,696 (74%)
- **总分支数**: 1,372
- **已覆盖分支**: 302 (22%)

---

## 🎯 Phase 1: 核心组件 - 优先级 P0 (第1-3天)

### 目标: 将核心包从 38.8% 提升到 70%+

#### 1.1 完全未覆盖的关键类 (0% → 90%)

##### 文件: `tests/Catga.Tests/Core/GracefulRecoveryTests.cs` (新建)
测试 `GracefulRecoveryManager` - 优雅恢复管理器
- [ ] Recovery基本功能
- [ ] 多种恢复策略
- [ ] 恢复失败处理
- [ ] 并发恢复场景

##### 文件: `tests/Catga.Tests/Core/GracefulShutdownTests.cs` (新建)
测试 `GracefulShutdownCoordinator` - 优雅关闭协调器
- [ ] 正常关闭流程
- [ ] 超时处理
- [ ] 强制关闭
- [ ] 资源清理验证

##### 文件: `tests/Catga.Tests/Core/MessageHelperTests.cs` (新建)
测试 `MessageHelper` - 消息辅助类
- [ ] 消息创建
- [ ] 消息验证
- [ ] 消息转换
- [ ] 边界情况

##### 文件: `tests/Catga.Tests/Core/FastPathTests.cs` (新建)
测试 `FastPath` (25% → 90%)
- [ ] 快速路径优化场景
- [ ] 性能关键路径
- [ ] 缓存命中/未命中
- [ ] 并发访问

#### 1.2 Pipeline Behaviors (0% → 85%)

##### 文件: `tests/Catga.Tests/Pipeline/DistributedTracingBehaviorTests.cs` (新建)
测试 `DistributedTracingBehavior`
- [ ] Activity创建和传播
- [ ] Trace ID传递
- [ ] Span创建
- [ ] 错误记录

##### 文件: `tests/Catga.Tests/Pipeline/InboxBehaviorTests.cs` (新建)
测试 `InboxBehavior`
- [ ] 消息去重
- [ ] Inbox存储
- [ ] 幂等性保证
- [ ] 并发处理

##### 文件: `tests/Catga.Tests/Pipeline/OutboxBehaviorTests.cs` (新建)
测试 `OutboxBehavior`
- [ ] 消息暂存
- [ ] Outbox发送
- [ ] 事务保证
- [ ] 重试机制

##### 文件: `tests/Catga.Tests/Pipeline/ValidationBehaviorTests.cs` (新建)
测试 `ValidationBehavior`
- [ ] 请求验证
- [ ] 自定义验证器
- [ ] 验证失败处理
- [ ] 多验证器组合

##### 文件: `tests/Catga.Tests/Pipeline/PipelineExecutorTests.cs` (新建)
测试 `PipelineExecutor`
- [ ] Pipeline执行流程
- [ ] 多Behavior链式调用
- [ ] 中断处理
- [ ] 性能验证

#### 1.3 Observability (低覆盖率 → 85%)

##### 文件: `tests/Catga.Tests/Observability/ActivityPayloadCaptureTests.cs` (新建)
测试 `ActivityPayloadCapture` (0% → 90%)
- [ ] 负载捕获
- [ ] 数据序列化
- [ ] 大负载处理
- [ ] 敏感数据过滤

##### 文件: `tests/Catga.Tests/Observability/CatgaActivitySourceTests.cs` (新建)
测试 `CatgaActivitySource` (5.5% → 90%)
- [ ] Activity创建
- [ ] Tag添加
- [ ] Event记录
- [ ] 嵌套Activity

##### 文件: `tests/Catga.Tests/Observability/CatgaLogTests.cs` (新建)
测试 `CatgaLog` (8.6% → 85%)
- [ ] 结构化日志
- [ ] 日志级别
- [ ] 异常日志
- [ ] 性能日志

#### 1.4 DependencyInjection (低覆盖率 → 85%)

##### 文件: `tests/Catga.Tests/DependencyInjection/CatgaServiceBuilderTests.cs` (新建)
测试 `CatgaServiceBuilder` (5.8% → 90%)
- [ ] 服务注册
- [ ] 配置选项
- [ ] Behavior注册
- [ ] 链式调用

##### 文件: `tests/Catga.Tests/DependencyInjection/CorrelationIdHandlerTests.cs` (新建)
测试 `CorrelationIdDelegatingHandler` (0% → 90%)
- [ ] HTTP请求CorrelationId传播
- [ ] Header注入
- [ ] 响应处理
- [ ] 错误场景

#### 1.5 Core Utilities (低覆盖率 → 85%)

##### 文件: `tests/Catga.Tests/Core/ValidationHelperTests.cs` (新建)
测试 `ValidationHelper` (8.6% → 90%)
- [ ] 参数验证
- [ ] 自定义验证规则
- [ ] 验证错误信息
- [ ] 性能验证

##### 文件: `tests/Catga.Tests/Core/MemoryPoolManagerTests.cs` (新建)
测试 `MemoryPoolManager` (33.3% → 85%)
- [ ] 内存池分配
- [ ] 内存回收
- [ ] 池大小管理
- [ ] 并发访问

##### 文件: `tests/Catga.Tests/Core/PooledBufferWriterTests.cs` (新建)
测试 `PooledBufferWriter` (68.3% → 90%)
- [ ] 缓冲区写入
- [ ] 自动扩展
- [ ] 资源释放
- [ ] 边界情况

##### 文件: `tests/Catga.Tests/Core/BatchOperationHelperTests.cs` (新建)
测试 `BatchOperationHelper` (22.2% → 85%)
- [ ] 批量操作辅助
- [ ] 批量错误处理
- [ ] 批量结果聚合
- [ ] 并发批处理

---

## 🎯 Phase 2: 持久化层 - 优先级 P0 (第4-5天)

### 目标: InMemory 从 24.6% 提升到 90%+

#### 2.1 InMemory Stores

##### 文件: `tests/Catga.Tests/Persistence/InMemory/InMemoryDeadLetterQueueTests.cs` (新建)
测试 `InMemoryDeadLetterQueue` (27.2% → 95%)
- [ ] 死信消息存储
- [ ] 消息检索
- [ ] 消息重试
- [ ] 消息过期

##### 文件: `tests/Catga.Tests/Persistence/InMemory/InMemoryEventStoreTests.cs` (新建)
测试 `InMemoryEventStore` (1.4% → 95%)
- [ ] 事件追加
- [ ] 事件查询
- [ ] 事件流读取
- [ ] 并发写入

##### 文件: `tests/Catga.Tests/Persistence/InMemory/MemoryInboxStoreTests.cs` (新建)
测试 `MemoryInboxStore` (0% → 95%)
- [ ] Inbox消息存储
- [ ] 消息去重
- [ ] 消息状态更新
- [ ] 消息清理

##### 文件: `tests/Catga.Tests/Persistence/InMemory/MemoryOutboxStoreTests.cs` (新建)
测试 `MemoryOutboxStore` (0% → 95%)
- [ ] Outbox消息存储
- [ ] 消息发送
- [ ] 消息确认
- [ ] 消息重试

##### 文件: `tests/Catga.Tests/Persistence/InMemory/ExpirationHelperTests.cs` (新建)
测试 `ExpirationHelper` (0% → 90%)
- [ ] 过期检查
- [ ] TTL管理
- [ ] 自动清理
- [ ] 性能验证

---

## 🎯 Phase 3: 序列化和传输 - 优先级 P1 (第6-7天)

### 目标: 序列化从 44-50% 提升到 90%+, InMemory Transport 从 81.8% 到 95%

#### 3.1 Serialization

##### 文件: `tests/Catga.Tests/Serialization/MessageSerializerBaseTests.cs` (新建)
测试 `MessageSerializerBase` (18.5% → 85%)
- [ ] 基类序列化逻辑
- [ ] 类型处理
- [ ] 错误处理
- [ ] 性能验证

##### 文件: `tests/Catga.Tests/Serialization/JsonSerializerExtensionsTests.cs` (新建)
测试 `JsonSerializerExtensions` (0% → 90%)
- [ ] 扩展方法
- [ ] 自定义序列化选项
- [ ] AOT兼容性
- [ ] 大对象序列化

##### 文件: `tests/Catga.Tests/Serialization/MemoryPackExtensionsTests.cs` (新建)
测试 `MemoryPackSerializerExtensions` (0% → 90%)
- [ ] 二进制序列化
- [ ] 扩展方法
- [ ] 性能优化
- [ ] 内存管理

#### 3.2 Transport

##### 文件: `tests/Catga.Tests/Transport/InMemory/InMemoryTransportExtendedTests.cs` (新建)
补充 `InMemoryMessageTransport` (81.7% → 95%)
- [ ] 边界情况
- [ ] 并发订阅
- [ ] 取消订阅
- [ ] 消息传递失败

##### 文件: `tests/Catga.Tests/Transport/InMemory/InMemoryTransportExtensionsTests.cs` (新建)
测试 `InMemoryTransportServiceCollectionExtensions` (0% → 100%)
- [ ] 服务注册
- [ ] 配置选项
- [ ] DI集成

---

## 🎯 Phase 4: 外部依赖 - 优先级 P1 (第8-10天)

### 目标: Redis 和 NATS 从 0-6% 提升到 75%+

#### 4.1 Redis Persistence (使用 Mock 或 Testcontainers)

##### 文件: `tests/Catga.Tests/Persistence/Redis/RedisIdempotencyStoreTests.cs` (新建)
- [ ] 幂等性检查
- [ ] Redis存储
- [ ] 并发处理
- [ ] 连接失败处理

##### 文件: `tests/Catga.Tests/Persistence/Redis/RedisEventStoreTests.cs` (新建)
- [ ] 事件存储
- [ ] 事件查询
- [ ] Stream操作
- [ ] 错误处理

##### 文件: `tests/Catga.Tests/Persistence/Redis/RedisDeadLetterQueueTests.cs` (新建)
- [ ] DLQ存储
- [ ] 消息检索
- [ ] 消息重试
- [ ] 过期处理

##### 文件: `tests/Catga.Tests/Persistence/Redis/RedisInboxOutboxTests.cs` (新建)
- [ ] Inbox/Outbox模式
- [ ] 事务保证
- [ ] 消息发送
- [ ] 错误恢复

#### 4.2 NATS Persistence (使用 Mock 或 Testcontainers)

##### 文件: `tests/Catga.Tests/Persistence/Nats/NatsJSEventStoreTests.cs` (新建)
- [ ] JetStream事件存储
- [ ] 事件追加
- [ ] 事件查询
- [ ] Stream管理

##### 文件: `tests/Catga.Tests/Persistence/Nats/NatsJSIdempotencyStoreTests.cs` (新建)
- [ ] JetStream幂等性
- [ ] KV Store操作
- [ ] TTL管理
- [ ] 错误处理

##### 文件: `tests/Catga.Tests/Persistence/Nats/NatsJSInboxOutboxTests.cs` (新建)
- [ ] JetStream Inbox/Outbox
- [ ] 消息去重
- [ ] 消息发送
- [ ] 重试机制

#### 4.3 Redis Transport (使用 Mock)

##### 文件: `tests/Catga.Tests/Transport/Redis/RedisMessageTransportTests.cs` (新建)
- [ ] 消息发送
- [ ] 消息接收
- [ ] PubSub模式
- [ ] 连接管理

#### 4.4 NATS Transport (使用 Mock)

##### 文件: `tests/Catga.Tests/Transport/Nats/NatsMessageTransportTests.cs` (新建)
- [ ] 消息传输
- [ ] 订阅管理
- [ ] 错误恢复
- [ ] 重连机制

---

## 🎯 Phase 5: 异常和错误处理 (第11天)

### 目标: 异常类从 0% 提升到 100%

##### 文件: `tests/Catga.Tests/Exceptions/CatgaExceptionsTests.cs` (新建)
测试所有异常类型 (0% → 100%)
- [ ] `CatgaConfigurationException`
- [ ] `CatgaTimeoutException`
- [ ] `CatgaValidationException`
- [ ] `HandlerNotFoundException`
- [ ] `ConcurrencyException`
- [ ] 异常序列化
- [ ] 异常消息格式

---

## 📊 预期覆盖率提升轨迹

| 阶段 | 完成日期 | 预期总体覆盖率 | 核心包覆盖率 | 关键指标 |
|------|---------|--------------|------------|---------|
| **开始** | Day 0 | 26% | 38.8% | 952/3648 行 |
| **Phase 1** | Day 3 | 45-50% | 70% | ~1800/3648 行 |
| **Phase 2** | Day 5 | 60-65% | 75% | ~2300/3648 行 |
| **Phase 3** | Day 7 | 75-80% | 80% | ~2800/3648 行 |
| **Phase 4** | Day 10 | 85-88% | 85% | ~3200/3648 行 |
| **Phase 5** | Day 11 | **90%+** | **90%+** | **~3300/3648 行** |

---

## 🛠️ 测试策略和最佳实践

### 1. 优先级排序
- **P0 (最高)**: 核心组件 (Catga, InMemory)
- **P1 (高)**: 外部依赖 (Redis, NATS)
- **P2 (中)**: 序列化和扩展
- **P3 (低)**: 已有高覆盖率的组件补充

### 2. 测试质量标准
- 每个测试必须测试一个明确的行为
- 使用 AAA 模式 (Arrange-Act-Assert)
- 测试名称清晰描述测试场景
- 使用 FluentAssertions 提高可读性
- 避免虚假覆盖（无意义的测试）

### 3. Mock 策略
- 优先使用真实实现 (InMemory)
- 外部依赖使用 NSubstitute 或 Moq
- 必要时使用 Testcontainers (Redis, NATS)
- 避免过度 Mock

### 4. 并发测试
- 所有并发相关组件需要并发测试
- 使用 `Task.WhenAll` 模拟高并发
- 测试竞态条件
- 验证线程安全

### 5. 边界情况
- Null 参数
- 空集合
- 极大/极小值
- 并发边界
- 资源耗尽

---

## ✅ 完成标准

- [ ] 总体覆盖率 ≥ 90%
- [ ] 核心包覆盖率 ≥ 90%
- [ ] 所有P0组件覆盖率 ≥ 85%
- [ ] 所有测试通过
- [ ] 无虚假覆盖
- [ ] 测试执行时间 < 120秒
- [ ] 代码评审通过
- [ ] 文档更新完成

---

## 📝 实施清单

### 立即开始 (Day 1-3)
- [x] 运行覆盖率分析
- [x] 识别未覆盖组件
- [x] 制定详细计划
- [ ] 创建测试文件结构
- [ ] 实现 Phase 1 测试

### 本周完成 (Day 4-7)
- [ ] 完成 Phase 2 持久化测试
- [ ] 完成 Phase 3 序列化测试
- [ ] 达到 75-80% 覆盖率

### 下周完成 (Day 8-11)
- [ ] 完成 Phase 4 外部依赖测试
- [ ] 完成 Phase 5 异常测试
- [ ] 达到 90%+ 覆盖率
- [ ] 生成最终报告

---

**文档更新**: 2025-10-27  
**负责人**: AI Assistant  
**当前状态**: Phase 1 准备中  
**目标日期**: 2025-11-07 (11个工作日)

