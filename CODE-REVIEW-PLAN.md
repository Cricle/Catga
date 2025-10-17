# Catga 代码全面审查计划

## 🎯 审查范围

### 1. 核心库 (src/Catga)
- [ ] 性能关键路径
- [ ] AOT 兼容性
- [ ] 内存分配
- [ ] 线程安全

### 2. 消息处理 (src/Catga.InMemory)
- [ ] CatgaMediator 性能
- [ ] 事件处理逻辑
- [ ] 并发控制

### 3. 可观测性 (新增功能)
- [ ] CatgaActivitySource
- [ ] DistributedTracingBehavior
- [ ] CatgaMetrics
- [ ] EventStore

### 4. Debugger (src/Catga.Debugger)
- [ ] ReplayableEventCapturer
- [ ] InMemoryEventStore
- [ ] 生产模式安全性

### 5. ASP.NET Core 集成
- [ ] CorrelationIdMiddleware
- [ ] Endpoints
- [ ] SignalR Hub

## 🔍 审查重点

### 性能
1. 避免不必要的分配
2. 使用 ArrayPool
3. ValueTask 而非 Task
4. Span<T> / Memory<T>
5. 避免装箱

### AOT 兼容性
1. 无反射（或条件编译）
2. 泛型约束正确
3. DynamicallyAccessedMembers 标记
4. Source Generator 支持

### 线程安全
1. 正确使用 Interlocked
2. 无竞态条件
3. AsyncLocal 正确使用

### 内存安全
1. 正确的 Dispose 模式
2. 无内存泄漏
3. 正确的 ArrayPool 返还

## 📋 审查检查表

### 每个文件检查
- [ ] 编译警告已修复
- [ ] 性能优化合理
- [ ] AOT 兼容
- [ ] 线程安全
- [ ] 内存安全
- [ ] 错误处理完整
- [ ] 注释清晰准确

### 集成检查
- [ ] API 一致性
- [ ] 配置合理性
- [ ] 默认值安全
- [ ] 向后兼容

## 🚀 执行计划

1. **阶段 1：编译检查**
   - 修复所有 warnings
   - 验证 AOT 发布

2. **阶段 2：核心审查**
   - CatgaMediator
   - Pipeline behaviors
   - Event handling

3. **阶段 3：可观测性审查**
   - Tracing behavior
   - Metrics
   - Debugger

4. **阶段 4：集成测试**
   - 启动示例
   - 测试所有功能
   - 性能验证

5. **阶段 5：文档审查**
   - API 文档准确性
   - 示例代码正确性
   - 配置指南完整性

