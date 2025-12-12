# OrderSystem.Api 优化机会分析

## 概述

本文档分析了 OrderSystem.Api 中的优化机会，包括代码质量改进、性能优化和架构增强。

---

## 1. Handler 中的代码重复

### 问题：CreateOrderCommand 和 CreateOrderFlowCommand 的重复代码

**当前代码（重复）**：
```csharp
// CreateOrderCommand 处理器 (行 33-50)
var order = new Order
{
    OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
    CustomerId = request.CustomerId,
    Items = request.Items,
    TotalAmount = request.Items.Sum(i => i.Subtotal),
    Status = OrderStatus.Confirmed,
    CreatedAt = DateTime.UtcNow
};

// CreateOrderFlowCommand 处理器 (行 54-62)
var order = new Order
{
    OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
    CustomerId = request.CustomerId,
    Items = request.Items,
    TotalAmount = request.Items.Sum(i => i.Subtotal),
    Status = OrderStatus.Pending,  // 只有这里不同
    CreatedAt = DateTime.UtcNow
};
```

### 优化方案：提取公共方法

```csharp
private Order CreateOrder(string customerId, List<OrderItem> items, OrderStatus status = OrderStatus.Confirmed)
{
    return new Order
    {
        OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
        CustomerId = customerId,
        Items = items,
        TotalAmount = items.Sum(i => i.Subtotal),
        Status = status,
        CreatedAt = DateTime.UtcNow
    };
}

// 使用
public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
{
    var order = CreateOrder(request.CustomerId, request.Items);
    // ... 其余代码
}

public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderFlowCommand request, CancellationToken ct = default)
{
    var order = CreateOrder(request.CustomerId, request.Items, OrderStatus.Pending);
    // ... 其余代码
}
```

**代码减少**：8 行

---

## 2. Repository 中的锁定模式重复

### 问题：InMemoryOrderRepository 中的重复锁定模式

**当前代码（重复）**：
```csharp
public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default)
{
    lock (_lock)
    {
        _orders.TryGetValue(orderId, out var order);
        return ValueTask.FromResult(order);
    }
}

public ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
{
    lock (_lock)
    {
        var orders = _orders.Values.Where(o => o.CustomerId == customerId).ToList();
        return ValueTask.FromResult(orders);
    }
}

public ValueTask SaveAsync(Order order, CancellationToken ct = default)
{
    lock (_lock)
    {
        _orders[order.OrderId] = order;
        return ValueTask.CompletedTask;
    }
}

public ValueTask UpdateAsync(Order order, CancellationToken ct = default)
{
    lock (_lock)
    {
        _orders[order.OrderId] = order;
        return ValueTask.CompletedTask;
    }
}
```

### 优化方案：提取通用的锁定方法

```csharp
private T WithLock<T>(Func<T> action)
{
    lock (_lock)
    {
        return action();
    }
}

private void WithLock(Action action)
{
    lock (_lock)
    {
        action();
    }
}

// 使用
public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default)
    => ValueTask.FromResult(WithLock(() =>
    {
        _orders.TryGetValue(orderId, out var order);
        return order;
    }));

public ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
    => ValueTask.FromResult(WithLock(() =>
        _orders.Values.Where(o => o.CustomerId == customerId).ToList()
    ));

public ValueTask SaveAsync(Order order, CancellationToken ct = default)
{
    WithLock(() => _orders[order.OrderId] = order);
    return ValueTask.CompletedTask;
}

public ValueTask UpdateAsync(Order order, CancellationToken ct = default)
{
    WithLock(() => _orders[order.OrderId] = order);
    return ValueTask.CompletedTask;
}
```

**代码减少**：12 行

---

## 3. Pipeline Behaviors 中的重复代码

### 问题：ValidationBehavior 和 LoggingBehavior 的相似结构

**当前代码**：
```csharp
public class ValidationBehavior<TRequest, TResponse>(
    ILogger<ValidationBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogDebug("Validating {Request}", requestName);

        if (request is null) return CatgaResult<TResponse>.Failure("Request cannot be null");

        var result = await next();
        if (!result.IsSuccess) logger.LogWarning("{Request} failed: {Error}", requestName, result.Error);

        return result;
    }
}

public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
    {
        var requestName = typeof(TRequest).Name;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Handling {Request}", requestName);

        var result = await next();

        sw.Stop();
        logger.LogInformation("{Request} completed in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
        return result;
    }
}
```

### 优化方案：创建基础 Pipeline Behavior 类

```csharp
public abstract class BasePipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    protected ILogger Logger { get; }
    protected string RequestName { get; }

    protected BasePipelineBehavior(ILogger logger)
    {
        Logger = logger;
        RequestName = typeof(TRequest).Name;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
    {
        OnBeforeHandle(request);
        var result = await next();
        OnAfterHandle(request, result);
        return result;
    }

    protected abstract void OnBeforeHandle(TRequest request);
    protected abstract void OnAfterHandle(TRequest request, CatgaResult<TResponse> result);
}

// 具体实现
public class ValidationBehavior<TRequest, TResponse>(
    ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : BasePipelineBehavior<TRequest, TResponse>(logger)
    where TRequest : IRequest<TResponse>
{
    protected override void OnBeforeHandle(TRequest request)
    {
        Logger.LogDebug("Validating {Request}", RequestName);
        if (request is null) throw new ArgumentNullException(nameof(request));
    }

    protected override void OnAfterHandle(TRequest request, CatgaResult<TResponse> result)
    {
        if (!result.IsSuccess) Logger.LogWarning("{Request} failed: {Error}", RequestName, result.Error);
    }
}

public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : BasePipelineBehavior<TRequest, TResponse>(logger)
    where TRequest : IRequest<TResponse>
{
    private System.Diagnostics.Stopwatch? _sw;

    protected override void OnBeforeHandle(TRequest request)
    {
        _sw = System.Diagnostics.Stopwatch.StartNew();
        Logger.LogInformation("Handling {Request}", RequestName);
    }

    protected override void OnAfterHandle(TRequest request, CatgaResult<TResponse> result)
    {
        _sw?.Stop();
        Logger.LogInformation("{Request} completed in {ElapsedMs}ms", RequestName, _sw?.ElapsedMilliseconds ?? 0);
    }
}
```

**代码减少**：15 行

---

## 4. 优化优先级

| 优先级 | 优化项 | 代码减少 | 难度 | 状态 |
|-------|-------|--------|------|------|
| 1 | Handler 中的 CreateOrder 提取 | 8 行 | 低 | ⏳ 推荐 |
| 2 | Repository 中的 WithLock 提取 | 12 行 | 低 | ⏳ 推荐 |
| 3 | Pipeline Behaviors 基类 | 15 行 | 中 | ⏳ 可选 |

**总体可减少代码量**：35+ 行

---

## 5. 快速修复清单

### 立即可做（优先级 1-2）

- [ ] 在 OrderHandler 中提取 CreateOrder 方法
- [ ] 在 InMemoryOrderRepository 中提取 WithLock 方法
- [ ] 验证编译成功

### 可选优化（优先级 3）

- [ ] 创建 BasePipelineBehavior 基类
- [ ] 重构 ValidationBehavior 和 LoggingBehavior

---

## 总结

通过应用这些优化方案，可以：
- 减少 35+ 行重复代码
- 提高代码可维护性
- 遵循 DRY 原则
- 使代码更加一致和易读

**预计实施时间**：15-20 分钟
**预计代码减少**：35+ 行
**难度等级**：低到中
