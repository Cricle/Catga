# Catga 测试覆盖率分析和改进计划

**分析日期**: 2024-10-16  
**测试数量**: 191  
**测试通过率**: 100% (191/191)  
**代码覆盖率**: 16.93%

---

## 📊 当前覆盖率统计

```
总体覆盖率: 16.93%
├─ 已覆盖行数: 869
├─ 总有效行数: 5,132
├─ 分支覆盖: 19.12%
└─ 已覆盖分支: 254/1,328
```

### 覆盖率分析

**16.93% 覆盖率的原因**:

1. **大量基础设施代码未测试** (约 40%)
   - Transport 层 (NATS)
   - Persistence 层 (Redis)
   - Debugger 组件
   - ASP.NET Core 集成

2. **InMemory 实现部分测试** (约 30%)
   - 基本功能已测试
   - 边界case和错误场景未覆盖

3. **Source Generator 代码** (约 20%)
   - 生成的代码难以直接测试
   - 通过集成测试间接覆盖

4. **Pipeline Behaviors** (约 10%)
   - 基本流程已测试
   - 复杂场景（如超时、重试）未完全覆盖

---

## ✅ 已有测试覆盖

### 核心功能 (良好覆盖)

1. **CatgaMediator** ✅
   - Send/SendAsync
   - Publish/PublishAsync
   - 批量操作
   - 错误处理

2. **消息类型** ✅
   - IRequest/INotification
   - CatgaResult
   - 错误传播

3. **基本 Handlers** ✅
   - IRequestHandler
   - IEventHandler
   - SafeRequestHandler

4. **分布式 ID** ✅
   - Snowflake 算法
   - 并发性能
   - ID 唯一性

5. **序列化** ✅
   - MemoryPack
   - JSON
   - 性能对比

---

## 🎯 需要补充的测试

### 高优先级 (影响核心功能)

#### 1. Pipeline Behaviors (当前覆盖: ~40%)

**需要补充的测试**:
```csharp
// InboxBehavior
- [ ] 幂等性验证
- [ ] 重复消息过滤
- [ ] 锁超时处理
- [ ] 存储失败恢复

// OutboxBehavior
- [ ] 事件持久化
- [ ] 传输失败重试
- [ ] 部分失败处理

// ValidationBehavior
- [ ] 自定义验证器
- [ ] 多验证器链
- [ ] 验证失败错误聚合

// TimeoutBehavior
- [ ] 超时配置
- [ ] 取消令牌传播
- [ ] 超时后的资源清理
```

#### 2. InMemory Transport (当前覆盖: ~50%)

**需要补充的测试**:
```csharp
- [ ] 并发订阅/取消订阅
- [ ] Handler 异常处理
- [ ] 消息路由优先级
- [ ] 内存泄漏测试（长时间运行）
```

#### 3. Graceful Lifecycle (当前覆盖: ~30%)

**需要补充的测试**:
```csharp
- [ ] 组件注册/注销
- [ ] 优雅关闭期间新请求处理
- [ ] 组件故障自动恢复
- [ ] 嵌套操作跟踪
```

### 中优先级 (增强稳定性)

#### 4. Error Handling (当前覆盖: ~60%)

**需要补充的测试**:
```csharp
- [ ] CatgaException 详细信息
- [ ] 错误传播链
- [ ] 错误日志验证
- [ ] OpenTelemetry 错误追踪
```

#### 5. Serialization Edge Cases (当前覆盖: ~70%)

**需要补充的测试**:
```csharp
- [ ] 大对象序列化 (>1MB)
- [ ] 循环引用检测
- [ ] 空/null 值处理
- [ ] 特殊字符编码
```

### 低优先级 (Nice to Have)

#### 6. NATS Transport (当前覆盖: 0%)

**原因**: 需要外部依赖，适合集成测试

**建议**:
- 使用 Testcontainers
- 或创建 Mock INatsConnection

#### 7. Redis Persistence (当前覆盖: 0%)

**原因**: 同上

**建议**:
- 使用 Testcontainers
- 或创建 Mock IDatabase

#### 8. Debugger (当前覆盖: 0%)

**原因**: UI 组件和 SignalR，需要端到端测试

**建议**:
- 单元测试核心逻辑（AdaptiveSampler, EventStore）
- 集成测试 Pipeline (ReplayableEventCapturer)

---

## 📈 覆盖率提升计划

### Phase 1: 关键路径覆盖 (目标: 40%)

**工作量**: 50-60 个新测试

1. Pipeline Behaviors 完整测试套件
2. InMemory Transport 边界测试
3. Graceful Lifecycle 场景测试
4. 错误处理增强测试

**预期覆盖率**: 16.93% → 40%

### Phase 2: 边界和错误场景 (目标: 60%)

**工作量**: 40-50 个新测试

1. 序列化边界测试
2. 并发压力测试
3. 资源泄漏测试
4. 性能退化测试

**预期覆盖率**: 40% → 60%

### Phase 3: 外部依赖集成 (目标: 75%)

**工作量**: 30-40 个新测试 (需要 Testcontainers)

1. NATS Transport 集成测试
2. Redis Persistence 集成测试
3. 端到端场景测试

**预期覆盖率**: 60% → 75%

---

## 🚀 快速改进建议

### 立即可添加的测试 (无需新依赖)

1. **InboxBehavior 幂等性测试**
```csharp
[Fact]
public async Task InboxBehavior_DuplicateMessage_ShouldReturnCachedResult()
{
    // Arrange: 两次发送同一消息
    // Act: 第二次应该从缓存返回
    // Assert: 不应重复执行 Handler
}
```

2. **OutboxBehavior 事件持久化测试**
```csharp
[Fact]
public async Task OutboxBehavior_EventPublish_ShouldPersistToStore()
{
    // Arrange: Mock IOutboxStore
    // Act: 发布事件
    // Assert: 事件已保存到 Outbox
}
```

3. **GracefulLifecycle 操作跟踪测试**
```csharp
[Fact]
public async Task GracefulLifecycle_BeginOperation_ShouldTrackPending()
{
    // Arrange: LifecycleCoordinator
    // Act: BeginOperation
    // Assert: 待处理操作计数 +1
}
```

4. **Timeout Behavior 测试**
```csharp
[Fact]
public async Task TimeoutBehavior_LongRunning_ShouldCancel()
{
    // Arrange: 10ms 超时
    // Act: 100ms Handler
    // Assert: TaskCanceledException
}
```

5. **CatgaMediator 并发测试**
```csharp
[Fact]
public async Task Mediator_ConcurrentRequests_ShouldIsolateScopes()
{
    // Arrange: 1000 并发请求
    // Act: 并发执行
    // Assert: 无状态冲突
}
```

---

## 📝 测试最佳实践

### 1. 测试结构

```csharp
// ✅ 好的测试结构
[Fact]
public async Task Method_Scenario_ExpectedBehavior()
{
    // Arrange - 准备测试数据
    var services = new ServiceCollection();
    services.AddCatga().UseMemoryPack();
    var mediator = services.BuildServiceProvider().GetRequiredService<ICatgaMediator>();
    
    // Act - 执行操作
    var result = await mediator.SendAsync(command);
    
    // Assert - 验证结果
    result.IsSuccess.Should().BeTrue();
    result.Value.Should().NotBeNull();
}
```

### 2. Mock 使用

```csharp
// ✅ 使用 Moq 或 NSubstitute
var mockStore = new Mock<IInboxStore>();
mockStore.Setup(x => x.IsProcessedAsync(It.IsAny<string>(), default))
         .ReturnsAsync(false);

services.AddSingleton(mockStore.Object);
```

### 3. 数据驱动测试

```csharp
// ✅ 使用 [Theory] 测试多种情况
[Theory]
[InlineData(1)]
[InlineData(10)]
[InlineData(100)]
[InlineData(1000)]
public async Task Mediator_SendBatch_ShouldHandleVariousSizes(int count)
{
    // ...
}
```

---

## 🎯 覆盖率目标

| 模块 | 当前 | 目标 (Phase 1) | 目标 (Phase 2) | 目标 (Phase 3) |
|------|------|----------------|----------------|----------------|
| **Catga (核心)** | 30% | 60% | 80% | 85% |
| **Catga.InMemory** | 40% | 70% | 85% | 90% |
| **Catga.Serialization** | 60% | 80% | 90% | 95% |
| **Catga.Transport.Nats** | 0% | 20% | 40% | 60% |
| **Catga.Persistence.Redis** | 0% | 20% | 40% | 60% |
| **Catga.Debugger** | 0% | 30% | 50% | 65% |
| **Catga.SourceGenerator** | 10% | 20% | 30% | 40% |
| **总体** | 16.93% | 40% | 60% | 75% |

---

## ✅ 结论

### 当前状态评估

**优点**:
- ✅ 核心 CQRS 功能测试充分
- ✅ 所有测试通过 (191/191)
- ✅ 关键路径有基本覆盖
- ✅ 性能基准测试完善

**需要改进**:
- ⚠️ Pipeline Behaviors 测试不足
- ⚠️ 外部依赖模块无测试
- ⚠️ 边界和错误场景覆盖不足
- ⚠️ 集成测试缺失

### 推荐行动

**短期 (1-2 周)**:
1. 补充 Pipeline Behaviors 测试 (优先级最高)
2. 增加 Graceful Lifecycle 测试
3. 完善错误处理测试
4. 目标: 覆盖率提升到 40%

**中期 (1 个月)**:
1. 边界和压力测试
2. 性能退化检测
3. 内存泄漏测试
4. 目标: 覆盖率提升到 60%

**长期 (3 个月)**:
1. 集成测试 (Testcontainers)
2. 端到端场景测试
3. Chaos Engineering 测试
4. 目标: 覆盖率达到 75%+

---

## 📚 相关文档

- [测试项目](./tests/Catga.Tests/) - 当前测试代码
- [性能基准](./benchmarks/) - 性能测试
- [贡献指南](./CONTRIBUTING.md) - 如何添加测试

---

<div align="center">

**🎯 目标: 从 16.93% 提升到 75%+ 覆盖率**

**保持代码质量，确保框架稳定性**

</div>

