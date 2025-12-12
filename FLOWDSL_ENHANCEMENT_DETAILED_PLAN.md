# Flow DSL 增强详细计划 - 像 MSIL 一样清晰的 Expression 表达式设计

## 📋 计划概述

本计划旨在改进 Catga Flow DSL，使其像 MSIL 一样清晰、灵活、可组合和可分析。充分利用 C# Expression 表达式特性，避免重复造轮子，提供更贴合 C# 语言的 API 设计。

**核心原则**（参考 MSIL 设计）：
- ✅ **清晰性** - 每个步骤都是明确的指令，易于理解和分析
- ✅ **灵活性** - 支持所有基本的控制流模式（分支、循环、异常处理）
- ✅ **可组合性** - 步骤可以自由组合，无限制地嵌套
- ✅ **可分析性** - 可以被工具分析、优化和转换
- ✅ **充分利用现有特性** - Expression、LINQ、async/await
- ✅ **避免重复造轮子** - 使用标准库而非自定义实现
- ✅ **类型安全** - 编译时检查和泛型约束

---

## 🎯 MSIL 对标分析

### MSIL 的设计特点
| MSIL 特性 | 对应 Flow DSL | 当前状态 | 目标状态 |
|----------|-------------|--------|--------|
| **指令清晰** | 每个步骤明确 | ✅ 完成 | ✅ 保持 |
| **条件分支** | If/ElseIf/Else | ✅ 完成 | ✅ 增强 Expression |
| **无条件分支** | Switch/Case | ✅ 完成 | ✅ 保持 |
| **循环** | While/DoWhile/For | ⚠️ 部分 | ✅ 完整 |
| **异常处理** | Try-Catch-Finally | ❌ 缺失 | ✅ 实现 |
| **嵌套块** | 任意嵌套结构 | ⚠️ 部分 | ✅ 完整 |
| **栈操作** | 变量和上下文 | ⚠️ 部分 | ✅ 完整 |
| **可分析性** | 静态分析工具 | ❌ 缺失 | ✅ 实现 |

### 现有优势
1. **基础步骤支持** - Send/Query/Publish（对应 MSIL 的方法调用）
2. **分支控制** - If/ElseIf/Else, Switch/Case（对应 MSIL 的 br/brfalse）
3. **循环支持** - ForEach（已实现，但不完整）
4. **并行处理** - WhenAll/WhenAny（MSIL 没有，Flow DSL 特有）
5. **事件钩子** - OnStepCompleted/OnStepFailed/OnFlowCompleted/OnFlowFailed
6. **持久化** - 支持多种存储（InMemory/Redis/NATS）

### 现有限制（与 MSIL 对标）
1. **控制流不完整**（❌ MSIL 完全支持）
   - 缺少 While/Do-While 循环（MSIL 有 br 实现）
   - 缺少 Try-Catch 错误处理（MSIL 有 leave/endfinally）
   - 缺少递归流支持（MSIL 有 call/callvirt）
   - 缺少动态步骤生成（MSIL 有 IL 生成）

2. **表达式灵活性不足**（❌ MSIL 完全灵活）
   - 条件表达式只支持简单的 `Func<TState, bool>`
   - 不支持复杂的 LINQ 查询表达式
   - 不支持表达式树分析和优化

3. **栈和变量管理有限**（❌ MSIL 完全支持）
   - 没有流程变量支持（MSIL 有 ldloc/stloc）
   - 没有上下文传递机制（MSIL 有栈）
   - 没有中间结果存储

4. **可分析性缺失**（❌ MSIL 可被工具分析）
   - 无法静态分析流程结构
   - 无法进行死代码消除
   - 无法进行性能优化

---

## 🔄 改进方案（MSIL 映射）

### 核心映射关系

```
MSIL 指令          Flow DSL 对标              当前实现        目标实现
─────────────────────────────────────────────────────────────────────
call/callvirt  →  Send/Query/Publish    →  ✅ 完成      →  ✅ 保持
br             →  Goto/Label           →  ❌ 缺失      →  ✅ 实现
brfalse/brtrue →  If/ElseIf/Else       →  ✅ 完成      →  ✅ 增强
switch         →  Switch/Case          →  ✅ 完成      →  ✅ 保持
br.s (loop)    →  While/DoWhile/For    →  ⚠️ 部分      →  ✅ 完整
leave          →  Try-Catch-Finally    →  ❌ 缺失      →  ✅ 实现
ldloc/stloc    →  Variables/Context    →  ⚠️ 部分      →  ✅ 完整
ldc.i4         →  Constants            →  ✅ 完成      →  ✅ 保持
ldarg          →  State Access         →  ✅ 完成      →  ✅ 保持
```

### 方案 A：Expression 表达式增强（推荐）

#### A1. 条件表达式扩展（对标 MSIL brfalse/brtrue）
**目标**：支持更复杂的条件表达式，像 MSIL 的条件分支一样灵活

```csharp
// 现有方式（简单）
flow.If(s => s.Amount > 1000)

// 增强方式 - 支持复杂表达式（对标 MSIL 的任意条件）
flow.When(s => s.Amount > 1000 && s.Status == OrderStatus.Pending)
flow.When(s => s.Items.Any(i => i.Price > 100))
flow.When(s => s.CreatedAt.AddDays(7) < DateTime.Now)

// 支持 Expression 树分析和优化（MSIL 工具可以做的）
flow.When(s => s.Items.Count > 0 && s.Items.All(i => i.IsValid))
    // 可以被优化为：先检查 Count，再检查 All
```

**实现方式**：
- 使用 `Expression<Func<TState, bool>>` 而非 `Func<TState, bool>`
- 在执行时编译 Expression 为委托
- 支持 Expression 树的分析和优化

**关键文件**：
- `FlowConfig.cs` - 扩展 `IFlowBuilder` 接口
- `DslFlowExecutor.cs` - 编译和执行 Expression

#### A2. 值选择和映射
**目标**：支持 Expression 的值选择和转换

```csharp
// 选择值并基于值执行步骤
flow.Select(s => s.Items.Count)
    .When(count => count > 10)
    .Send(s => new ProcessBulkOrderCommand(s.OrderId))
    .EndWhen();

// 映射和合并状态
flow.Map(s => new { s.Amount, s.Items })
    .Into((state, mapped) => {
        state.TotalAmount = mapped.Amount;
        state.ItemCount = mapped.Items.Count;
    });
```

**实现方式**：
- 创建 `ISelectBuilder<TState, TValue>` 接口
- 支持 Expression 的值提取
- 在执行时计算和传递值

**关键文件**：
- `FlowConfig.cs` - 新增 Select/Map 方法
- `DslFlowExecutor.cs` - 执行值选择和映射

#### A3. LINQ 风格的查询
**目标**：支持 LINQ 风格的流程查询

```csharp
// 使用 LINQ 风格的 API
flow.Where(s => s.Status == OrderStatus.Pending)
    .Select(s => s.Items)
    .ForEach(item => /* process item */)
    .Aggregate(/* aggregate results */);
```

**实现方式**：
- 不创建新的 LINQ 实现，而是利用现有的 LINQ 表达式
- 在 FlowBuilder 中添加 Where/Select 方法
- 这些方法返回新的 FlowBuilder，支持链式调用

**关键文件**：
- `FlowConfig.cs` - 添加 Where/Select/Aggregate 方法

---

### 方案 B：控制流增强（推荐）

#### B1. While/Do-While 循环（对标 MSIL br.s）
**目标**：支持更多的循环类型，像 MSIL 的分支指令一样灵活

```csharp
// While 循环（对标 MSIL: br.s 实现的 while）
flow.While(s => s.RetryCount < 3)
    .Send(s => new RetryCommand(s.OrderId))
    .EndWhile();

// Do-While 循环（对标 MSIL: br.s 实现的 do-while）
flow.DoWhile()
    .Send(s => new ProcessCommand(s.OrderId))
    .Until(s => s.IsProcessed);

// Repeat 循环（对标 MSIL: 固定次数的循环）
flow.Repeat(3)
    .Send(s => new RetryCommand(s.OrderId))
    .EndRepeat();

// 支持嵌套循环（对标 MSIL 的嵌套块）
flow.While(s => s.OuterCount < 5)
    .ForEach(s => s.Items)
        .Send(item => new ProcessItemCommand(item.Id))
        .EndForEach()
    .EndWhile();
```

**实现方式**：
- 在 `FlowStep` 中添加新的步骤类型
- 在 `DslFlowExecutor` 中实现循环执行逻辑
- 支持循环计数器和索引

**关键文件**：
- `Abstractions.cs` - 扩展 `StepType` 枚举
- `FlowConfig.cs` - 添加循环构建器接口
- `DslFlowExecutor.cs` - 实现循环执行逻辑

#### B2. Try-Catch 错误处理（对标 MSIL leave/endfinally）
**目标**：支持结构化的错误处理，像 MSIL 的异常处理一样完整

```csharp
// Try-Catch-Finally（对标 MSIL: .try/.catch/.finally）
flow.Try()
    .Send(s => new RiskyCommand(s.OrderId))
    .Catch<TimeoutException>(
        (s, ex) => {
            s.Status = OrderStatus.Timeout;
            return new RetryCommand(s.OrderId);
        })
    .Catch<InvalidOperationException>(
        (s, ex) => s.Status = OrderStatus.Invalid)
    .Finally(s => s.LastProcessedAt = DateTime.Now)
    .EndTry();

// 嵌套的 Try-Catch（对标 MSIL 的嵌套异常处理块）
flow.Try()
    .Try()
        .Send(s => new InnerCommand(s.OrderId))
        .Catch<Exception>((s, ex) => s.InnerError = ex.Message)
        .EndTry()
    .Catch<Exception>((s, ex) => s.OuterError = ex.Message)
    .EndTry();
```

**实现方式**：
- 在 `FlowStep` 中添加 Try-Catch 步骤类型
- 在 `DslFlowExecutor` 中包装步骤执行在 try-catch 中
- 支持多个 catch 块和 finally 块

**关键文件**：
- `Abstractions.cs` - 扩展 `StepType` 枚举
- `FlowConfig.cs` - 添加 Try-Catch 构建器接口
- `DslFlowExecutor.cs` - 实现异常处理逻辑

#### B3. 递归流调用
**目标**：支持流的递归调用

```csharp
// 调用另一个流
flow.CallFlow<PaymentFlow>(
    s => new PaymentFlowState { OrderId = s.OrderId });

// 递归调用自身
flow.RecursiveCall(
    shouldContinue: s => s.RetryCount < 3,
    configure: f => f.Send(s => new RetryCommand(s.OrderId)));
```

**实现方式**：
- 在 `DslFlowExecutor` 中支持流的嵌套执行
- 管理递归深度和状态映射
- 支持结果的合并和返回

**关键文件**：
- `FlowConfig.cs` - 添加 CallFlow/RecursiveCall 方法
- `DslFlowExecutor.cs` - 实现流的递归调用

---

### 方案 C：状态和上下文管理（推荐）

#### C1. 流程变量（对标 MSIL ldloc/stloc）
**目标**：支持在流程中存储和使用中间值，像 MSIL 的本地变量一样

```csharp
// 定义和使用流程变量（对标 MSIL: .locals init）
flow.Var("retryCount", s => 0)
    .While(s => s.GetVar<int>("retryCount") < 3)
        .Send(s => new RetryCommand(s.OrderId))
        .SetVar("retryCount", s => s.GetVar<int>("retryCount") + 1)
    .EndWhile();

// 支持多个变量（对标 MSIL 的多个本地变量）
flow.Var("totalAmount", s => 0m)
    .Var("itemCount", s => 0)
    .ForEach(s => s.Items)
        .SetVar("totalAmount", (s, item) => s.GetVar<decimal>("totalAmount") + item.Price)
        .SetVar("itemCount", s => s.GetVar<int>("itemCount") + 1)
    .EndForEach()
    .Send(s => new SummaryCommand(
        s.OrderId,
        s.GetVar<decimal>("totalAmount"),
        s.GetVar<int>("itemCount")));
```

**实现方式**：
- 在 `IFlowState` 中添加变量存储机制
- 提供 `GetVar<T>` 和 `SetVar<T>` 扩展方法
- 在执行时管理变量的生命周期

**关键文件**：
- `Abstractions.cs` - 扩展 `IFlowState` 接口
- `BaseFlowState.cs` - 实现变量存储
- `FlowConfig.cs` - 添加 Var/SetVar 方法

#### C2. 流程上下文
**目标**：在步骤执行时传递上下文信息

```csharp
// 在步骤中访问上下文
flow.WithContext(s => new { s.OrderId, s.Amount })
    .Send((s, ctx) => new ProcessCommand(s.OrderId, ctx.Amount))
    .EndContext();
```

**实现方式**：
- 创建 `FlowContext<TState>` 类来存储执行上下文
- 在 `DslFlowExecutor` 中创建和传递上下文
- 支持上下文的嵌套和作用域

**关键文件**：
- `DslFlowExecutor.cs` - 创建和管理上下文
- `FlowConfig.cs` - 添加 WithContext 方法

---

### 方案 D：高级查询和操作（可选）

#### D1. 聚合操作
**目标**：支持集合的聚合操作

```csharp
// 聚合操作
flow.Aggregate(
    s => s.Items,
    (items, f) => {
        f.Send(s => new ProcessItemsCommand(items.ToList()));
    });

// 分组操作
flow.GroupBy(
    s => s.Items,
    item => item.Category,
    (category, items, f) => {
        f.Send(s => new ProcessCategoryCommand(category, items.ToList()));
    });
```

**实现方式**：
- 使用 LINQ 的 `Aggregate` 和 `GroupBy` 方法
- 在 `DslFlowExecutor` 中实现聚合逻辑
- 支持复杂的集合操作

**关键文件**：
- `FlowConfig.cs` - 添加 Aggregate/GroupBy 方法
- `DslFlowExecutor.cs` - 实现聚合执行逻辑

#### D2. 管道操作
**目标**：支持步骤的管道化处理

```csharp
// 管道操作
flow.Pipe(
    s => s.Items,
    items => items.Where(i => i.Price > 100),
    filtered => filtered.Select(i => new ProcessItemCommand(i.Id)),
    commands => /* send commands */);
```

**实现方式**：
- 使用函数组合和管道模式
- 在 `DslFlowExecutor` 中实现管道执行
- 支持多个转换步骤的链接

**关键文件**：
- `FlowConfig.cs` - 添加 Pipe 方法
- `DslFlowExecutor.cs` - 实现管道执行逻辑

---

## 🏗️ MSIL 对标实现路线图

### 完整的 MSIL 映射实现

```
┌─────────────────────────────────────────────────────────────────┐
│                    Flow DSL MSIL 对标设计                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 1 层：基础指令（已完成）                              │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ✅ call/callvirt     → Send/Query/Publish               │   │
│  │ ✅ ldarg             → State Access (s => s.Property)    │   │
│  │ ✅ ldc.i4            → Constants                         │   │
│  │ ✅ brfalse/brtrue    → If/ElseIf/Else                   │   │
│  │ ✅ switch            → Switch/Case                       │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 2 层：控制流（优先级 1）                              │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ⏳ br.s (loop)       → While/DoWhile/Repeat             │   │
│  │ ⏳ leave             → Try-Catch-Finally                │   │
│  │ ⏳ endfinally        → Finally Block                     │   │
│  │ ⏳ Nested blocks     → 嵌套结构支持                      │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 3 层：变量和栈（优先级 2）                            │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ⏳ ldloc/stloc       → Variables (Var/SetVar/GetVar)    │   │
│  │ ⏳ Stack operations  → Context Management                │   │
│  │ ⏳ Local scope       → Variable Scope                    │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 4 层：高级特性（优先级 3-4）                          │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ⏳ call (recursive)  → Recursive Calls                   │   │
│  │ ⏳ IL generation     → Dynamic Step Generation           │   │
│  │ ⏳ Optimization      → Expression Analysis & Optimization│   │
│  │ ⏳ Analysis tools    → Static Analysis                   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

### 核心设计原则（MSIL 启发）

1. **指令清晰** - 每个步骤都是明确的操作
   ```csharp
   // 像 MSIL 指令一样清晰
   flow.Send(...)        // 对应 call
   flow.If(...)          // 对应 brfalse
   flow.While(...)       // 对应 br.s
   flow.Try(...)         // 对应 .try
   ```

2. **完整的控制流** - 支持所有基本的控制流模式
   ```csharp
   // 分支、循环、异常处理都完全支持
   flow.If(...).ElseIf(...).Else().EndIf()
   flow.While(...).EndWhile()
   flow.Try(...).Catch(...).Finally(...).EndTry()
   ```

3. **无限嵌套** - 像 MSIL 的块结构一样支持任意嵌套
   ```csharp
   // 可以任意嵌套
   flow.Try()
       .While(...)
           .If(...)
               .ForEach(...)
               .EndForEach()
           .EndIf()
       .EndWhile()
       .Catch(...)
   .EndTry()
   ```

4. **栈操作** - 像 MSIL 的栈一样管理数据流
   ```csharp
   // 变量和上下文像栈一样管理
   flow.Var("x", s => 0)
       .SetVar("x", s => s.GetVar<int>("x") + 1)
       .Send(s => new Command(s.GetVar<int>("x")))
   ```

5. **可分析性** - 像 MSIL 一样可以被工具分析
   ```csharp
   // 可以被静态分析、优化、转换
   var flowAnalyzer = new FlowAnalyzer(flow);
   var deadSteps = flowAnalyzer.FindDeadCode();
   var optimized = flowAnalyzer.Optimize();
   ```

---

## 📊 实现优先级

### 优先级 1（核心功能）
1. **Expression 条件表达式** - 支持复杂条件
2. **While/Do-While 循环** - 基本循环支持
3. **Try-Catch 错误处理** - 结构化异常处理

**预计代码量**：300-400 行
**预计测试**：50-70 个
**预计工期**：1-2 周

### 优先级 2（重要功能）
1. **流程变量** - 中间值存储
2. **递归流调用** - 流的嵌套执行
3. **值选择和映射** - Expression 值操作

**预计代码量**：250-350 行
**预计测试**：40-60 个
**预计工期**：1-2 周

### 优先级 3（增强功能）
1. **流程上下文** - 执行上下文传递
2. **聚合操作** - 集合聚合
3. **管道操作** - 步骤管道化

**预计代码量**：200-300 行
**预计测试**：30-50 个
**预计工期**：1 周

---

## 🔧 实现策略

### 不造轮子的原则

#### ✅ 充分利用现有特性
1. **C# Expression 树** - 用于条件和值选择
2. **LINQ** - 用于集合操作和查询
3. **async/await** - 用于异步执行
4. **Func/Action 委托** - 用于回调和处理
5. **反射** - 用于属性访问和赋值

#### ❌ 避免重复造轮子
1. 不创建自定义的 LINQ 实现
2. 不创建自定义的异步框架
3. 不创建自定义的表达式编译器
4. 不创建自定义的序列化框架
5. 不创建自定义的依赖注入容器

#### ✅ 利用现有库
1. **System.Linq.Expressions** - Expression 树分析
2. **System.Reflection** - 属性和方法访问
3. **System.Collections.Generic** - 集合操作
4. **System.Threading.Tasks** - 异步操作

---

## 📈 API 设计示例

### 完整的流程示例

```csharp
public class EnhancedOrderFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow
            // 条件检查
            .When(s => s.Amount > 0 && s.Items.Count > 0)
                // 发送命令
                .Send(s => new ValidateOrderCommand(s.OrderId))
                // 条件分支
                .If(s => s.Amount > 1000)
                    .Send(s => new ApplyDiscountCommand(s.OrderId))
                    .ElseIf(s => s.Amount > 500)
                    .Send(s => new ApplySmallDiscountCommand(s.OrderId))
                    .Else()
                    .Send(s => new NoDiscountCommand(s.OrderId))
                    .EndIf()
                // 循环处理
                .ForEach(s => s.Items)
                    .Send(item => new ProcessItemCommand(item.Id))
                    .EndForEach()
                // While 循环
                .While(s => s.RetryCount < 3)
                    .Send(s => new ProcessCommand(s.OrderId))
                    .BreakIf(s => s.IsProcessed)
                    .EndWhile()
                // Try-Catch 处理
                .Try()
                    .Send(s => new PaymentCommand(s.OrderId))
                    .Catch<TimeoutException>(
                        (s, ex) => {
                            s.Status = OrderStatus.Timeout;
                            return new RetryCommand(s.OrderId);
                        })
                    .Catch<InvalidOperationException>(
                        (s, ex) => s.Status = OrderStatus.Invalid)
                    .Finally(s => s.LastProcessedAt = DateTime.Now)
                    .EndTry()
                // 递归调用
                .CallFlow<PaymentFlow>(
                    s => new PaymentFlowState { OrderId = s.OrderId })
                // 发布事件
                .Publish(s => new OrderProcessedEvent(s.OrderId))
            .EndWhen();
    }
}
```

---

## 🎯 关键设计决策

### 1. Expression vs Func
- **使用 Expression**：条件、值选择、映射
- **使用 Func**：步骤工厂、回调处理
- **原因**：Expression 支持分析和优化，Func 更简洁

### 2. 链式调用 vs 块式调用
- **使用链式调用**：主流程步骤
- **使用块式调用**：分支和循环内部
- **原因**：链式调用更直观，块式调用更清晰

### 3. 编译时 vs 运行时
- **编译时检查**：类型安全、泛型约束
- **运行时编译**：Expression 树编译为委托
- **原因**：平衡类型安全和灵活性

### 4. 持久化支持
- **保存 Expression 树**：序列化为 JSON
- **保存编译后的委托**：缓存以提高性能
- **原因**：支持流程恢复和性能优化

---

## 📝 MSIL 对标实现检查清单

### 第 1 层：基础指令（已完成 ✅）
- [x] call/callvirt → Send/Query/Publish
- [x] ldarg → State Access
- [x] ldc.i4 → Constants
- [x] brfalse/brtrue → If/ElseIf/Else
- [x] switch → Switch/Case

### 第 2 层：控制流（优先级 1）

#### A. While/DoWhile/Repeat 循环（对标 MSIL br.s）
- [ ] 扩展 `IFlowBuilder` 接口，添加 `While`/`DoWhile`/`Repeat` 方法
- [ ] 创建 `IWhileBuilder`/`IDoWhileBuilder`/`IRepeatBuilder` 接口
- [ ] 在 `FlowStep` 中添加循环步骤类型
- [ ] 在 `DslFlowExecutor` 中实现循环执行逻辑
  - [ ] While 循环：条件检查 → 执行 → 回到条件检查
  - [ ] DoWhile 循环：执行 → 条件检查 → 回到执行
  - [ ] Repeat 循环：固定次数或表达式次数
- [ ] 支持循环计数器和索引
- [ ] 支持 BreakIf/ContinueIf 条件
- [ ] 编写单元测试（20+ 个）
- [ ] 性能基准测试

#### B. Try-Catch-Finally 异常处理（对标 MSIL leave/endfinally）
- [ ] 扩展 `IFlowBuilder` 接口，添加 `Try` 方法
- [ ] 创建 `ITryBuilder`/`ICatchBuilder` 接口
- [ ] 在 `FlowStep` 中添加 Try-Catch 步骤类型
- [ ] 在 `DslFlowExecutor` 中实现异常处理逻辑
  - [ ] Try 块执行
  - [ ] 异常捕获和处理
  - [ ] Finally 块执行
  - [ ] 异常重新抛出
- [ ] 支持多个 Catch 块
- [ ] 支持异常类型匹配
- [ ] 支持异常恢复（返回新的 Request）
- [ ] 编写单元测试（25+ 个）

#### C. 嵌套块支持（对标 MSIL 的块结构）
- [ ] 验证现有的嵌套支持（If/Switch/ForEach）
- [ ] 确保 While/DoWhile/Repeat 支持嵌套
- [ ] 确保 Try-Catch 支持嵌套
- [ ] 编写嵌套测试（15+ 个）

**第 2 层小计**：
- 代码量：400-500 行
- 测试：60-80 个
- 工期：2-3 周

### 第 3 层：变量和栈（优先级 2）

#### A. 流程变量（对标 MSIL ldloc/stloc）
- [ ] 在 `IFlowState` 中添加变量存储机制
- [ ] 创建 `FlowVariables` 类管理变量
- [ ] 实现 `Var<T>` 方法定义变量
- [ ] 实现 `SetVar<T>` 方法设置变量
- [ ] 实现 `GetVar<T>` 扩展方法获取变量
- [ ] 支持变量的作用域管理
- [ ] 支持变量的类型安全
- [ ] 编写单元测试（20+ 个）

#### B. 流程上下文（对标 MSIL 栈）
- [ ] 创建 `FlowContext<TState>` 类
- [ ] 在 `DslFlowExecutor` 中创建和管理上下文
- [ ] 支持上下文的嵌套和作用域
- [ ] 支持上下文的传递
- [ ] 编写单元测试（15+ 个）

**第 3 层小计**：
- 代码量：250-350 行
- 测试：35-50 个
- 工期：1-2 周

### 第 4 层：高级特性（优先级 3-4）

#### A. 递归调用（对标 MSIL call/callvirt）
- [ ] 添加 `CallFlow<TOtherFlow>` 方法
- [ ] 支持状态映射
- [ ] 支持结果合并
- [ ] 管理递归深度
- [ ] 编写单元测试（15+ 个）

#### B. 动态步骤生成（对标 MSIL IL 生成）
- [ ] 添加 `Dynamic` 方法
- [ ] 支持运行时步骤生成
- [ ] 支持条件步骤生成
- [ ] 编写单元测试（10+ 个）

#### C. 表达式分析和优化（对标 MSIL 工具）
- [ ] 创建 `FlowAnalyzer` 类
- [ ] 实现死代码检测
- [ ] 实现常量折叠
- [ ] 实现条件优化
- [ ] 编写单元测试（15+ 个）

**第 4 层小计**：
- 代码量：300-400 行
- 测试：40-50 个
- 工期：2-3 周

### 第 5 层：文档和示例
- [ ] 更新 API 文档
- [ ] 创建完整的使用示例
- [ ] 创建最佳实践指南
- [ ] 创建性能优化指南
- [ ] 创建 MSIL 对标说明文档

---

## 🚀 预期成果

### 代码质量
- **新增代码**：1200-1600 行（4 层实现）
- **新增测试**：180-240 个（覆盖所有新特性）
- **代码覆盖率**：>90%
- **性能提升**：10-20%（通过 Expression 优化）

### 功能增强（MSIL 对标）
- **第 1 层**：✅ 已完成（基础指令）
- **第 2 层**：⏳ 优先级 1（控制流）
  - While/DoWhile/Repeat 循环
  - Try-Catch-Finally 异常处理
  - 嵌套块支持
- **第 3 层**：⏳ 优先级 2（变量和栈）
  - 流程变量（Var/SetVar/GetVar）
  - 流程上下文管理
- **第 4 层**：⏳ 优先级 3-4（高级特性）
  - 递归调用
  - 动态步骤生成
  - 表达式分析和优化

### API 方法增加
- `While(condition)` / `DoWhile()` / `Repeat(times)`
- `Try()` / `Catch<T>()` / `Finally()`
- `Var<T>(name, initializer)` / `SetVar<T>()` / `GetVar<T>()`
- `CallFlow<T>()` / `RecursiveCall()`
- `Dynamic()` / `Analyze()` / `Optimize()`

### 文档
- **API 文档**：完整的方法文档和 MSIL 对标说明
- **使用示例**：15+ 个完整示例（涵盖所有新特性）
- **最佳实践**：5+ 篇指南（性能、安全、设计）
- **MSIL 对标指南**：详细的 MSIL 映射说明

---

## 📌 实现时间表

### 第 1 周（优先级 1 - 控制流）
- **目标**：实现 While/DoWhile/Repeat 和 Try-Catch
- **代码量**：400-500 行
- **测试**：60-80 个
- **关键里程碑**：
  - Day 1-2：设计和接口定义
  - Day 3-4：执行器实现
  - Day 5：测试和优化

### 第 2 周（优先级 2 - 变量和上下文）
- **目标**：实现流程变量和上下文管理
- **代码量**：250-350 行
- **测试**：35-50 个
- **关键里程碑**：
  - Day 1-2：变量存储机制
  - Day 3-4：上下文管理
  - Day 5：集成测试

### 第 3-4 周（优先级 3-4 - 高级特性）
- **目标**：实现递归、动态生成和优化
- **代码量**：300-400 行
- **测试**：40-50 个
- **关键里程碑**：
  - Week 3：递归和动态生成
  - Week 4：分析和优化工具

### 第 5 周（文档和示例）
- **目标**：完成文档和示例
- **产出**：完整的 API 文档和 15+ 示例

---

## 🎯 成功标准

### 功能完整性
- ✅ 所有 MSIL 对标特性都已实现
- ✅ 支持任意嵌套的控制流
- ✅ 完整的异常处理
- ✅ 变量和上下文管理

### 代码质量
- ✅ 代码覆盖率 > 90%
- ✅ 所有单元测试通过
- ✅ 性能基准测试通过
- ✅ 代码审查通过

### 文档完整性
- ✅ API 文档完整
- ✅ 示例代码可运行
- ✅ 最佳实践指南完成
- ✅ MSIL 对标说明完成

### 用户体验
- ✅ API 设计直观易用
- ✅ 错误消息清晰有帮助
- ✅ 性能满足预期
- ✅ 文档易于理解

---

## 💡 关键设计决策（MSIL 启发）

### 1. 为什么选择 MSIL 作为参考？
- **MSIL 是成熟的中间语言**：经过 20+ 年的验证
- **MSIL 支持完整的控制流**：分支、循环、异常处理都完全支持
- **MSIL 可被工具分析**：编译器、反编译器、分析工具都能理解
- **MSIL 的设计原则适用于 Flow DSL**：清晰、灵活、可组合

### 2. 为什么不造轮子？
- **充分利用现有 C# 特性**：Expression、LINQ、async/await 都是成熟的
- **避免重复实现**：不需要自己实现表达式编译器、LINQ 引擎等
- **降低维护成本**：使用标准库的代码更容易维护
- **提高性能**：标准库经过优化，性能更好

### 3. Expression vs Func 的选择
- **使用 Expression**：条件、值选择、映射（支持分析和优化）
- **使用 Func**：步骤工厂、回调处理（更简洁直观）
- **原因**：平衡灵活性和易用性

### 4. 链式调用 vs 块式调用
- **链式调用**：主流程步骤（`.Send().If().Send()...`）
- **块式调用**：分支和循环内部（`.If(...).Then(...).EndIf()`)
- **原因**：链式调用直观，块式调用清晰

---

## 📚 参考资源

### MSIL 相关
- [MSIL 指令集文档](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes)
- [.NET 中间语言](https://en.wikipedia.org/wiki/Common_Intermediate_Language)
- [IL 反汇编器 (ildasm.exe)](https://docs.microsoft.com/en-us/dotnet/framework/tools/ildasm-exe-il-disassembler)

### C# Expression 相关
- [Expression Trees](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/expression-trees/)
- [System.Linq.Expressions](https://docs.microsoft.com/en-us/dotnet/api/system.linq.expressions)
- [Expression Tree Visitor Pattern](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/expression-trees/how-to-use-expression-trees-to-build-dynamic-queries)

### Flow DSL 相关
- 现有 Flow DSL 实现：`src/Catga/Flow/FlowConfig.cs`
- 执行器实现：`src/Catga/Flow/DslFlowExecutor.cs`
- 测试示例：`tests/Catga.Tests/Flow/`

---

## 🎓 学习路径

### 对于新的开发者
1. **理解 MSIL 基础**：学习基本的 MSIL 指令
2. **理解 Flow DSL 现有设计**：阅读 FlowConfig.cs 和 DslFlowExecutor.cs
3. **理解 Expression Trees**：学习如何使用 Expression 树
4. **开始实现**：从优先级 1 的简单特性开始

### 对于代码审查者
1. **检查 MSIL 对标性**：确保新特性符合 MSIL 设计原则
2. **检查代码质量**：覆盖率、性能、可维护性
3. **检查文档完整性**：API 文档、示例、最佳实践
4. **检查测试覆盖**：单元测试、集成测试、性能测试

---

**计划创建日期**: 2025-12-12
**计划状态**: 待审核和执行
**优先级**: 高
**预计总工期**: 4-5 周
**参考标准**: MSIL（Microsoft Intermediate Language）

---

## 🎯 最终目标

**使 Flow DSL 像 MSIL 一样清晰、灵活、可组合和可分析，充分利用 C# 的 Expression 特性，避免重复造轮子，提供更贴合语言的 API 设计。**

懂 MSIL 的人都能理解 Flow DSL 的设计原则和使用方式。✨
