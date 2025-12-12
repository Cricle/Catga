# OrderSystem.Api 重复代码分析与优化方案

## 概述

本文档分析 OrderSystem.Api 中的重复代码，并提供具体的优化方案。

---

## 1. FlowState 重复代码

### 问题：CreateOrderFlowState 和 OrderFlowState 没有使用 BaseFlowState

**当前代码（重复）**：
```csharp
// Messages/Commands.cs - CreateOrderFlowState
[FlowState]
public partial class CreateOrderFlowState : IFlowState
{
    public string? FlowId { get; set; }
    [FlowStateField]
    private string? _orderId;
    [FlowStateField]
    private decimal _totalAmount;
    [FlowStateField]
    private bool _stockReserved;
    public string? CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

// Flows/ComprehensiveOrderFlow.cs - OrderFlowState
[FlowState]
public partial class OrderFlowState : IFlowState
{
    public string? FlowId { get; set; }
    [FlowStateField]
    private string? _orderId;
    [FlowStateField]
    private decimal _totalAmount;
    // ... 更多字段
}
```

**优化后（使用 BaseFlowState）**：
```csharp
// Messages/Commands.cs - CreateOrderFlowState
[FlowState]
public partial class CreateOrderFlowState : BaseFlowState
{
    [FlowStateField]
    private string? _orderId;
    [FlowStateField]
    private decimal _totalAmount;
    [FlowStateField]
    private bool _stockReserved;
    public string? CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

// Flows/ComprehensiveOrderFlow.cs - OrderFlowState
[FlowState]
public partial class OrderFlowState : BaseFlowState
{
    [FlowStateField]
    private string? _orderId;
    [FlowStateField]
    private decimal _totalAmount;
    // ... 更多字段
}
```

**代码减少**：每个类减少 1 行（FlowId 属性）

---

## 2. Command 定义中的重复 MessageId

### 问题 A：简单 Command 的重复 MessageId 实现

**当前代码（重复）**：
```csharp
// 行 103-108
public record SaveOrderFlowCommand(string OrderId, string CustomerId, List<OrderItem> Items, decimal TotalAmount) : IRequest { public long MessageId => 0; }
public record DeleteOrderFlowCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record ReserveStockCommand(string OrderId, List<OrderItem> Items) : IRequest { public long MessageId => 0; }
public record ReleaseStockCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record ConfirmOrderFlowCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record MarkOrderFailedCommand(string OrderId) : IRequest { public long MessageId => 0; }
```

**优化方案 1：创建基础 Command 类**
```csharp
// 创建基础类
public abstract record BaseFlowCommand : IRequest
{
    public long MessageId => 0;
}

// 使用基础类
public record SaveOrderFlowCommand(string OrderId, string CustomerId, List<OrderItem> Items, decimal TotalAmount) : BaseFlowCommand;
public record DeleteOrderFlowCommand(string OrderId) : BaseFlowCommand;
public record ReserveStockCommand(string OrderId, List<OrderItem> Items) : BaseFlowCommand;
public record ReleaseStockCommand(string OrderId) : BaseFlowCommand;
public record ConfirmOrderFlowCommand(string OrderId) : BaseFlowCommand;
public record MarkOrderFailedCommand(string OrderId) : BaseFlowCommand;
```

**代码减少**：6 行 → 1 行（基础类定义）+ 6 行（简化的 record）= 7 行（减少 6 行）

### 问题 B：复杂 Command 的重复 MessageId 属性

**当前代码（重复）**：
```csharp
// 行 115-163：许多 Command 都重复定义相同的 MessageId 属性
public record RequireManagerApprovalCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record NotifyManagerCommand(string OrderId, string Message) : IRequest
{
    public long MessageId { get; init; }
}

public record RequireSeniorStaffReviewCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record AutoApproveOrderCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

// ... 更多重复的 MessageId 定义
```

**优化方案 2：创建基础 Command 类（带返回值）**
```csharp
// 创建基础类
public abstract record BaseCommand : IRequest
{
    public long MessageId { get; init; }
}

public abstract record BaseCommand<TResponse> : IRequest<TResponse>
{
    public long MessageId { get; init; }
}

// 使用基础类
public record RequireManagerApprovalCommand(string OrderId) : BaseCommand<bool>;
public record NotifyManagerCommand(string OrderId, string Message) : BaseCommand;
public record RequireSeniorStaffReviewCommand(string OrderId) : BaseCommand<bool>;
public record AutoApproveOrderCommand(string OrderId) : BaseCommand<bool>;
public record ApplyVIPDiscountCommand(string OrderId, decimal Rate) : BaseCommand<decimal>;
public record AssignPriorityShippingCommand(string OrderId) : BaseCommand<bool>;
public record ApplyStandardDiscountCommand(string OrderId, decimal Rate) : BaseCommand<decimal>;
public record SendWelcomeEmailCommand(string Email) : BaseCommand;
public record ApplyNewCustomerDiscountCommand(string OrderId, decimal Rate) : BaseCommand<decimal>;
public record LogUnknownCustomerTypeCommand(string OrderId) : BaseCommand;
public record CheckInventoryCommand(string ProductId, int Quantity) : BaseCommand<CheckInventoryResult>;
public record ReserveInventoryCommand(string ProductId, int Quantity) : BaseCommand<ReserveInventoryResult>;
public record ProcessPaymentWithStripeCommand(string OrderId, decimal Amount) : BaseCommand<PaymentResult>;
public record ProcessPaymentWithPayPalCommand(string OrderId, decimal Amount) : BaseCommand<PaymentResult>;
public record ProcessPaymentWithSquareCommand(string OrderId, decimal Amount) : BaseCommand<PaymentResult>;
public record GenerateInvoiceCommand(string OrderId) : BaseCommand<string>;
```

**代码减少**：每个 Command 减少 1 行（MessageId 属性）× 15+ 个 Command = 15+ 行

---

## 3. Flow 配置中的重复模式

### 问题：多个 Flow 都有相同的 flow.Name() 和 flow.Retry() 模式

**当前代码（重复）**：
```csharp
// PaymentProcessingFlow
public class PaymentProcessingFlow : FlowConfig<PaymentFlowState>
{
    protected override void Configure(IFlowBuilder<PaymentFlowState> flow)
    {
        flow.Name("payment-processing");
        flow.Retry(3);
        flow.Send(s => new ProcessPaymentCommand(s.PaymentId, s.Amount));
    }
}

// ShippingOrchestrationFlow
public class ShippingOrchestrationFlow : FlowConfig<ShippingFlowState>
{
    protected override void Configure(IFlowBuilder<ShippingFlowState> flow)
    {
        flow.Name("shipping-orchestration");
        // ... 没有 Retry，但有 ForEach
        flow.ForEach(s => new[] { "FedEx", "UPS", "DHL" })
            .WithParallelism(3)
            .Configure((carrier, f) => { })
            .OnComplete(s => { })
            .EndForEach();
    }
}

// InventoryManagementFlow
public class InventoryManagementFlow : FlowConfig<InventoryFlowState>
{
    protected override void Configure(IFlowBuilder<InventoryFlowState> flow)
    {
        flow.Name("inventory-management");
        flow.ForEach(s => s.Products)
            .WithParallelism(5)
            .Configure((product, f) => { })
            .ContinueOnFailure()
            .OnComplete(s => { })
            .EndForEach();
    }
}
```

**优化方案：使用模板方法模式**

已在 FLOW_DSL_REUSE_EXAMPLES.md 中提供了详细的模板方法模式示例。

---

## 4. 优化优先级

| 优先级 | 问题 | 代码减少 | 难度 | 状态 |
|-------|------|--------|------|------|
| 1 | CreateOrderFlowState 和 OrderFlowState 使用 BaseFlowState | 2 行 | 低 | ⏳ 推荐 |
| 2 | 简单 Command 使用 BaseFlowCommand | 6 行 | 低 | ⏳ 推荐 |
| 3 | 复杂 Command 使用 BaseCommand<T> | 15+ 行 | 低 | ⏳ 推荐 |
| 4 | Flow 配置使用模板方法模式 | 10+ 行 | 中 | ⏳ 可选 |

**总体可减少代码量**：30+ 行

---

## 5. 快速修复清单

### 立即可做（优先级 1-3）

- [ ] 修改 CreateOrderFlowState 继承 BaseFlowState
- [ ] 修改 OrderFlowState 继承 BaseFlowState
- [ ] 创建 BaseFlowCommand 基类
- [ ] 创建 BaseCommand<T> 和 BaseCommand 基类
- [ ] 更新所有 Command 定义使用新的基类

### 可选优化（优先级 4）

- [ ] 为 Flow 配置创建模板方法基类
- [ ] 重构现有 Flow 配置使用模板

---

## 6. 实施步骤

### 步骤 1：修复 FlowState（2 分钟）
```csharp
// Messages/Commands.cs
[FlowState]
public partial class CreateOrderFlowState : BaseFlowState
{
    // 移除 public string? FlowId { get; set; }
    // 其他字段保持不变
}

// Flows/ComprehensiveOrderFlow.cs
[FlowState]
public partial class OrderFlowState : BaseFlowState
{
    // 移除 public string? FlowId { get; set; }
    // 其他字段保持不变
}
```

### 步骤 2：创建 Command 基类（5 分钟）
```csharp
// Messages/Commands.cs 顶部添加
public abstract record BaseFlowCommand : IRequest
{
    public long MessageId => 0;
}

public abstract record BaseCommand : IRequest
{
    public long MessageId { get; init; }
}

public abstract record BaseCommand<TResponse> : IRequest<TResponse>
{
    public long MessageId { get; init; }
}
```

### 步骤 3：更新 Command 定义（10 分钟）
```csharp
// 简单 Command
public record SaveOrderFlowCommand(string OrderId, string CustomerId, List<OrderItem> Items, decimal TotalAmount) : BaseFlowCommand;

// 复杂 Command
public record RequireManagerApprovalCommand(string OrderId) : BaseCommand<bool>;
```

---

## 总结

通过应用这些优化方案，可以：
- 减少 30+ 行重复代码
- 提高代码可维护性
- 遵循 DRY（Don't Repeat Yourself）原则
- 使代码更加一致和易读

**预计实施时间**：15-20 分钟
**预计代码减少**：30+ 行
**难度等级**：低
