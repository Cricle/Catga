# Flow DSL 代码复用指南

## 问题分析

当前 Flow DSL 配置中存在大量重复代码：

```csharp
// 重复模式 1: ForEach + OnComplete + ContinueOnFailure
flow.ForEach(s => s.Items)
    .WithParallelism(5)
    .Configure((item, f) => { /* ... */ })
    .OnComplete(s => { /* ... */ })
    .ContinueOnFailure()
    .EndForEach();

// 重复模式 2: 简单的 Send 命令
flow.Send(s => new SomeCommand(s.Property));

// 重复模式 3: If/Else 条件分支
flow.If(s => s.Value > threshold)
    .EndIf();
```

## 解决方案

### 方案 1: 使用 BaseFlowState 减少 IFlowState 实现重复

在主库中创建 `BaseFlowState` 基类（已实现）：

```csharp
// src/Catga/Flow/Dsl/BaseFlowState.cs
public abstract class BaseFlowState : IFlowState
{
    public string? FlowId { get; set; }

    public virtual bool HasChanges => true;
    public virtual int GetChangedMask() => 0;
    public virtual bool IsFieldChanged(int fieldIndex) => false;
    public virtual void ClearChanges() { }
    public virtual void MarkChanged(int fieldIndex) { }
    public virtual IEnumerable<string> GetChangedFieldNames() { yield break; }
}
```

**在各个库中使用**：

```csharp
// 之前 - 重复的 IFlowState 实现
[FlowState]
public partial class PaymentFlowState : IFlowState
{
    public string? FlowId { get; set; }
    [FlowStateField] private string _paymentId = string.Empty;
    [FlowStateField] private decimal _amount;

    // 重复的方法实现
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    // ... 更多重复代码
}

// 之后 - 使用基类
[FlowState]
public partial class PaymentFlowState : BaseFlowState
{
    [FlowStateField] private string _paymentId = string.Empty;
    [FlowStateField] private decimal _amount;
    // 不需要重复实现 IFlowState 方法！
}
```

### 方案 2: 创建具体的可复用 Flow 配置类

在 OrderSystem.Api 中创建具体的可复用实现：

```csharp
// examples/OrderSystem.Api/Flows/ReusableFlowConfigurations.cs

// 可复用的并行 ForEach 配置
public class ParallelForEachFlow : FlowConfig<ParallelForEachState>
{
    private readonly int _parallelism;
    private readonly string _flowName;
    private readonly Action<IFlowBuilder<ParallelForEachState>> _configure;

    public ParallelForEachFlow(
        string flowName,
        int parallelism = 5,
        Action<IFlowBuilder<ParallelForEachState>>? configure = null)
    {
        _flowName = flowName;
        _parallelism = parallelism;
        _configure = configure ?? (f => { });
    }

    protected override void Configure(IFlowBuilder<ParallelForEachState> flow)
    {
        flow.Name(_flowName);

        var forEachBuilder = flow.ForEach(s => s.Items)
            .WithParallelism(_parallelism)
            .Configure((item, f) => { /* process item */ })
            .OnComplete(s => { /* aggregate results */ });

        forEachBuilder.ContinueOnFailure();
        forEachBuilder.EndForEach();

        _configure(flow);
    }
}

// 可复用的简单 Send 配置
public class SimpleSendFlow : FlowConfig<SimpleSendState>
{
    private readonly string _flowName;
    private readonly int _retryCount;
    private readonly Func<SimpleSendState, IRequest> _commandFactory;

    public SimpleSendFlow(
        string flowName,
        Func<SimpleSendState, IRequest> commandFactory,
        int retryCount = 0)
    {
        _flowName = flowName;
        _commandFactory = commandFactory;
        _retryCount = retryCount;
    }

    protected override void Configure(IFlowBuilder<SimpleSendState> flow)
    {
        flow.Name(_flowName);

        if (_retryCount > 0)
        {
            flow.Retry(_retryCount);
        }

        flow.Send(_commandFactory);
    }
}
```

### 方案 3: 使用扩展方法简化常见操作

```csharp
// examples/OrderSystem.Api/Flows/FlowBuilderExtensions.cs

public static class FlowBuilderExtensions
{
    /// <summary>
    /// 配置标准的并行 ForEach 模式
    /// </summary>
    public static void ConfigureParallelForEach<TState>(
        this IFlowBuilder<TState> flow,
        Func<TState, IEnumerable<object>> itemsSelector,
        int parallelism = 5,
        Action<TState>? onComplete = null)
        where TState : IFlowState
    {
        var forEachBuilder = flow.ForEach(itemsSelector)
            .WithParallelism(parallelism)
            .Configure((item, f) => { /* ... */ });

        if (onComplete != null)
        {
            forEachBuilder.OnComplete(onComplete);
        }

        forEachBuilder.ContinueOnFailure();
        forEachBuilder.EndForEach();
    }

    /// <summary>
    /// 配置标准的条件分支模式
    /// </summary>
    public static void ConfigureConditional<TState>(
        this IFlowBuilder<TState> flow,
        Func<TState, bool> condition,
        Action<IFlowBuilder<TState>>? thenAction = null)
        where TState : IFlowState
    {
        var ifBuilder = flow.If(condition);
        thenAction?.Invoke(flow);
        ifBuilder.EndIf();
    }
}
```

**使用扩展方法**：

```csharp
// 简化前
flow.ForEach(s => s.Items)
    .WithParallelism(5)
    .Configure((item, f) => { /* ... */ })
    .OnComplete(s => { /* ... */ })
    .ContinueOnFailure()
    .EndForEach();

// 简化后
flow.ConfigureParallelForEach(
    s => s.Items,
    parallelism: 5,
    onComplete: s => { /* ... */ });
```

### 方案 4: 使用模板方法模式创建具体的 Flow 类

```csharp
// examples/OrderSystem.Api/Flows/TemplateFlows.cs

/// <summary>
/// 模板：并行处理流
/// </summary>
public abstract class ParallelProcessingFlow<TState> : FlowConfig<TState>
    where TState : IFlowState
{
    protected abstract string FlowName { get; }
    protected abstract int Parallelism { get; }
    protected abstract void ProcessItem(object item, IFlowBuilder<TState> flow);
    protected abstract void OnProcessingComplete(TState state);

    protected sealed override void Configure(IFlowBuilder<TState> flow)
    {
        flow.Name(FlowName);

        var forEachBuilder = flow.ForEach(GetItems)
            .WithParallelism(Parallelism)
            .Configure((item, f) => ProcessItem(item, f))
            .OnComplete(OnProcessingComplete);

        forEachBuilder.ContinueOnFailure();
        forEachBuilder.EndForEach();
    }

    protected abstract IEnumerable<object> GetItems(TState state);
}

// 具体实现
public class ShippingFlow : ParallelProcessingFlow<ShippingFlowState>
{
    protected override string FlowName => "shipping-orchestration";
    protected override int Parallelism => 3;

    protected override IEnumerable<object> GetItems(ShippingFlowState state)
        => new[] { "FedEx", "UPS", "DHL" };

    protected override void ProcessItem(object item, IFlowBuilder<ShippingFlowState> flow)
    {
        // 处理每个承运商
    }

    protected override void OnProcessingComplete(ShippingFlowState state)
    {
        // 选择最优报价
    }
}
```

## 实施建议

### 优先级 1: 立即实施
- ✅ 使用 `BaseFlowState` 替换所有 `IFlowState` 实现
- 这可以减少 50% 的 FlowState 代码

### 优先级 2: 短期实施
- 创建扩展方法简化常见操作
- 这可以减少 30% 的 Flow 配置代码

### 优先级 3: 长期优化
- 根据具体需求创建可复用的 Flow 配置类
- 使用模板方法模式标准化 Flow 结构

## 代码对比

### 代码量减少示例

**之前**（每个 FlowState 类）：
```csharp
[FlowState]
public partial class MyFlowState : IFlowState
{
    public string? FlowId { get; set; }
    [FlowStateField] private string _field1;
    [FlowStateField] private int _field2;

    // 重复的 IFlowState 实现 (8 行)
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
```

**之后**（使用 BaseFlowState）：
```csharp
[FlowState]
public partial class MyFlowState : BaseFlowState
{
    [FlowStateField] private string _field1;
    [FlowStateField] private int _field2;
    // 自动继承 IFlowState 实现！
}
```

**减少代码量**: 8 行 → 0 行 (100% 减少)

## 总结

通过以上方案，可以显著减少 Flow DSL 配置中的重复代码：

| 方案 | 减少代码量 | 实施难度 | 优先级 |
|------|----------|--------|-------|
| BaseFlowState | 50% | 低 | 1 |
| 扩展方法 | 30% | 中 | 2 |
| 可复用 Flow 类 | 20% | 中 | 3 |
| 模板方法模式 | 15% | 高 | 3 |

**总体可减少代码量**: 50-80%
