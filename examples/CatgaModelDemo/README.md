# Catga 模型示例

这个示例展示了 **Catga 模型**的核心优势：用户只需实现 2 个方法，框架自动处理其余一切。

## 🌟 Catga 模型特点

### 引导式聚合根

用户只需实现 **2 个方法**:

```csharp
public class Order : AggregateRoot<string, OrderState>
{
    // 1. 从事件中提取 ID
    protected override string GetId(IEvent @event) => @event switch
    {
        OrderCreated e => e.OrderId,
        _ => Id!
    };

    // 2. 应用事件到状态 (纯函数，不可变)
    protected override OrderState Apply(OrderState state, IEvent @event) => @event switch
    {
        OrderCreated e => state with { Items = e.Items, Status = OrderStatus.Created },
        OrderPaid e => state with { Status = OrderStatus.Paid },
        OrderShipped e => state with { Status = OrderStatus.Shipped },
        _ => state
    };
}
```

**框架自动处理**:
- ✅ 事件管理和跟踪
- ✅ 版本控制
- ✅ 持久化到 Event Store
- ✅ 并发控制
- ✅ 分布式追踪
- ✅ 结构化日志

## 🚀 运行示例

```bash
cd examples/CatgaModelDemo
dotnet run
```

## 📊 代码对比

| 特性 | 传统 Event Sourcing | Catga 模型 |
|------|-------------------|-----------|
| **代码量** | ~200 行 | ~50 行 |
| **用户实现** | 全部手动 | 2 个方法 |
| **事件管理** | 手动跟踪 | 自动 ✅ |
| **版本控制** | 手动实现 | 自动 ✅ |
| **持久化** | 手动实现 | 自动 ✅ |
| **追踪日志** | 手动埋点 | 自动 ✅ |
| **性能** | 运行时反射 | 编译时生成 ✅ |
| **AOT** | ❌ 不兼容 | ✅ 100% 兼容 |

**代码减少**: **75%** 🎯

## 🎯 示例场景

1. **创建订单** - 演示聚合根创建和事件发布
2. **从 Event Store 加载** - 演示事件重放和状态恢复
3. **支付订单** - 演示状态变更和并发控制
4. **发货** - 演示多步骤业务流程
5. **查看事件历史** - 演示完整的事件溯源

## 💡 核心概念

### 1. 不可变状态

```csharp
public record OrderState
{
    public OrderStatus Status { get; init; } = OrderStatus.Created;
    public List<string> Items { get; init; } = new();
    public decimal TotalAmount { get; init; }
}
```

### 2. 纯函数 Apply

```csharp
protected override OrderState Apply(OrderState state, IEvent @event) => @event switch
{
    OrderCreated e => state with { Items = e.Items },
    _ => state
};
```

### 3. 业务方法

```csharp
public void Pay(decimal amount)
{
    if (State.Status != OrderStatus.Created)
        throw new InvalidOperationException("Invalid status");

    RaiseEvent(new OrderPaid(Id!, amount, DateTime.UtcNow));
}
```

## 📝 输出示例

```
========================================
Catga 模型示例 - Event Sourcing
========================================

场景 1: 创建订单
----------------------------------------
订单已创建: ORDER-001
状态: Created, 商品数: 3, 总额: ¥300.00
未提交事件数: 1
✅ 订单已保存到 Event Store, Version: 0

场景 2: 从 Event Store 加载订单
----------------------------------------
✅ 订单已加载: ORDER-001, Version: 0
状态: Created, 商品数: 3

场景 3: 支付订单
----------------------------------------
订单已支付, 新状态: Paid
未提交事件数: 1
✅ 订单更新已保存, Version: 1

场景 4: 发货
----------------------------------------
订单已发货, 追踪号: TRACK-12345
✅ 发货信息已保存, Version: 2

场景 5: 查看完整事件历史
----------------------------------------
订单最终状态:
  ID: ORDER-001
  Version: 2
  Status: Shipped
  Items: 3
  Amount: ¥300.00
  Tracking: TRACK-12345

========================================
✅ Catga 模型示例完成！
========================================
```

## 🏆 Catga 模型优势

1. **极低学习成本** - 只需实现 2 个方法
2. **极致性能** - 零反射，编译时生成
3. **完整可观测性** - 自动追踪和日志
4. **类型安全** - 编译时检查
5. **AOT 兼容** - 100% Native AOT

**传统模式**: 需要理解复杂的 Event Sourcing 概念
**Catga 模型**: 写普通代码即可 ✅

---

**就这么简单！框架自动处理其余一切。** 🎉

