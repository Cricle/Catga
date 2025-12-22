# E2E 测试修复总结

## 问题描述

E2E 测试在批量运行时失败，但单独运行时通过：
- `CompleteOrderFlow_Redis_ShouldWorkEndToEnd` - 失败（批量），通过（单独）
- `CompleteOrderFlow_NATS_ShouldWorkEndToEnd` - 失败（批量），通过（单独）
- `CompleteOrderFlow_InMemory_ShouldWorkEndToEnd` - 始终通过

**症状**：Redis 和 NATS 测试只读取到 2/3 个事件（OrderPaid 和 OrderShipped），缺少第一个 OrderCreated 事件。

## 根本原因分析

### 1. CatgaMediator 的静态缓存机制

`CatgaMediator` 使用静态字典缓存 Handler 实例以提高性能：

```csharp
private static readonly ConcurrentDictionary<Type, object?> _handlerCache = new();
private static readonly ConcurrentDictionary<Type, object?> _behaviorCache = new();
```

### 2. 测试间的状态污染

当测试顺序执行时：
1. **InMemory 测试先运行**：Handler 被缓存，使用 InMemory 的 EventStore
2. **Redis/NATS 测试后运行**：从静态缓存获取 Handler，但 Handler 内部仍然持有 InMemory 的 EventStore 引用
3. **结果**：事件被写入 InMemory EventStore，而不是 Redis/NATS EventStore
4. **读取时**：从 Redis/NATS 读取，发现缺少第一个事件

### 3. 验证过程

- 单独运行 Redis 测试：✅ 通过（缓存为空，正确获取 Redis EventStore）
- 单独运行 NATS 测试：✅ 通过（缓存为空，正确获取 NATS EventStore）
- 批量运行所有测试：❌ 失败（缓存污染）

## 解决方案

### 方案 1：清理静态缓存（已采用）

在每个 E2E 测试开始时，使用反射清理 `CatgaMediator` 的静态缓存：

```csharp
private static void ClearMediatorCaches()
{
    var mediatorType = typeof(CatgaMediator);
    var handlerCacheField = mediatorType.GetField("_handlerCache", 
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    var behaviorCacheField = mediatorType.GetField("_behaviorCache", 
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    if (handlerCacheField?.GetValue(null) is System.Collections.IDictionary handlerCache)
        handlerCache.Clear();
    
    if (behaviorCacheField?.GetValue(null) is System.Collections.IDictionary behaviorCache)
        behaviorCache.Clear();
}
```

### 方案 2：修改 Handler 注册（辅助）

将 Handler 从 Singleton 改为 Transient，避免 DI 容器级别的缓存：

```csharp
// Before
services.AddSingleton<IRequestHandler<E2ECreateOrderCommand, E2EOrderCreatedResult>, E2ECreateOrderCommandHandler>();

// After
services.AddTransient<IRequestHandler<E2ECreateOrderCommand, E2EOrderCreatedResult>, E2ECreateOrderCommandHandler>();
```

## 测试结果

所有 9 个 E2E 测试现在都能稳定通过：

```
测试运行成功。
测试总数: 9
     通过数: 9
总时间: 41.3 秒
```

### 测试覆盖

1. **Complete Order Flow**
   - InMemory ✅
   - Redis ✅
   - NATS ✅

2. **Snapshot Store**
   - InMemory ✅
   - Redis ✅
   - NATS ✅

3. **Event Publishing**
   - InMemory ✅
   - Redis ✅
   - NATS ✅

## 提交记录

- Commit: `c039f3e`
- Message: "fix(e2e): resolve test interference by clearing CatgaMediator static caches"

## 后续建议

### 短期（测试层面）
- ✅ 已实现：在测试中清理静态缓存
- 考虑为每个后端创建独立的测试类，避免共享缓存

### 长期（框架层面）
- 考虑重构 `CatgaMediator` 的缓存机制：
  - 选项 1：不缓存 Handler 实例，每次从 ServiceProvider 获取
  - 选项 2：使用 `IServiceScopeFactory` 创建 Scope，从 Scope 获取 Handler
  - 选项 3：缓存 Handler 工厂而不是实例
- 评估缓存对性能的实际影响
- 添加配置选项允许禁用缓存（用于测试场景）

## 经验教训

1. **静态缓存的风险**：静态缓存在测试环境中容易导致状态污染
2. **测试隔离的重要性**：即使使用独立的 ServiceProvider，静态状态仍然会共享
3. **单独测试 vs 批量测试**：两种场景都要验证，才能发现隐藏的问题
4. **反射的合理使用**：在测试中使用反射清理内部状态是可接受的临时方案

## 相关文件

- `tests/Catga.Tests/E2E/CompleteBackendE2ETests.cs` - E2E 测试文件
- `src/Catga/CatgaMediator.cs` - Mediator 实现（包含静态缓存）
- `.kiro/specs/tdd-validation/E2E-TESTS-STATUS.md` - 测试状态跟踪
