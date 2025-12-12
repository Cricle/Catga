# Flow DSL 增强详细计划 - 灵活、清晰、可组合的 Expression 表达式设计

## 📋 计划概述

本计划旨在改进 Catga Flow DSL，使其成为一个**可看、可感知、可预测**的工作流引擎，功能完整度不低于 MassTransit，但有自己的独特设计。

**核心目标**：
- ✅ **可看** - 代码清晰易读，流程一目了然
- ✅ **可感知** - 能感知流程执行状态，提供完整的可观测性
- ✅ **可预测** - 行为可预测，恢复机制完整，存储对等性完全

**核心原则**：
- ✅ **清晰的 API** - 每个步骤都是明确的操作，易于理解
- ✅ **完整的控制流** - 支持分支、循环、异常处理，无限嵌套
- ✅ **强大的持久化** - 内存、Redis、NATS 功能完全对等
- ✅ **完整的恢复机制** - 任何时刻都能恢复，不丢失状态
- ✅ **充分的可观测性** - 流程状态、步骤执行、异常信息都可感知
- ✅ **不照抄 MassTransit** - 有自己的设计特色，更简洁直观
- ✅ **类型安全** - 编译时检查，泛型约束
- ✅ **高性能** - 支持并行处理，性能不低于 MassTransit

---

## 🎯 设计目标对标

### 与 MassTransit 的对标
| 功能 | MassTransit | Flow DSL 目标 | 差异 |
|------|-----------|-------------|------|
| 基础步骤 | Send/Publish | Send/Query/Publish | ✅ 相当 |
| 条件分支 | If/Else | If/ElseIf/Else | ✅ 相当 |
| 值分支 | Switch | Switch/Case | ✅ 相当 |
| 循环 | ForEach | ForEach/While/DoWhile | ✅ 更强 |
| 异常处理 | Try/Catch | Try-Catch-Finally | ✅ 相当 |
| 并行处理 | WhenAll/WhenAny | WhenAll/WhenAny | ✅ 相当 |
| 递归调用 | ❌ 不支持 | CallFlow/RecursiveCall | ✅ 更强 |
| 可观测性 | 有限 | 完整的状态感知 | ✅ 更强 |
| 代码清晰度 | 复杂 | 简洁直观 | ✅ 更强 |

### 现有优势
1. **基础步骤** - Send/Query/Publish
2. **分支控制** - If/ElseIf/Else, Switch/Case
3. **循环支持** - ForEach（已实现）
4. **并行处理** - WhenAll/WhenAny
5. **事件钩子** - OnStepCompleted/OnStepFailed/OnFlowCompleted/OnFlowFailed
6. **多存储支持** - InMemory/Redis/NATS（功能对等）

### 工作流完整性分析

**已实现的功能**：
- ✅ 基础步骤（Send/Query/Publish）
- ✅ 条件分支（If/ElseIf/Else）
- ✅ 值分支（Switch/Case）
- ✅ 集合循环（ForEach）
- ✅ 并行处理（WhenAll/WhenAny）
- ✅ 多存储支持（InMemory/Redis/NATS）

**缺失的核心功能**：
1. **控制流完整性**
   - ❌ 缺少 While/Do-While 循环
   - ❌ 缺少 Try-Catch 错误处理
   - ❌ 缺少递归流支持

2. **安全机制**（关键！）
   - ❌ 缺少循环深度限制（防止无限循环）
   - ❌ 缺少递归深度限制（防止栈溢出）
   - ❌ 缺少执行超时控制
   - ❌ 缺少内存溢出保护
   - ❌ 缺少死锁检测

3. **可观测性和可预测性**
   - ❌ 缺少完整的流程状态感知
   - ❌ 缺少步骤执行追踪
   - ❌ 缺少恢复点管理
   - ❌ 缺少执行日志和审计

4. **代码可读性**
   - ⚠️ 复杂的嵌套结构不够清晰
   - ❌ 缺少流程可视化支持

---

## 🔄 改进方案（灵活性增强）

### 核心增强方向

```
灵活性维度          当前状态          目标状态          实现方式
────────────────────────────────────────────────────────────────
控制流完整性        ⚠️ 部分          ✅ 完整          While/DoWhile/Try-Catch
表达式灵活性        ⚠️ 基础          ✅ 高级          Expression 树支持
嵌套能力            ⚠️ 有限          ✅ 无限          递归块结构
可分析性            ❌ 缺失          ✅ 完整          静态分析工具
递归支持            ❌ 缺失          ✅ 支持          流的递归调用
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
// 基于值执行步骤
flow.When(s => s.Items.Count > 10)
    .Send(s => new ProcessBulkOrderCommand(s.OrderId))
    .EndWhen();

// 条件分支
flow.If(s => s.Amount > 1000)
    .Then(f => f.Send(s => new PremiumCommand(s.OrderId)))
    .Else(f => f.Send(s => new StandardCommand(s.OrderId)))
    .EndIf();
```

**实现方式**：
- 扩展条件表达式支持 `Expression<Func<TState, bool>>`
- 支持复杂的 Expression 树编译
- 在执行时计算条件值

**关键文件**：
- `FlowConfig.cs` - 扩展 `When` 方法
- `DslFlowExecutor.cs` - 编译和执行 Expression

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


---

## 🏗️ 灵活性增强实现路线图

### 分层实现策略（基于 Flow DSL 现有架构）

```
┌─────────────────────────────────────────────────────────────────┐
│                Flow DSL 灵活性增强分层设计                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 1 层：基础步骤（已完成）                              │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ✅ Send/Query/Publish - 基础步骤                         │   │
│  │ ✅ If/ElseIf/Else - 条件分支                            │   │
│  │ ✅ Switch/Case - 值分支                                 │   │
│  │ ✅ ForEach - 集合循环                                   │   │
│  │ ✅ WhenAll/WhenAny - 并行处理                           │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 2 层：控制流增强（优先级 1）                          │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ⏳ While/DoWhile/Repeat - 条件循环                      │   │
│  │ ⏳ Try-Catch-Finally - 异常处理                         │   │
│  │ ⏳ 无限嵌套支持 - 块结构可任意嵌套                       │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 第 3 层：表达式增强（优先级 2）                          │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │ ⏳ Expression 树支持 - 复杂条件和值选择                  │   │
│  │ ⏳ 递归流调用 - 流的嵌套执行                             │   │
│  │ ⏳ 静态分析工具 - 流程分析和优化                         │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

### 核心设计原则（学习 MSIL 的灵活性，但不复制其栈式实现）

1. **清晰的步骤语义** - 每个步骤作用明确
   ```csharp
   // 清晰易懂的 API
   flow.Send(...)        // 发送请求
   flow.If(...)          // 条件分支
   flow.While(...)       // 条件循环
   flow.Try(...)         // 异常处理
   ```

2. **完整的控制流** - 支持所有基本的控制流模式
   ```csharp
   // 分支、循环、异常处理都完全支持
   flow.If(...).ElseIf(...).Else().EndIf()
   flow.While(...).EndWhile()
   flow.Try(...).Catch(...).Finally(...).EndTry()
   ```

3. **无限嵌套能力** - 块结构可以任意嵌套
   ```csharp
   // 可以任意嵌套，像 MSIL 的块结构一样灵活
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

4. **灵活的表达式支持** - 支持复杂的 Expression 树
   ```csharp
   // 支持复杂的条件和值选择
   flow.When(s => s.Items.Any(i => i.Price > 100))
   flow.Select(s => s.Items.Where(i => i.IsValid))
   flow.Map(s => new { s.Amount, s.Items })
   ```

5. **可分析和优化** - 支持静态分析和优化
   ```csharp
   // 可以被分析和优化
   var analyzer = new FlowAnalyzer(flow);
   var optimized = analyzer.Optimize();
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

## 📝 灵活性增强实现检查清单

### 第 1 层：基础步骤（已完成 ✅）
- [x] Send/Query/Publish - 基础步骤
- [x] If/ElseIf/Else - 条件分支
- [x] Switch/Case - 值分支
- [x] ForEach - 集合循环
- [x] WhenAll/WhenAny - 并行处理

### 第 2 层：控制流增强（优先级 1）

#### A. While/DoWhile/Repeat 循环
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

#### B. Try-Catch-Finally 异常处理
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

#### C. 无限嵌套支持
- [ ] 验证现有的嵌套支持（If/Switch/ForEach）
- [ ] 确保 While/DoWhile/Repeat 支持嵌套
- [ ] 确保 Try-Catch 支持嵌套
- [ ] 编写嵌套测试（15+ 个）

#### D. 存储对等性和恢复机制
- [ ] 验证 While/DoWhile/Repeat 在内存存储中的恢复
- [ ] 验证 While/DoWhile/Repeat 在 Redis 存储中的恢复
- [ ] 验证 While/DoWhile/Repeat 在 NATS 存储中的恢复
- [ ] 验证 Try-Catch 在所有存储中的恢复
- [ ] 确保循环计数器和异常状态正确持久化
- [ ] 编写存储对等性测试（20+ 个）

#### E. 安全机制（关键！）
**循环安全**：
- [ ] 添加循环深度限制（默认 1000）
- [ ] 添加循环迭代次数限制（默认 10000）
- [ ] 添加循环执行超时控制（默认 5 分钟）
- [ ] 循环超限时抛出 `FlowExecutionException`
- [ ] 在所有存储中持久化循环计数器
- [ ] 编写循环安全测试（15+ 个）

**递归安全**：
- [ ] 添加递归深度限制（默认 100）
- [ ] 添加递归调用栈大小监控
- [ ] 添加递归执行超时控制（默认 10 分钟）
- [ ] 递归超限时抛出 `RecursionLimitExceededException`
- [ ] 在所有存储中持久化递归深度
- [ ] 编写递归安全测试（15+ 个）

**通用安全**：
- [ ] 添加内存使用监控和限制
- [ ] 添加死锁检测机制
- [ ] 添加执行日志和审计
- [ ] 添加异常恢复策略
- [ ] 编写安全测试（20+ 个）

**第 2 层小计**：
- 代码量：500-600 行
- 测试：120-150 个（包括安全测试）
- 工期：3-4 周

### 第 3 层：表达式和状态增强（优先级 2）

#### A. Expression 树支持
- [ ] 扩展条件表达式支持 `Expression<Func<TState, bool>>`
- [ ] 实现 Expression 树编译和缓存
- [ ] 支持复杂的条件表达式（不使用 LINQ 风格）
- [ ] 在所有存储中验证 Expression 的恢复
- [ ] 编写单元测试（15+ 个）

#### B. 递归流调用
- [ ] 添加 `CallFlow<TOtherFlow>` 方法
- [ ] 支持状态映射
- [ ] 支持结果合并
- [ ] 管理递归深度
- [ ] 编写单元测试（15+ 个）

**第 3 层小计**：
- 代码量：250-350 行
- 测试：35-50 个
- 工期：2-3 周

### 第 4 层：静态分析和优化（优先级 3）

#### 静态分析和优化
- [ ] 创建 `FlowAnalyzer` 类
- [ ] 实现死代码检测
- [ ] 实现常量折叠
- [ ] 实现条件优化
- [ ] 编写单元测试（15+ 个）

**第 4 层小计**：
- 代码量：200-300 行
- 测试：15+ 个
- 工期：1-2 周

### 第 5 层：文档和示例
- [ ] 更新 API 文档
- [ ] 创建完整的使用示例
- [ ] 创建最佳实践指南
- [ ] 创建性能优化指南

---

## 🚀 预期成果

### 代码质量
- **新增代码**：1000-1300 行（4 层实现 + 安全机制）
- **新增测试**：180-220 个（覆盖所有新特性和安全机制）
- **代码覆盖率**：>95%（包括安全路径）
- **性能提升**：10-20%（通过 Expression 优化）

### 功能增强
- **第 1 层**：✅ 已完成（基础步骤）
- **第 2 层**：⏳ 优先级 1（控制流 + 安全）
  - While/DoWhile/Repeat 循环（带安全限制）
  - Try-Catch-Finally 异常处理
  - 无限嵌套支持
  - **循环安全机制**（深度/迭代/超时限制）
  - **递归安全机制**（深度/栈/超时限制）
  - **通用安全机制**（内存/死锁/审计）
- **第 3 层**：⏳ 优先级 2（表达式和递归）
  - Expression 树支持
  - 递归流调用（带安全限制）
  - 静态分析工具
- **第 4 层**：⏳ 优先级 3（优化）
  - 流程分析和优化

### API 方法增加
- `While(condition)` / `DoWhile()` / `Repeat(times)`
- `Try()` / `Catch<T>()` / `Finally()`
- `CallFlow<T>()` / `RecursiveCall()`
- `Analyze()` / `Optimize()`

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

### 第 2 周（优先级 2 - 表达式和递归）
- **目标**：实现 Expression 树支持和递归流调用
- **代码量**：250-350 行
- **测试**：35-50 个
- **关键里程碑**：
  - Day 1-2：Expression 树支持
  - Day 3-4：递归流调用
  - Day 5：集成测试

### 第 3 周（优先级 3 - 优化）
- **目标**：实现静态分析和优化
- **代码量**：200-300 行
- **测试**：15+ 个
- **关键里程碑**：
  - Day 1-3：分析和优化工具
  - Day 4-5：测试和文档

### 第 4 周（文档和示例）
- **目标**：完成文档和示例
- **产出**：完整的 API 文档和 10+ 示例

---

## 🎯 成功标准

### 功能完整性
- ✅ 完整的控制流支持（While/DoWhile/Try-Catch）
- ✅ 支持任意嵌套的控制流
- ✅ Expression 树支持
- ✅ 递归流调用支持

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

## 💡 关键设计决策

### 1. 学习 MSIL 的灵活性，但不复制其栈式实现
- **学习 MSIL 的优点**：完整的控制流、无限嵌套、清晰的语义
- **采用 Flow DSL 友好的方式**：基于现有架构，而非栈式模型
- **保持 API 一致性**：与现有 Flow DSL API 保持风格一致
- **原因**：MSIL 是低级中间语言，Flow DSL 是高级 DSL，实现方式应该不同

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

### 5. 不包含变量和动态生成
- **不实现流程变量**：避免增加复杂性
- **不实现动态步骤生成**：保持 DSL 的清晰性
- **原因**：专注于核心的控制流增强，保持 API 简洁

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
**设计理念**: 学习 MSIL 的灵活性原则，但采用 Flow DSL 友好的实现方式

---

## 🎯 最终目标

**使 Flow DSL 更加灵活、清晰、可组合和可分析。学习 MSIL 的灵活性设计原则（完整的控制流、无限嵌套、清晰的语义），但采用更符合 Flow DSL 特性的实现方式，而非栈式模型。充分利用 C# 的 Expression 特性，避免重复造轮子，提供更贴合语言的 API 设计。**

核心特点：
- ✅ **灵活性** - 支持所有基本的控制流模式（分支、循环、异常处理）
- ✅ **清晰性** - 每个步骤都是明确的操作，易于理解
- ✅ **可组合性** - 步骤可以自由组合，无限制地嵌套
- ✅ **可分析性** - 可以被工具分析、优化和转换
- ✅ **Flow DSL 友好** - 基于现有架构，保持 API 一致性
- ✅ **不造轮子** - 充分利用 C# 标准库特性
