# Event Sourcing 指南

Event Sourcing（事件溯源）是一种将应用程序状态存储为事件序列的架构模式。Catga 提供了完整的 Event Sourcing 支持。

---

## 📚 目录

- [核心概念](#核心概念)
- [快速开始](#快速开始)
- [AggregateRoot](#aggregateroot)
- [Event Store](#event-store)
- [使用示例](#使用示例)
- [最佳实践](#最佳实践)
- [性能优化](#性能优化)

---

## 核心概念

### Event Sourcing 是什么？

传统应用存储当前状态，Event Sourcing 存储导致状态改变的事件序列。

**传统方式**:
```
User: { Id: 1, Name: "Alice", Email: "alice@example.com", Balance: 100 }
```

**Event Sourcing**:
```
UserCreated: { Id: 1, Name: "Alice", Email: "alice@example.com" }
MoneyDeposited: { Amount: 150 }
MoneyWithdrawn: { Amount: 50 }
=> 当前余额: 100
```

### 优势

- ✅ **完整历史** - 所有状态变化都被记录
- ✅ **审计追踪** - 自然的审计日志
- ✅ **时间旅行** - 可以重放到任意时间点
- ✅ **调试友好** - 重现任何 bug
- ✅ **事件驱动** - 天然支持事件发布

---

## 快速开始

### 1. 安装依赖

```bash
dotnet add package Catga
```

### 2. 注册服务

```csharp
using Catga.EventSourcing;

var builder = WebApplication.CreateBuilder(args);

// 使用内存 Event Store (测试/单实例)
builder.Services.AddMemoryEventStore();

// 生产环境建议使用 Redis/SQL
// builder.Services.AddRedisEventStore("localhost:6379");

var app = builder.Build();
app.Run();
```

### 3. 定义事件

```csharp
using Catga.Messages;

// 账户创建事件
public record AccountCreated(
    string AccountId,
    string Owner,
    decimal InitialBalance) : IEvent;

// 存款事件
public record MoneyDeposited(
    string AccountId,
    decimal Amount,
    string Description) : IEvent;

// 取款事件
public record MoneyWithdrawn(
    string AccountId,
    decimal Amount,
    string Description) : IEvent;
```

### 4. 创建 Aggregate

```csharp
using Catga.EventSourcing;

public class BankAccount : AggregateRoot
{
    public string Owner { get; private set; } = string.Empty;
    public decimal Balance { get; private set; }
    public bool IsClosed { get; private set; }

    // 创建账户
    public void Create(string accountId, string owner, decimal initialBalance)
    {
        if (initialBalance < 0)
            throw new InvalidOperationException("Initial balance cannot be negative");

        RaiseEvent(new AccountCreated(accountId, owner, initialBalance));
    }

    // 存款
    public void Deposit(decimal amount, string description)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be positive");

        if (IsClosed)
            throw new InvalidOperationException("Account is closed");

        RaiseEvent(new MoneyDeposited(Id, amount, description));
    }

    // 取款
    public void Withdraw(decimal amount, string description)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be positive");

        if (IsClosed)
            throw new InvalidOperationException("Account is closed");

        if (Balance < amount)
            throw new InvalidOperationException("Insufficient balance");

        RaiseEvent(new MoneyWithdrawn(Id, amount, description));
    }

    // 应用事件更新状态
    protected override void Apply(IEvent @event)
    {
        switch (@event)
        {
            case AccountCreated created:
                Id = created.AccountId;
                Owner = created.Owner;
                Balance = created.InitialBalance;
                break;

            case MoneyDeposited deposited:
                Balance += deposited.Amount;
                break;

            case MoneyWithdrawn withdrawn:
                Balance -= withdrawn.Amount;
                break;
        }
    }
}
```

### 5. 使用 Aggregate

```csharp
public class BankAccountService
{
    private readonly IEventStore _eventStore;

    public BankAccountService(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    // 创建新账户
    public async Task<string> CreateAccountAsync(string owner, decimal initialBalance)
    {
        var account = new BankAccount();
        var accountId = Guid.NewGuid().ToString();
        
        account.Create(accountId, owner, initialBalance);

        // 保存事件
        await _eventStore.AppendAsync(
            accountId,
            account.UncommittedEvents.ToArray());

        account.MarkEventsAsCommitted();

        return accountId;
    }

    // 存款
    public async Task DepositAsync(string accountId, decimal amount, string description)
    {
        // 加载 Aggregate
        var account = await LoadAccountAsync(accountId);
        
        // 执行业务逻辑
        account.Deposit(amount, description);

        // 保存事件 (乐观并发控制)
        await _eventStore.AppendAsync(
            accountId,
            account.UncommittedEvents.ToArray(),
            expectedVersion: account.Version);

        account.MarkEventsAsCommitted();
    }

    // 取款
    public async Task WithdrawAsync(string accountId, decimal amount, string description)
    {
        var account = await LoadAccountAsync(accountId);
        account.Withdraw(amount, description);

        await _eventStore.AppendAsync(
            accountId,
            account.UncommittedEvents.ToArray(),
            expectedVersion: account.Version);

        account.MarkEventsAsCommitted();
    }

    // 查询余额
    public async Task<decimal> GetBalanceAsync(string accountId)
    {
        var account = await LoadAccountAsync(accountId);
        return account.Balance;
    }

    // 从事件流加载 Aggregate
    private async Task<BankAccount> LoadAccountAsync(string accountId)
    {
        var stream = await _eventStore.ReadAsync(accountId);
        
        var account = new BankAccount();
        account.LoadFromHistory(stream.Events);
        
        return account;
    }
}
```

---

## AggregateRoot

### 基类方法

```csharp
public abstract class AggregateRoot
{
    // 唯一标识
    public string Id { get; protected set; }
    
    // 当前版本
    public long Version { get; protected set; }
    
    // 未提交的事件
    public IReadOnlyList<IEvent> UncommittedEvents { get; }
    
    // 触发新事件
    protected void RaiseEvent(IEvent @event);
    
    // 应用事件 (必须实现)
    protected abstract void Apply(IEvent @event);
    
    // 从历史加载
    public void LoadFromHistory(IEnumerable<StoredEvent> events);
    
    // 标记事件为已提交
    public void MarkEventsAsCommitted();
}
```

### 实现要点

1. **Apply 必须是幂等的**
   ```csharp
   protected override void Apply(IEvent @event)
   {
       // ✅ 正确 - 设置状态
       Balance += amount;
       
       // ❌ 错误 - 副作用
       _logger.LogInformation("Balance updated");
   }
   ```

2. **业务逻辑在 Command 方法中**
   ```csharp
   public void Withdraw(decimal amount)
   {
       // ✅ 验证在这里
       if (Balance < amount)
           throw new InvalidOperationException();
       
       RaiseEvent(new MoneyWithdrawn(amount));
   }
   ```

3. **所有状态变化通过事件**
   ```csharp
   public void Close()
   {
       // ❌ 错误 - 直接修改
       // IsClosed = true;
       
       // ✅ 正确 - 通过事件
       RaiseEvent(new AccountClosed(Id));
   }
   ```

---

## Event Store

### IEventStore 接口

```csharp
public interface IEventStore
{
    // 追加事件
    ValueTask AppendAsync(
        string streamId,
        IEvent[] events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default);

    // 读取事件流
    ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default);

    // 获取当前版本
    ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default);
}
```

### 并发控制

```csharp
try
{
    // 乐观并发控制
    await _eventStore.AppendAsync(
        streamId,
        events,
        expectedVersion: currentVersion);
}
catch (ConcurrencyException ex)
{
    // 版本冲突 - 需要重试
    _logger.LogWarning(
        "Concurrency conflict: expected {Expected}, actual {Actual}",
        ex.ExpectedVersion,
        ex.ActualVersion);
    
    // 重新加载并重试
    await RetryOperationAsync(streamId);
}
```

---

## 使用示例

### 示例 1: 订单管理

```csharp
// 事件定义
public record OrderCreated(string OrderId, string CustomerId) : IEvent;
public record ItemAdded(string ProductId, int Quantity, decimal Price) : IEvent;
public record OrderSubmitted(DateTime SubmittedAt) : IEvent;
public record OrderCancelled(string Reason) : IEvent;

// Aggregate
public class Order : AggregateRoot
{
    public string CustomerId { get; private set; } = string.Empty;
    public List<OrderLine> Lines { get; } = new();
    public OrderStatus Status { get; private set; }
    public decimal Total => Lines.Sum(l => l.Total);

    public void Create(string orderId, string customerId)
    {
        RaiseEvent(new OrderCreated(orderId, customerId));
    }

    public void AddItem(string productId, int quantity, decimal price)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Cannot modify submitted order");

        RaiseEvent(new ItemAdded(productId, quantity, price));
    }

    public void Submit()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Order already submitted");

        if (Lines.Count == 0)
            throw new InvalidOperationException("Cannot submit empty order");

        RaiseEvent(new OrderSubmitted(DateTime.UtcNow));
    }

    protected override void Apply(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreated created:
                Id = created.OrderId;
                CustomerId = created.CustomerId;
                Status = OrderStatus.Draft;
                break;

            case ItemAdded added:
                Lines.Add(new OrderLine(added.ProductId, added.Quantity, added.Price));
                break;

            case OrderSubmitted submitted:
                Status = OrderStatus.Submitted;
                break;

            case OrderCancelled cancelled:
                Status = OrderStatus.Cancelled;
                break;
        }
    }
}
```

### 示例 2: API 端点

```csharp
[ApiController]
[Route("api/[controller]")]
public class BankAccountController : ControllerBase
{
    private readonly IEventStore _eventStore;

    public BankAccountController(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAccount(CreateAccountRequest request)
    {
        var account = new BankAccount();
        var accountId = Guid.NewGuid().ToString();
        
        account.Create(accountId, request.Owner, request.InitialBalance);

        await _eventStore.AppendAsync(accountId, account.UncommittedEvents.ToArray());
        account.MarkEventsAsCommitted();

        return Ok(new { AccountId = accountId });
    }

    [HttpPost("{accountId}/deposit")]
    public async Task<IActionResult> Deposit(string accountId, DepositRequest request)
    {
        var account = await LoadAccountAsync(accountId);
        account.Deposit(request.Amount, request.Description);

        await _eventStore.AppendAsync(
            accountId,
            account.UncommittedEvents.ToArray(),
            account.Version);

        account.MarkEventsAsCommitted();

        return Ok(new { Balance = account.Balance });
    }

    [HttpGet("{accountId}/balance")]
    public async Task<IActionResult> GetBalance(string accountId)
    {
        var account = await LoadAccountAsync(accountId);
        return Ok(new { Balance = account.Balance });
    }

    private async Task<BankAccount> LoadAccountAsync(string accountId)
    {
        var stream = await _eventStore.ReadAsync(accountId);
        var account = new BankAccount();
        account.LoadFromHistory(stream.Events);
        return account;
    }
}
```

---

## 最佳实践

### 1. 事件命名

✅ **好的命名** - 过去式动词
```csharp
AccountCreated
MoneyDeposited
OrderSubmitted
```

❌ **不好的命名**
```csharp
CreateAccount
DepositMoney
SubmitOrder
```

### 2. 事件设计

✅ **小而专注**
```csharp
public record AddressChanged(string Street, string City, string ZipCode) : IEvent;
```

❌ **过大的事件**
```csharp
public record UserUpdated(string Name, string Email, Address Address, ...) : IEvent;
```

### 3. Aggregate 边界

- 一个 Aggregate = 一个事件流
- 保持 Aggregate 小而聚焦
- 避免跨 Aggregate 事务

### 4. 快照优化

对于长事件流，使用快照加速加载：

```csharp
public class AccountSnapshot
{
    public string Id { get; set; }
    public string Owner { get; set; }
    public decimal Balance { get; set; }
    public long Version { get; set; }
}

// 每 100 个事件创建快照
if (account.Version % 100 == 0)
{
    await SaveSnapshotAsync(account);
}

// 加载时从最近的快照开始
var snapshot = await LoadSnapshotAsync(accountId);
var events = await _eventStore.ReadAsync(accountId, snapshot.Version + 1);
```

---

## 性能优化

### 1. 批量读取

```csharp
// ❌ 低效 - 一次一个
foreach (var id in accountIds)
{
    var stream = await _eventStore.ReadAsync(id);
}

// ✅ 高效 - 批量读取
var streams = await _eventStore.ReadBatchAsync(accountIds);
```

### 2. 投影/物化视图

```csharp
// 创建读模型
public class AccountBalanceProjection
{
    private readonly Dictionary<string, decimal> _balances = new();

    public void Apply(IEvent @event)
    {
        switch (@event)
        {
            case AccountCreated created:
                _balances[created.AccountId] = created.InitialBalance;
                break;

            case MoneyDeposited deposited:
                _balances[deposited.AccountId] += deposited.Amount;
                break;

            case MoneyWithdrawn withdrawn:
                _balances[withdrawn.AccountId] -= withdrawn.Amount;
                break;
        }
    }

    public decimal GetBalance(string accountId)
    {
        return _balances.GetValueOrDefault(accountId);
    }
}
```

### 3. 事件版本化

```csharp
// V1
public record MoneyDeposited(string AccountId, decimal Amount) : IEvent;

// V2 - 添加字段
public record MoneyDepositedV2(
    string AccountId,
    decimal Amount,
    string Currency) : IEvent;

// 处理升级
protected override void Apply(IEvent @event)
{
    switch (@event)
    {
        case MoneyDepositedV2 v2:
            Balance += ConvertCurrency(v2.Amount, v2.Currency);
            break;

        case MoneyDeposited v1:
            Balance += v1.Amount; // 默认 USD
            break;
    }
}
```

---

## 故障排查

### 问题 1: ConcurrencyException

**原因**: 多个操作同时修改同一 Aggregate

**解决**:
```csharp
public async Task<T> RetryOnConcurrencyAsync<T>(
    Func<Task<T>> operation,
    int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await operation();
        }
        catch (ConcurrencyException) when (i < maxRetries - 1)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100 * (i + 1)));
        }
    }
    throw new InvalidOperationException("Max retries exceeded");
}
```

### 问题 2: 事件流过长

**解决**: 实现快照机制

### 问题 3: 查询性能差

**解决**: 创建读模型/投影

---

## 总结

Event Sourcing 是一个强大的架构模式，适合：

✅ **适用场景**:
- 需要完整审计日志
- 需要时间旅行功能
- 复杂业务逻辑
- 事件驱动架构

❌ **不适用场景**:
- 简单 CRUD
- 只需要当前状态
- 极高查询性能要求 (除非有读模型)

Catga 的 Event Sourcing 实现提供了：
- ✅ 简单易用的 API
- ✅ 乐观并发控制
- ✅ 完整的 Aggregate 支持
- ✅ 可扩展的存储后端

---

**相关文档**:
- [分布式锁](./distributed-lock.md)
- [Saga 模式](./saga-pattern.md)
- [分布式缓存](./distributed-cache.md)

