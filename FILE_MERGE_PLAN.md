# 📁 文件合并优化计划

> **目标**: 合理合并代码和文件，减少文件数量，提升代码组织性  
> **原则**: 相关性强的代码应该在一起，减少导航成本

---

## 🔍 当前分析

### 统计数据
```
总文件数: ~120个 .cs 文件
小文件 (<100行): ~60个
接口文件: ~29个
扩展类文件: ~15个
```

---

## 🎯 可合并的文件类别

### 1️⃣ **消息接口** (5个文件 → 1个文件)

**当前状态**:
```
Messages/
  ├── IMessage.cs (33行)
  ├── ICommand.cs (16行)
  ├── IQuery.cs (9行)
  ├── IEvent.cs (21行)
  └── IRequest.cs (16行)
```

**问题**: 
- 5个小接口分散在5个文件
- 它们都是消息类型定义，关联性极强
- 总计只有 95 行代码

**合并方案**:
```csharp
// Messages/MessageContracts.cs (统一消息契约)
namespace Catga.Messages;

/// <summary>
/// Base message interface with common properties
/// </summary>
public interface IMessage
{
    string MessageId { get; set; }
    string? CorrelationId { get; set; }
    DateTime CreatedAt { get; set; }
}

/// <summary>
/// Command message - represents an action/operation
/// </summary>
public interface ICommand : IMessage { }

/// <summary>
/// Query message - represents a data request
/// </summary>
public interface IQuery<out TResponse> : IMessage { }

/// <summary>
/// Event message - represents something that happened
/// </summary>
public interface IEvent : IMessage
{
    DateTime OccurredAt { get; set; }
}

/// <summary>
/// Request marker interface
/// </summary>
public interface IRequest<out TResponse> { }
```

**收益**:
- 文件数: 5 → 1 (-4)
- 导航: 更容易，所有消息类型在一处
- 理解: 更清晰，看到全貌

**优先级**: P1 (高)  
**工作量**: 10分钟

---

### 2️⃣ **Handler接口** (2个文件 → 1个文件)

**当前状态**:
```
Handlers/
  ├── IRequestHandler.cs (21行)
  └── IEventHandler.cs (12行)
```

**合并方案**:
```csharp
// Handlers/HandlerContracts.cs
namespace Catga.Handlers;

/// <summary>
/// Request handler interface
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Event handler interface
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
```

**收益**:
- 文件数: 2 → 1 (-1)
- 所有Handler接口在一起

**优先级**: P1 (高)  
**工作量**: 5分钟

---

### 3️⃣ **ServiceCollectionExtensions** (多个 → 少数)

**当前问题**:
```
分散的扩展方法文件:
├── DistributedIdServiceCollectionExtensions.cs (55行)
├── EventSourcingServiceCollectionExtensions.cs (20行)
├── HealthCheckServiceCollectionExtensions.cs (41行)
├── MemoryDistributedLockServiceCollectionExtensions.cs (20行)
├── SagaServiceCollectionExtensions.cs (20行)
├── TransportServiceCollectionExtensions.cs (33行)
├── DistributedCacheServiceCollectionExtensions.cs (23行)
└── 还有更多...
```

**合并方案**: 按功能域合并

```csharp
// DependencyInjection/DistributedExtensions.cs
// 合并: DistributedId + DistributedLock + DistributedCache

// DependencyInjection/PersistenceExtensions.cs  
// 合并: EventSourcing + Saga + Outbox + Inbox

// DependencyInjection/ObservabilityExtensions.cs
// 合并: HealthCheck + Metrics + Tracing
```

**收益**:
- 文件数: ~10 → 3 (-7)
- 更好的组织：按功能域分组

**优先级**: P2 (中)  
**工作量**: 30分钟

---

### 4️⃣ **配置类** (多个 → 1个)

**当前状态**:
```
Configuration/
  ├── CatgaOptions.cs (95行)
  ├── ThreadPoolOptions.cs (28行)
  ├── CatgaOptionsValidator.cs (?)
  └── SmartDefaults.cs (?)
  
DistributedId/
  └── DistributedIdOptions.cs (95行)
```

**合并方案**:
```csharp
// Configuration/CatgaConfiguration.cs (统一配置文件)
namespace Catga.Configuration;

/// <summary>
/// Unified Catga configuration
/// </summary>
public sealed class CatgaOptions
{
    // 核心配置
    public int MaxConcurrency { get; set; } = 100;
    public bool EnableMetrics { get; set; } = true;
    
    // ThreadPool配置 (内嵌)
    public ThreadPoolConfiguration ThreadPool { get; set; } = new();
    
    // DistributedId配置 (内嵌)
    public DistributedIdConfiguration DistributedId { get; set; } = new();
    
    // 其他配置...
}

public sealed class ThreadPoolConfiguration
{
    public int MinThreads { get; set; } = 10;
    public int MaxThreads { get; set; } = 100;
}

public sealed class DistributedIdConfiguration
{
    public int WorkerId { get; set; }
    public int DatacenterId { get; set; }
    public DateTime? CustomEpoch { get; set; }
    public SnowflakeBitLayout? BitLayout { get; set; }
}
```

**收益**:
- 文件数: 3-4 → 1 (-2~3)
- 配置更集中
- 智能提示更好

**优先级**: P3 (低 - 破坏性变更)  
**工作量**: 1小时

---

### 5️⃣ **小的接口+实现** (配对合并)

#### 示例1: IDeadLetterQueue
**当前**:
```
DeadLetter/
  ├── IDeadLetterQueue.cs (45行)
  └── InMemoryDeadLetterQueue.cs (75行)
```

**问题**: 
- 只有一个实现
- 接口和实现分离增加导航成本

**建议**: 合并到一个文件
```csharp
// DeadLetter/DeadLetterQueue.cs
namespace Catga.DeadLetter;

public interface IDeadLetterQueue { ... }

public class InMemoryDeadLetterQueue : IDeadLetterQueue { ... }
```

#### 示例2: IDistributedLock
**当前**:
```
DistributedLock/
  ├── IDistributedLock.cs (46行)
  └── MemoryDistributedLock.cs (92行)
```

**合并**: 同上

#### 适用的其他配对:
- `IHealthCheck` + `CatgaHealthCheck`
- `ISaga` + `SagaExecutor` + `SagaBuilder` (考虑)

**收益**:
- 文件数: ~6 → 3 (-3)
- 更容易理解接口和实现的关系

**优先级**: P2 (中)  
**工作量**: 20分钟

---

### 6️⃣ **Pipeline相关** 

**当前**:
```
Pipeline/
  ├── IPipelineBehavior.cs (39行)
  ├── PipelineExecutor.cs (79行)
  └── Behaviors/ (9个文件)
```

**建议**: 合并 IPipelineBehavior 和 PipelineExecutor
```csharp
// Pipeline/Pipeline.cs
namespace Catga.Pipeline;

public interface IPipelineBehavior<TRequest, TResponse> { ... }

public interface IPipelineBehavior<TRequest> { ... }

public delegate ValueTask<CatgaResult<TResponse>> PipelineDelegate<TResponse>();

public static class PipelineExecutor { ... }
```

**收益**:
- 文件数: 2 → 1 (-1)
- Pipeline核心在一个文件

**优先级**: P2 (中)  
**工作量**: 10分钟

---

## 📊 预期总收益

### 文件数量减少

| 优化项 | 当前 | 优化后 | 减少 |
|--------|------|--------|------|
| **消息接口** | 5 | 1 | **-4** |
| **Handler接口** | 2 | 1 | **-1** |
| **Extensions** | ~10 | 3 | **-7** |
| **小接口+实现** | 6 | 3 | **-3** |
| **Pipeline** | 2 | 1 | **-1** |
| **总计** | 25 | 9 | **-16 (64%)** |

### 代码组织改进

```
优化前:
- 文件分散，需要频繁切换
- 接口和实现分离
- 难以快速理解全貌

优化后:
- 相关代码集中
- 接口和实现在一起
- 更容易理解和导航
```

---

## 🚀 实施计划

### 第1步: P1 优化 (30分钟)
- [ ] 合并消息接口 (5→1)
- [ ] 合并Handler接口 (2→1)
- [ ] 运行测试

### 第2步: P2 优化 (1小时)
- [ ] 合并ServiceCollectionExtensions (10→3)
- [ ] 合并小接口+实现 (6→3)
- [ ] 合并Pipeline核心 (2→1)
- [ ] 运行测试

### 第3步: P3 优化 (可选)
- [ ] 合并配置类 (需评估破坏性)

---

## ✅ 验收标准

### 功能验收
- [ ] 所有90个测试通过
- [ ] 编译无错误
- [ ] 向后兼容 (P1, P2)

### 代码质量
- [ ] 文件数量减少 >50%
- [ ] 代码行数不增加
- [ ] 导航体验提升

---

## ⚠️ 注意事项

### 何时合并
✅ 文件总行数 < 300 行  
✅ 职责相关性强  
✅ 经常一起查看/修改  
✅ 只有一个实现的接口  

### 何时不合并
❌ 文件会变得过大 (>500行)  
❌ 职责不相关  
❌ 可能被独立扩展  
❌ 团队约定分离  

---

## 📖 最佳实践

### 文件组织原则
1. **按功能聚合**: 相关的放一起
2. **适度大小**: 100-400行最佳
3. **清晰命名**: 文件名反映内容
4. **逻辑分组**: 用 region 或注释分隔

### 示例结构
```csharp
// MessageContracts.cs

namespace Catga.Messages;

#region Base Interfaces

public interface IMessage { ... }

#endregion

#region Command & Query

public interface ICommand : IMessage { ... }

public interface IQuery<out TResponse> : IMessage { ... }

#endregion

#region Event

public interface IEvent : IMessage { ... }

#endregion
```

---

**创建日期**: 2025-10-09  
**预计完成**: 2025-10-09 (P1+P2)  
**预期减少**: 16个文件 (64%)

