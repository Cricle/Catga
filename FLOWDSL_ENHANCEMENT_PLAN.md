# Flow DSL 增强计划 - 更自由的表达式设计

## 📋 计划概述

本计划旨在改进 Catga Flow DSL，使其更加自由灵活，并充分利用 C# Expression 表达式特性，提供更贴合 C# 语言的 API 设计。

---

## 🎯 核心目标

### 主要目标
1. **更自由的表达式支持** - 支持复杂的 Lambda 表达式和 LINQ 查询
2. **Expression 树集成** - 充分利用 Expression 树进行编译和优化
3. **灵活的步骤组合** - 支持更多的步骤组合方式
4. **类型安全的 API** - 保持强类型检查，避免运行时错误
5. **更好的性能** - 通过 Expression 编译优化执行性能

### 次要目标
1. 简化 API 复杂性
2. 提供更直观的 DSL 语法
3. 支持条件和循环的更多变体
4. 提供更好的错误提示

---

## 📊 当前设计分析

### 现有特性
- ✅ 基础的 Send/Query/Publish 步骤
- ✅ If/ElseIf/Else 分支
- ✅ Switch/Case 分支
- ✅ ForEach 循环
- ✅ WhenAll/WhenAny 并行
- ✅ 事件钩子

### 现有限制
- ❌ 表达式灵活性不足
- ❌ 复杂条件表达式支持有限
- ❌ 缺少 While/Do-While 循环
- ❌ 缺少 Try-Catch 错误处理
- ❌ 缺少递归流支持
- ❌ 缺少动态步骤生成
- ❌ 缺少流程变量支持

---

## 🔄 改进方案

### 阶段 1：Expression 树增强（优先级：高）

#### 1.1 创建 ExpressionFlowBuilder
```csharp
public interface IExpressionFlowBuilder<TState> where TState : class, IFlowState
{
    // 基于 Expression 的条件
    IExpressionFlowBuilder<TState> When(Expression<Func<TState, bool>> condition);

    // 基于 Expression 的值选择
    IExpressionFlowBuilder<TState> Select<TValue>(
        Expression<Func<TState, TValue>> selector,
        Action<TValue, IFlowBuilder<TState>> configure);

    // 基于 Expression 的属性更新
    IExpressionFlowBuilder<TState> Update(
        Expression<Func<TState, object>> property,
        Expression<Func<TState, object>> valueExpression);

    // 基于 Expression 的过滤
    IExpressionFlowBuilder<TState> Where(Expression<Func<TState, bool>> predicate);

    // 基于 Expression 的映射
    IExpressionFlowBuilder<TState> Map<TResult>(
        Expression<Func<TState, TResult>> mapper,
        Expression<Func<TState, TResult, TState>> merger);
}
```

**实现要点**：
- 编译 Expression 树为委托
- 支持 Expression 树的分析和优化
- 提供更好的错误信息

**代码量估计**：200-300 行

#### 1.2 创建 ExpressionAnalyzer
```csharp
public class ExpressionAnalyzer
{
    // 分析 Expression 树
    public static ExpressionInfo Analyze<T>(Expression<Func<T, object>> expression);

    // 提取属性访问链
    public static PropertyChain ExtractPropertyChain(Expression expression);

    // 检测副作用
    public static bool HasSideEffects(Expression expression);

    // 优化 Expression 树
    public static Expression Optimize(Expression expression);
}
```

**实现要点**：
- 使用 ExpressionVisitor 遍历树
- 识别属性访问、方法调用等
- 进行常数折叠和死代码消除

**代码量估计**：150-200 行

---

### 阶段 2：循环和控制流增强（优先级：高）

#### 2.1 While 循环支持
```csharp
public interface IFlowBuilder<TState>
{
    // While 循环
    IWhileBuilder<TState> While(Expression<Func<TState, bool>> condition);

    // Do-While 循环
    IDoWhileBuilder<TState> DoWhile(Expression<Func<TState, bool>> condition);

    // Repeat 循环
    IRepeatBuilder<TState> Repeat(int times);
    IRepeatBuilder<TState> Repeat(Expression<Func<TState, int>> timesSelector);
}

public interface IWhileBuilder<TState> where TState : class, IFlowState
{
    IWhileBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IWhileBuilder<TState> EndWhile();
}
```

**实现要点**：
- 在执行器中实现循环逻辑
- 支持循环计数器和索引
- 提供循环中断和继续机制

**代码量估计**：250-350 行

#### 2.2 Try-Catch 错误处理
```csharp
public interface IFlowBuilder<TState>
{
    ITryBuilder<TState> Try();
}

public interface ITryBuilder<TState> where TState : class, IFlowState
{
    ITryBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    ICatchBuilder<TState> Catch<TException>(
        Action<TState, TException> handler) where TException : Exception;
    ITryBuilder<TState> Finally(Action<TState> handler);
    IFlowBuilder<TState> EndTry();
}
```

**实现要点**：
- 包装步骤执行在 try-catch 中
- 支持多个 catch 块
- 支持 finally 块

**代码量估计**：200-250 行

---

### 阶段 3：动态和递归支持（优先级：中）

#### 3.1 动态步骤生成
```csharp
public interface IFlowBuilder<TState>
{
    // 动态生成步骤
    IFlowBuilder<TState> Dynamic(
        Func<TState, IEnumerable<FlowStep>> stepGenerator);

    // 条件性步骤
    IFlowBuilder<TState> IfPresent<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure)
        where TValue : class;
}
```

**实现要点**：
- 在运行时生成步骤
- 支持条件性的步骤包含
- 动态步骤的持久化

**代码量估计**：150-200 行

#### 3.2 递归流支持
```csharp
public interface IFlowBuilder<TState>
{
    // 递归调用另一个流
    IStepBuilder<TState> CallFlow<TOtherFlow>(
        Expression<Func<TState, IFlowState>> stateMapper)
        where TOtherFlow : FlowConfig<IFlowState>;

    // 递归调用自身
    IStepBuilder<TState> RecursiveCall(
        Expression<Func<TState, bool>> shouldContinue,
        Action<IFlowBuilder<TState>> configure);
}
```

**实现要点**：
- 支持流的嵌套调用
- 处理递归深度限制
- 管理递归状态

**代码量估计**：200-250 行

---

### 阶段 4：流程变量和上下文（优先级：中）

#### 4.1 流程变量支持
```csharp
public interface IFlowBuilder<TState>
{
    // 定义流程变量
    IFlowBuilder<TState> Var<TValue>(
        string name,
        Expression<Func<TState, TValue>> initializer);

    // 更新流程变量
    IFlowBuilder<TState> SetVar<TValue>(
        string name,
        Expression<Func<TState, TValue>> valueExpression);

    // 使用流程变量
    IFlowBuilder<TState> UseVar<TValue>(
        string name,
        Action<IFlowBuilder<TState>, TValue> configure);
}
```

**实现要点**：
- 在流程上下文中存储变量
- 支持变量的类型安全访问
- 变量的生命周期管理

**代码量估计**：150-200 行

#### 4.2 流程上下文
```csharp
public class FlowContext<TState> where TState : class, IFlowState
{
    public TState State { get; }
    public Dictionary<string, object> Variables { get; }
    public int CurrentStepIndex { get; }
    public FlowPosition Position { get; }
    public CancellationToken CancellationToken { get; }

    public T GetVar<T>(string name);
    public void SetVar<T>(string name, T value);
}
```

**实现要点**：
- 传递上下文到每个步骤
- 支持变量的动态访问
- 线程安全的变量存储

**代码量估计**：100-150 行

---

### 阶段 5：高级查询支持（优先级：低）

#### 5.1 LINQ 风格的 API
```csharp
public interface IFlowBuilder<TState>
{
    // 链式查询
    IFlowBuilder<TState> Chain(
        Expression<Func<TState, IEnumerable<IRequest>>> requestsSelector);

    // 聚合操作
    IFlowBuilder<TState> Aggregate<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, IRequest>> requestFactory);

    // 分组操作
    IFlowBuilder<TState> GroupBy<TKey, TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector,
        Action<TKey, IEnumerable<TValue>, IFlowBuilder<TState>> configure);
}
```

**实现要点**：
- 支持复杂的集合操作
- 编译 LINQ 表达式为执行计划
- 优化执行性能

**代码量估计**：200-300 行

---

## 📈 实现路线图

### 第 1 周：Expression 树增强
- [ ] 创建 ExpressionFlowBuilder 接口
- [ ] 实现 ExpressionAnalyzer
- [ ] 集成到 FlowBuilder
- [ ] 编写单元测试（30+ 个）

**预计代码**: 400-500 行
**预计测试**: 30-40 个

### 第 2 周：循环和控制流
- [ ] 实现 While/DoWhile 循环
- [ ] 实现 Try-Catch 错误处理
- [ ] 更新执行器支持新特性
- [ ] 编写单元测试（40+ 个）

**预计代码**: 450-550 行
**预计测试**: 40-50 个

### 第 3 周：动态和递归支持
- [ ] 实现动态步骤生成
- [ ] 实现递归流调用
- [ ] 实现流程变量
- [ ] 编写单元测试（30+ 个）

**预计代码**: 400-500 行
**预计测试**: 30-40 个

### 第 4 周：高级查询和优化
- [ ] 实现 LINQ 风格 API
- [ ] 性能优化
- [ ] 文档和示例
- [ ] 集成测试（20+ 个）

**预计代码**: 300-400 行
**预计测试**: 20-30 个

---

## 🎯 关键设计原则

### 1. Expression 优先
- 所有复杂操作都应该支持 Expression 表达式
- 提供 Expression 树分析和优化
- 编译 Expression 为高效的委托

### 2. 类型安全
- 保持强类型检查
- 在编译时捕获错误
- 提供 IntelliSense 支持

### 3. 灵活性
- 支持多种步骤组合方式
- 允许自定义步骤类型
- 支持扩展和插件

### 4. 性能
- 编译 Expression 树为委托
- 缓存编译结果
- 优化执行路径

### 5. 可维护性
- 清晰的 API 设计
- 完整的文档
- 详细的错误信息

---

## 📊 预期成果

### 代码量
- **新增代码**: 1500-2000 行
- **新增测试**: 120-160 个
- **文档**: 50+ 页

### 性能改进
- **Expression 编译**: 10-20% 性能提升
- **执行优化**: 15-25% 性能提升
- **内存使用**: 5-10% 减少

### 功能增强
- **新特性**: 8-10 个主要特性
- **API 方法**: 30-40 个新方法
- **支持的模式**: 20+ 种

---

## 🔗 相关文件

### 需要修改的文件
- `src/Catga/Flow/FlowConfig.cs` - 添加新接口
- `src/Catga/Flow/DslFlowExecutor.cs` - 实现新执行逻辑
- `src/Catga/Flow/Abstractions.cs` - 添加新抽象

### 需要创建的文件
- `src/Catga/Flow/ExpressionFlowBuilder.cs` - Expression 支持
- `src/Catga/Flow/ExpressionAnalyzer.cs` - Expression 分析
- `src/Catga/Flow/FlowContext.cs` - 流程上下文
- `src/Catga/Flow/LoopBuilders.cs` - 循环支持
- `src/Catga/Flow/ErrorHandlingBuilders.cs` - 错误处理

### 测试文件
- `tests/Catga.Tests/Flow/ExpressionFlowBuilderTests.cs`
- `tests/Catga.Tests/Flow/LoopBuilderTests.cs`
- `tests/Catga.Tests/Flow/ErrorHandlingTests.cs`
- `tests/Catga.Tests/Flow/DynamicFlowTests.cs`

---

## 💡 示例用法

### 使用 Expression 的条件
```csharp
flow.When(s => s.Amount > 1000)
    .Send(s => new ProcessLargeOrderCommand(s.OrderId))
    .EndWhen();
```

### 使用 While 循环
```csharp
flow.While(s => s.RetryCount < 3)
    .Send(s => new RetryCommand(s.OrderId))
    .EndWhile();
```

### 使用 Try-Catch
```csharp
flow.Try()
    .Send(s => new RiskyCommand(s.OrderId))
    .Catch<TimeoutException>(
        (s, ex) => s.Status = OrderStatus.Timeout)
    .EndTry();
```

### 使用流程变量
```csharp
flow.Var("retryCount", s => 0)
    .While(s => s.GetVar<int>("retryCount") < 3)
        .Send(s => new RetryCommand(s.OrderId))
        .SetVar("retryCount", s => s.GetVar<int>("retryCount") + 1)
    .EndWhile();
```

---

## 📝 下一步

1. **评审计划** - 确认优先级和范围
2. **设计详细** - 完成详细的 API 设计
3. **实现第 1 阶段** - Expression 树增强
4. **迭代改进** - 根据反馈调整设计
5. **文档和示例** - 创建完整的文档和示例

---

**计划创建日期**: 2025-12-12
**计划状态**: 待审核
**优先级**: 高
**预计工期**: 4 周

---

**这个计划旨在使 Flow DSL 更加自由灵活，充分利用 C# 的 Expression 特性，提供更贴合语言的 API 设计。** 🚀
