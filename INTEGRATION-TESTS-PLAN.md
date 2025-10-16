# Catga 集成测试计划

## 目标
创建全面的集成测试，验证 Catga 框架的端到端功能和真实场景。

## 测试范围

### 1. 端到端场景测试 ✅
**文件**: `tests/Catga.Tests/Integration/EndToEndScenarioTests.cs`

- **CQRS 完整流程**
  - 命令处理 → 事件发布 → 事件订阅
  - 查询处理
  - 结果验证

- **事件驱动架构**
  - 单个事件，多个订阅者
  - 事件顺序保证
  - 事件扇出（Fan-out）

- **SafeRequestHandler 集成**
  - 成功场景
  - 自动错误处理
  - 自定义回滚逻辑

### 2. 序列化集成测试 ✅
**文件**: `tests/Catga.Tests/Integration/SerializationIntegrationTests.cs`

- **MemoryPack 序列化**
  - 复杂对象序列化/反序列化
  - 嵌套对象
  - 集合类型
  - AOT 兼容性验证

- **JSON 序列化**
  - 相同场景的 JSON 版本
  - 跨序列化器兼容性

### 3. 传输层集成测试 ✅
**文件**: `tests/Catga.Tests/Integration/TransportIntegrationTests.cs`

- **InMemory 传输**
  - 请求/响应模式
  - 发布/订阅模式
  - QoS 验证

- **NATS 传输**（如果可用）
  - 连接管理
  - 消息传输
  - 重连机制

### 4. Pipeline 集成测试 ✅
**文件**: `tests/Catga.Tests/Integration/PipelineIntegrationTests.cs`

- **Behavior 链**
  - Logging → Idempotency → Retry → Handler
  - Behavior 执行顺序
  - 短路逻辑

- **幂等性**
  - 重复请求检测
  - 结果缓存
  - 并发处理

- **重试机制**
  - 瞬态错误重试
  - 指数退避
  - 最大重试次数

### 5. 调试器集成测试 ✅
**文件**: `tests/Catga.Tests/Integration/DebuggerIntegrationTests.cs`

- **消息流追踪**
  - 流程记录
  - 调用链追踪
  - 元数据捕获

- **时间旅行回放**
  - 事件存储
  - 状态快照
  - 回放验证

### 6. 并发和压力测试 ✅
**文件**: `tests/Catga.Tests/Integration/ConcurrencyIntegrationTests.cs`

- **高并发场景**
  - 100+ 并发请求
  - 无锁并发验证
  - 线程安全验证

- **分布式锁**
  - 竞态条件
  - 死锁预防
  - 超时处理

### 7. 生命周期集成测试 ✅
**文件**: `tests/Catga.Tests/Integration/LifecycleIntegrationTests.cs`

- **优雅停机**
  - 活跃操作完成
  - 超时处理
  - 资源清理

- **自动恢复**
  - 连接断开
  - 自动重连
  - 状态恢复

### 8. OrderSystem 真实场景测试 ✅
**文件**: `tests/Catga.Tests/Integration/OrderSystemScenarioTests.cs`

- **订单创建流程**
  - 成功场景：库存检查 → 保存 → 预留 → 事件发布
  - 失败场景：自动回滚 → 库存释放 → 订单删除 → 失败事件

- **事件处理**
  - 订单创建事件 → 多个订阅者
  - 订单支付事件 → 发货触发
  - 订单取消事件 → 库存释放

## 测试工具

### 辅助类
- `IntegrationTestFixture` - 测试基类，提供 DI 容器、传输层等
- `TestMessageHelper` - 测试消息生成
- `ConcurrencyHelper` - 并发测试辅助
- `AssertionExtensions` - 自定义断言扩展

### Mock 和 Stub
- 使用真实组件优先
- 仅在外部依赖时使用 Mock（如 NATS 服务器）

## 覆盖率目标

- **单元测试**: 70%+ (已达成)
- **集成测试**: 50%+ (新增)
- **总体覆盖率**: 80%+

## 执行策略

1. **快速测试** (<100ms)
   - InMemory 传输
   - 简单场景

2. **标准测试** (100ms-1s)
   - Pipeline 测试
   - 序列化测试

3. **慢速测试** (>1s)
   - 并发测试
   - 压力测试
   - NATS 集成（需要真实服务）

## CI/CD 集成

```yaml
# .github/workflows/test.yml
- name: Run Integration Tests
  run: dotnet test --filter Category=Integration --verbosity normal
  
- name: Run All Tests with Coverage
  run: dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
```

## 执行顺序

1. ✅ 端到端场景测试 (基础)
2. ✅ 序列化集成测试
3. ✅ 传输层集成测试
4. ✅ Pipeline 集成测试
5. ✅ OrderSystem 真实场景测试
6. ✅ 调试器集成测试
7. ✅ 并发和压力测试
8. ✅ 生命周期集成测试

---

**预计时间**: 3-4 小时
**预计新增测试数**: 80-100 个
**预计代码行数**: 2000-2500 行

