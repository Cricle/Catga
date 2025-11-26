# 自定义错误处理和自动回滚

本指南介绍如何使用 `SafeRequestHandler` 的虚函数来实现自定义错误处理和自动回滚。

---

## 🎯 概述

`SafeRequestHandler` 提供三个虚函数供你重写：

1. **`OnBusinessErrorAsync`** - 处理业务异常（`CatgaException`）
2. **`OnUnexpectedErrorAsync`** - 处理系统异常（其他 `Exception`）
3. **`OnValidationErrorAsync`** - 处理验证异常（可选）

---

## 🚀 基础用法

### 默认行为

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            throw new CatgaException("Amount must be positive");  // 自动记录日志并返回失败

        return new OrderResult(...);
    }
}
```

**默认行为**：
- ✅ 自动记录警告日志
- ✅ 返回 `CatgaResult.Failure` 包含错误消息
- ✅ 不会中断应用程序

---

## 🎨 自定义业务错误处理

### 示例：添加详细元数据

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    protected override async Task<OrderResult> HandleCoreAsync(...)
    {
        // 业务逻辑
        if (!await _inventory.CheckStockAsync(...))
            throw new CatgaException("Insufficient stock");

        return new OrderResult(...);
    }

    protected override async Task<CatgaResult<OrderResult>> OnBusinessErrorAsync(
        CreateOrder request,
        CatgaException exception,
        CancellationToken ct)
    {
        // 记录自定义日志
        Logger.LogWarning("Order creation failed for customer {CustomerId}: {Error}",
            request.CustomerId, exception.Message);

        // 添加详细元数据
        var metadata = new ResultMetadata();
        metadata.Add("CustomerId", request.CustomerId);
        metadata.Add("RequestedAmount", request.Amount.ToString());
        metadata.Add("ErrorType", "BusinessValidation");
        metadata.Add("Timestamp", DateTime.UtcNow.ToString("O"));

        // 返回自定义错误响应
        return new CatgaResult<OrderResult>
        {
            IsSuccess = false,
            Error = $"Failed to create order: {exception.Message}",
            Exception = exception,
            Metadata = metadata
        };
    }
}
```

---

## 🔄 自动回滚模式

### 示例：订单创建失败回滚

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly IPaymentService _payment;
    private readonly ICatgaMediator _mediator;

    // 跟踪操作状态
    private string? _orderId;
    private bool _orderSaved;
    private bool _inventoryReserved;

    public CreateOrderHandler(...) : base(logger) { }

    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request,
        CancellationToken ct)
    {
        Logger.LogInformation("Starting order creation for customer {CustomerId}",
            request.CustomerId);

        // 步骤 1: 检查库存
        var stockCheck = await _inventory.CheckStockAsync(request.Items, ct);
        if (!stockCheck.IsSuccess)
            throw new CatgaException("Insufficient stock");

        // 步骤 2: 保存订单（检查点 1）
        _orderId = Guid.NewGuid().ToString("N");
        await _repository.SaveAsync(_orderId, request, ct);
        _orderSaved = true;
        Logger.LogInformation("Order saved: {OrderId}", _orderId);

        // 步骤 3: 预留库存（检查点 2）
        var reserveResult = await _inventory.ReserveAsync(_orderId, request.Items, ct);
        if (!reserveResult.IsSuccess)
            throw new CatgaException("Failed to reserve inventory");
        _inventoryReserved = true;
        Logger.LogInformation("Inventory reserved: {OrderId}", _orderId);

        // 步骤 4: 验证支付（可能失败）
        var paymentResult = await _payment.ValidateAsync(request.PaymentMethod, ct);
        if (!paymentResult.IsSuccess)
            throw new CatgaException("Payment validation failed");

        // 步骤 5: 发布成功事件
        await _mediator.PublishAsync(new OrderCreatedEvent(_orderId, ...), ct);

        Logger.LogInformation("✅ Order created successfully: {OrderId}", _orderId);
        return new OrderResult(_orderId, DateTime.UtcNow);
    }

    /// <summary>
    /// 自动回滚所有已完成的操作
    /// </summary>
    protected override async Task<CatgaResult<OrderResult>> OnBusinessErrorAsync(
        CreateOrder request,
        CatgaException exception,
        CancellationToken ct)
    {
        Logger.LogWarning("⚠️ Order creation failed: {Error}. Initiating rollback...",
            exception.Message);

        try
        {
            // 反向回滚（与执行顺序相反）

            // 回滚步骤 3: 释放库存
            if (_inventoryReserved && _orderId != null)
            {
                Logger.LogInformation("Rolling back inventory for order {OrderId}", _orderId);
                await _inventory.ReleaseAsync(_orderId, request.Items, ct);
                Logger.LogInformation("✓ Inventory rollback completed");
            }

            // 回滚步骤 2: 删除订单
            if (_orderSaved && _orderId != null)
            {
                Logger.LogInformation("Rolling back order {OrderId}", _orderId);
                await _repository.DeleteAsync(_orderId, ct);
                Logger.LogInformation("✓ Order deletion completed");
            }

            // 发布失败事件
            if (_orderId != null)
            {
                await _mediator.PublishAsync(new OrderFailedEvent(
                    _orderId,
                    request.CustomerId,
                    exception.Message,
                    DateTime.UtcNow
                ), ct);
            }

            Logger.LogInformation("✅ Rollback completed successfully");
        }
        catch (Exception rollbackEx)
        {
            // 回滚本身失败！记录错误，需要人工介入
            Logger.LogError(rollbackEx,
                "❌ CRITICAL: Rollback failed for order {OrderId}! Manual intervention required.",
                _orderId);
        }

        // 返回详细的错误和回滚信息
        var metadata = new ResultMetadata();
        metadata.Add("OrderId", _orderId ?? "N/A");
        metadata.Add("CustomerId", request.CustomerId);
        metadata.Add("RollbackCompleted", "true");
        metadata.Add("InventoryRolledBack", _inventoryReserved.ToString());
        metadata.Add("OrderDeleted", _orderSaved.ToString());
        metadata.Add("FailureTimestamp", DateTime.UtcNow.ToString("O"));
        metadata.Add("OriginalError", exception.Message);

        return new CatgaResult<OrderResult>
        {
            IsSuccess = false,
            Error = $"Order creation failed: {exception.Message}. All changes have been rolled back.",
            Exception = exception,
            Metadata = metadata
        };
    }
}
```

---

## 🛡️ 处理系统异常

### 示例：捕获意外错误

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    protected override async Task<CatgaResult<OrderResult>> OnUnexpectedErrorAsync(
        CreateOrder request,
        Exception exception,
        CancellationToken ct)
    {
        Logger.LogError(exception,
            "❌ Unexpected system error during order creation for customer {CustomerId}",
            request.CustomerId);

        // 对于系统错误，也尝试回滚
        // 可以复用 OnBusinessErrorAsync 的逻辑
        return await OnBusinessErrorAsync(
            request,
            new CatgaException("System error occurred", exception),
            ct);
    }
}
```

---

## 📋 完整示例：电商订单

### 场景描述

1. **成功流程**：检查库存 → 保存订单 → 预留库存 → 验证支付 → 发布事件
2. **失败流程**：在支付验证失败时，自动回滚库存和订单

### 完整代码

```csharp
using Catga;
using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using Catga.Results;

namespace MyApp.Handlers;

public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly IPaymentService _payment;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    // 状态跟踪
    private string? _orderId;
    private bool _orderSaved;
    private bool _inventoryReserved;

    public CreateOrderHandler(
        IOrderRepository repository,
        IInventoryService inventory,
        IPaymentService payment,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger) : base(logger)
    {
        _repository = repository;
        _inventory = inventory;
        _payment = payment;
        _mediator = mediator;
        _logger = logger;
    }

    protected override async Task<OrderCreatedResult> HandleCoreAsync(
        CreateOrderCommand request,
        CancellationToken ct)
    {
        _logger.LogInformation("🚀 Starting order creation for customer {CustomerId}",
            request.CustomerId);

        // 1. 验证库存
        var stockCheck = await _inventory.CheckStockAsync(request.Items, ct);
        if (!stockCheck.IsSuccess)
        {
            throw new CatgaException(
                $"Insufficient stock for items: {string.Join(", ", request.Items.Select(i => i.ProductId))}");
        }
        _logger.LogInformation("✓ Stock check passed");

        // 2. 计算总金额
        var totalAmount = request.Items.Sum(item => item.Subtotal);
        _logger.LogInformation("✓ Total amount: {Amount:C}", totalAmount);

        // 3. 保存订单（检查点 1）
        _orderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N[..8]}";
        var order = new Order
        {
            OrderId = _orderId,
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = totalAmount,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await _repository.SaveAsync(order, ct);
        _orderSaved = true;
        _logger.LogInformation("✓ Order saved: {OrderId}", _orderId);

        // 4. 预留库存（检查点 2）
        var reserveResult = await _inventory.ReserveAsync(_orderId, request.Items, ct);
        if (!reserveResult.IsSuccess)
        {
            throw new CatgaException("Failed to reserve inventory", reserveResult.Exception!);
        }
        _inventoryReserved = true;
        _logger.LogInformation("✓ Inventory reserved: {OrderId}", _orderId);

        // 5. 验证支付方式
        var paymentResult = await _payment.ValidateAsync(request.PaymentMethod, totalAmount, ct);
        if (!paymentResult.IsSuccess)
        {
            throw new CatgaException(
                $"Payment validation failed for method '{request.PaymentMethod}'",
                paymentResult.Exception);
        }
        _logger.LogInformation("✓ Payment validated");

        // 6. 发布成功事件
        await _mediator.PublishAsync(new OrderCreatedEvent(
            _orderId,
            request.CustomerId,
            request.Items,
            totalAmount,
            order.CreatedAt
        ), ct);

        _logger.LogInformation("✅ Order created successfully: {OrderId}, Amount: {Amount:C}",
            _orderId, totalAmount);

        return new OrderCreatedResult(_orderId, totalAmount, order.CreatedAt);
    }

    protected override async Task<CatgaResult<OrderCreatedResult>> OnBusinessErrorAsync(
        CreateOrderCommand request,
        CatgaException exception,
        CancellationToken ct)
    {
        _logger.LogWarning("⚠️ Order creation failed: {Error}. Initiating rollback...",
            exception.Message);

        var rollbackSteps = new List<string>();

        try
        {
            // 反向回滚
            if (_inventoryReserved && _orderId != null)
            {
                _logger.LogInformation("🔄 Rolling back inventory for {OrderId}...", _orderId);
                await _inventory.ReleaseAsync(_orderId, request.Items, ct);
                rollbackSteps.Add("Inventory released");
                _logger.LogInformation("✓ Inventory rollback completed");
            }

            if (_orderSaved && _orderId != null)
            {
                _logger.LogInformation("🔄 Rolling back order {OrderId}...", _orderId);
                await _repository.DeleteAsync(_orderId, ct);
                rollbackSteps.Add("Order deleted");
                _logger.LogInformation("✓ Order deletion completed");
            }

            // 发布失败事件
            if (_orderId != null)
            {
                await _mediator.PublishAsync(new OrderFailedEvent(
                    _orderId,
                    request.CustomerId,
                    exception.Message,
                    DateTime.UtcNow
                ), ct);
                rollbackSteps.Add("Failure event published");
            }

            _logger.LogInformation("✅ Rollback completed: {Steps}",
                string.Join(", ", rollbackSteps));
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx,
                "❌ CRITICAL: Rollback failed for order {OrderId}! Manual intervention required. " +
                "Completed steps: {CompletedSteps}",
                _orderId, string.Join(", ", rollbackSteps));
        }

        // 构建详细的错误响应
        var metadata = new ResultMetadata();
        metadata.Add("OrderId", _orderId ?? "N/A");
        metadata.Add("CustomerId", request.CustomerId);
        metadata.Add("TotalAmount", request.Items.Sum(i => i.Subtotal).ToString("C"));
        metadata.Add("RollbackSteps", string.Join(", ", rollbackSteps));
        metadata.Add("InventoryRolledBack", _inventoryReserved.ToString());
        metadata.Add("OrderDeleted", _orderSaved.ToString());
        metadata.Add("FailureTimestamp", DateTime.UtcNow.ToString("O"));

        return new CatgaResult<OrderCreatedResult>
        {
            IsSuccess = false,
            Error = $"Order creation failed: {exception.Message}. " +
                    $"Rollback completed: {string.Join(", ", rollbackSteps)}.",
            Exception = exception,
            Metadata = metadata
        };
    }

    protected override async Task<CatgaResult<OrderCreatedResult>> OnUnexpectedErrorAsync(
        CreateOrderCommand request,
        Exception exception,
        CancellationToken ct)
    {
        _logger.LogError(exception, "❌ Unexpected system error during order creation");

        // 对系统错误也执行回滚
        return await OnBusinessErrorAsync(
            request,
            new CatgaException("System error occurred", exception),
            ct);
    }
}
```

---

## 🎯 最佳实践

### 1. 状态跟踪

```csharp
// ✅ 好：使用字段跟踪操作状态
private string? _orderId;
private bool _orderSaved;
private bool _inventoryReserved;

// ❌ 差：没有状态跟踪，无法精确回滚
```

### 2. 反向回滚

```csharp
// ✅ 好：按执行的反向顺序回滚
// 执行：Save → Reserve → Validate
// 回滚：Release → Delete

// ❌ 差：回滚顺序与执行顺序相同
```

### 3. 回滚失败处理

```csharp
// ✅ 好：记录回滚失败，需要人工介入
try
{
    await RollbackAsync();
}
catch (Exception ex)
{
    Logger.LogError(ex, "CRITICAL: Rollback failed! Manual intervention required.");
    // 可以发送告警、创建工单等
}

// ❌ 差：忽略回滚失败
await RollbackAsync();  // 如果失败就静默失败了
```

### 4. 详细的元数据

```csharp
// ✅ 好：提供丰富的诊断信息
var metadata = new ResultMetadata();
metadata.Add("OrderId", _orderId);
metadata.Add("RollbackSteps", "Inventory released, Order deleted");
metadata.Add("FailureTimestamp", DateTime.UtcNow.ToString("O"));

// ❌ 差：只有错误消息，没有上下文
return CatgaResult.Failure("Failed");
```

### 5. 日志级别

```csharp
// ✅ 好：使用合适的日志级别
Logger.LogInformation("✓ Step completed");      // 正常流程
Logger.LogWarning("⚠️ Business error occurred");  // 预期的业务错误
Logger.LogError(ex, "❌ System error");          // 非预期的系统错误

// ❌ 差：所有都用 Error
Logger.LogError("Step completed");  // 过度记录
```

---

## 📊 实际效果

### 日志输出（成功）

```
info: 🚀 Starting order creation for customer CUST-001
info: ✓ Stock check passed
info: ✓ Total amount: $299.97
info: ✓ Order saved: ORD-20241016120000-a1b2c3d4
info: ✓ Inventory reserved: ORD-20241016120000-a1b2c3d4
info: ✓ Payment validated
info: ✅ Order created successfully: ORD-20241016120000-a1b2c3d4, Amount: $299.97
```

### 日志输出（失败 + 回滚）

```
info: 🚀 Starting order creation for customer CUST-002
info: ✓ Stock check passed
info: ✓ Total amount: $17,648.00
info: ✓ Order saved: ORD-20241016120001-e5f6g7h8
info: ✓ Inventory reserved: ORD-20241016120001-e5f6g7h8
warn: ⚠️ Order creation failed: Payment validation failed for method 'FAIL-CreditCard'. Initiating rollback...
info: 🔄 Rolling back inventory for ORD-20241016120001-e5f6g7h8...
info: ✓ Inventory rollback completed
info: 🔄 Rolling back order ORD-20241016120001-e5f6g7h8...
info: ✓ Order deletion completed
info: ✅ Rollback completed: Inventory released, Order deleted, Failure event published
```

---

## 🔗 相关资源

- [SafeRequestHandler 指南](./custom-error-handling.md)
- [错误处理基础](./error-handling.md)
- [OrderSystem 完整示例](../../examples/OrderSystem.Api/Handlers/OrderCommandHandlers.cs)
- [错误处理与 CatgaResult](../guides/error-handling.md)

---

**通过自定义错误处理，你可以实现生产级的事务回滚和错误恢复！** 🎉
