# Debugger UI 未连接问题 - 诊断与解决方案

## 🔍 问题描述

**症状**:
- Debugger UI 显示"未连接"
- 没有捕获任何事件数据
- Total Events: 0, Total Flows: 0

**测试结果**:
```
✅ Debugger UI 可访问 (Status 200, 包含 SignalR)
✅ Debugger API 可访问 (/stats, /flows)
✅ SignalR Hub 可访问
✅ 订单创建成功
❌ 但事件数据为 0 - 未被捕获
```

---

## 🔬 根本原因

### 问题定位

**Debugger 事件捕获机制**:
- `ReplayableEventCapturer<TRequest, TResponse>` 实现了 `IPipelineBehavior`
- 它需要在 Mediator 的 Pipeline 中被调用
- **但 `CatgaMediator` (InMemory) 不支持 Pipeline Behaviors**

**当前架构**:
```
Request/Event
  ↓
CatgaMediator.SendAsync() / PublishAsync()
  ↓
Handler.HandleAsync()  ← 直接调用，跳过所有 Pipeline
  ↓
完成
```

**期望架构**:
```
Request/Event
  ↓
CatgaMediator
  ↓
Pipeline Behaviors (包括 ReplayableEventCapturer)  ← 缺失！
  ↓
Handler
  ↓
完成
```

### 代码证据

**1. Debugger 注册了 Capturer**:
```csharp
// src/Catga.Debugger/DependencyInjection/DebuggerServiceCollectionExtensions.cs
public static IServiceCollection AddCatgaDebugger(...)
{
    // ...
    services.AddSingleton(typeof(ReplayableEventCapturer<,>));  // ✅ 已注册
    return services;
}
```

**2. 但 Mediator 不调用它**:
```csharp
// src/Catga.InMemory/CatgaMediator.cs
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(...)
{
    // ...
    var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(scopedProvider);
    result = await handler.HandleAsync(request, cancellationToken);  // ❌ 直接调用
    // 没有 Pipeline Behavior 调用！
}
```

---

## 💡 解决方案

### 方案 A: 为 InMemory Mediator 添加 Pipeline Behavior 支持 ⭐ **推荐**

**优势**:
- ✅ 符合 Catga 架构设计
- ✅ 支持所有 Pipeline Behaviors（不仅是 Debugger）
- ✅ 与其他 Mediator 实现一致
- ✅ 可扩展性强

**实现步骤**:

1. **修改 `CatgaMediator` 构造函数，注入 Pipeline Behaviors**:
```csharp
public class CatgaMediator : ICatgaMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IPipelineBehavior<,>> _behaviors; // 新增

    public CatgaMediator(
        IServiceProvider serviceProvider,
        IEnumerable<IPipelineBehavior<,>> behaviors) // 新增
    {
        _serviceProvider = serviceProvider;
        _behaviors = behaviors;
    }
}
```

2. **在 `SendAsync` 中构建 Pipeline**:
```csharp
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(...)
{
    using var scope = _serviceProvider.CreateScope();
    var handler = scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>();

    // 构建 Pipeline
    PipelineDelegate<TResponse> pipeline = () => handler.HandleAsync(request, cancellationToken);

    // 获取所有 Behaviors 并倒序执行
    var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
    foreach (var behavior in behaviors.Reverse())
    {
        var currentPipeline = pipeline;
        pipeline = () => behavior.HandleAsync(request, currentPipeline, cancellationToken);
    }

    // 执行 Pipeline
    return await pipeline();
}
```

3. **注册 Pipeline Behaviors**:
```csharp
// Program.cs 或 DI 配置
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ReplayableEventCapturer<,>));
```

**预计工作量**: 2-3小时
- 修改 `CatgaMediator.cs`
- 添加 Pipeline 构建逻辑
- 测试所有场景
- 更新文档

---

### 方案 B: 使用 Decorator Pattern 包装 Mediator

**优势**:
- ✅ 不修改现有 Mediator
- ✅ 可插拔
- ✅ 测试简单

**实现**:
```csharp
public class DebuggingMediatorDecorator : ICatgaMediator
{
    private readonly ICatgaMediator _inner;
    private readonly IEventStore _eventStore;

    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(...)
    {
        var sw = Stopwatch.StartNew();
        var result = await _inner.SendAsync(request, cancellationToken);
        sw.Stop();

        // 捕获事件
        await CaptureEvent(request, result, sw.Elapsed);

        return result;
    }
}

// 注册
builder.Services.Decorate<ICatgaMediator, DebuggingMediatorDecorator>();
```

**预计工作量**: 1-2小时

---

### 方案 C: 在 InMemory Transport 中集成 Debugger

**优势**:
- ✅ 所有通过 Transport 的消息都会被捕获
- ✅ 不依赖 Mediator 实现

**实现**:
```csharp
public class InMemoryTransport : IMessageTransport
{
    private readonly IEventStore? _eventStore;

    public async Task PublishAsync<T>(T message, ...)
    {
        // 捕获事件
        await _eventStore?.SaveAsync(CreateReplayableEvent(message));

        // 原有逻辑
        await _channel.Writer.WriteAsync(message, cancellationToken);
    }
}
```

**预计工作量**: 1小时

---

## 🎯 推荐方案

**选择方案 A** (Pipeline Behavior 支持):

1. **符合设计理念**: Catga 基于 Pipeline 架构
2. **一劳永逸**: 支持所有 Behaviors，不仅是 Debugger
3. **可扩展**: 未来可以添加更多 Behaviors (Retry, Validation, Caching 等)
4. **一致性**: 与其他 Mediator 实现 (如基于 MediatR 的) 保持一致

---

## 📋 实现清单

### Phase 1: 核心 Pipeline 支持 (必需)
- [ ] 修改 `CatgaMediator` 构造函数接受 `IPipelineBehavior<,>[]`
- [ ] 实现 Pipeline 构建逻辑 (`SendAsync`, `PublishAsync`)
- [ ] 添加单元测试

### Phase 2: Debugger 集成 (必需)
- [ ] 确保 `ReplayableEventCapturer` 正确注册
- [ ] 测试事件捕获
- [ ] 验证 SignalR 实时推送

### Phase 3: 文档和示例 (推荐)
- [ ] 更新 Mediator 文档
- [ ] 添加 Pipeline Behavior 开发指南
- [ ] OrderSystem 示例展示 Debugger 功能

---

## 🚀 快速临时解决方案 (仅用于演示)

如果需要立即看到 Debugger 工作，可以在 Handler 中手动捕获：

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IEventStore? _eventStore;

    protected override async Task<OrderCreatedResult> HandleCoreAsync(...)
    {
        // 原有逻辑
        var result = ...;

        // 手动捕获 (临时)
        if (_eventStore != null)
        {
            await _eventStore.SaveAsync(new[] { CreateEvent(request, result) });
        }

        return result;
    }
}
```

⚠️ **不推荐用于生产**，仅用于验证 Debugger UI 功能。

---

## 📊 当前状态总结

| 组件 | 状态 | 说明 |
|------|------|------|
| Debugger UI | ✅ 正常 | 可访问，SignalR 集成 |
| Debugger API | ✅ 正常 | 所有端点工作 |
| Event Store | ✅ 正常 | 存储服务正常 |
| SignalR Hub | ✅ 正常 | 实时通信正常 |
| Event Capture | ❌ **不工作** | Pipeline Behavior 未被调用 |
| Data Display | ❌ **无数据** | 因为没有事件被捕获 |

**结论**: Debugger 基础设施完整，但缺少 Mediator Pipeline 支持导致事件捕获失败。

---

## 🔧 相关文件

- **Mediator**: `src/Catga.InMemory/CatgaMediator.cs`
- **Event Capturer**: `src/Catga.Debugger/Pipeline/ReplayableEventCapturer.cs`
- **DI Configuration**: `src/Catga.Debugger/DependencyInjection/DebuggerServiceCollectionExtensions.cs`
- **UI**: `src/Catga.Debugger.AspNetCore/wwwroot/debugger/index.html`
- **Example**: `examples/OrderSystem.Api/Program.cs`

---

**下一步**: 实现方案 A - 为 InMemory Mediator 添加完整的 Pipeline Behavior 支持。

