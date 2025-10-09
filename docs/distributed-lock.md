# 分布式锁 (Distributed Lock)

Catga 提供了分布式锁抽象和多种实现，用于在分布式环境中协调对共享资源的访问。

---

## 📦 安装

```bash
# 核心包（包含内存实现）
dotnet add package Catga

# Redis 实现
dotnet add package Catga.Persistence.Redis
```

---

## 🚀 快速开始

### 使用内存锁

```csharp
using Catga.DistributedLock;

// 注册服务
builder.Services.AddMemoryDistributedLock();

// 使用锁
public class PaymentHandler : IRequestHandler<ProcessPaymentCommand, PaymentResponse>
{
    private readonly IDistributedLock _lock;

    public PaymentHandler(IDistributedLock lock)
    {
        _lock = lock;
    }

    public async ValueTask<PaymentResponse> Handle(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken)
    {
        // 获取锁以防止重复支付
        await using var lockHandle = await _lock.TryAcquireAsync(
            $"payment:{request.OrderId}",
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (lockHandle == null)
        {
            throw new InvalidOperationException("Payment is already processing");
        }

        // 处理支付（受锁保护）
        var result = await ProcessPaymentAsync(request);

        return result;
    }
}
```

### 使用 Redis 锁

```csharp
using Catga.Persistence.Redis;
using StackExchange.Redis;

// 注册服务
var redis = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddRedisDistributedLock();

// 使用方式相同
```

---

## 📖 接口定义

### IDistributedLock

```csharp
public interface IDistributedLock
{
    ValueTask<ILockHandle?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
```

### ILockHandle

```csharp
public interface ILockHandle : IDisposable, IAsyncDisposable
{
    string Key { get; }
    string LockId { get; }
    DateTime AcquiredAt { get; }
    bool IsHeld { get; }
}
```

---

## 💡 使用场景

### 1. 防止重复处理

```csharp
await using var lock = await _lock.TryAcquireAsync(
    $"order:{orderId}",
    TimeSpan.FromSeconds(30));

if (lock == null)
{
    return Result.Failure("Order is being processed");
}

await ProcessOrderAsync(orderId);
```

### 2. 限制并发访问

```csharp
await using var lock = await _lock.TryAcquireAsync(
    "critical-section",
    TimeSpan.FromMinutes(5));

if (lock == null)
{
    return Result.Failure("Resource is busy");
}

await AccessCriticalResourceAsync();
```

### 3. 分布式任务调度

```csharp
await using var lock = await _lock.TryAcquireAsync(
    $"scheduled-task:{taskId}",
    TimeSpan.FromMinutes(10));

if (lock != null)
{
    await ExecuteScheduledTaskAsync(taskId);
}
```

---

## 🎯 最佳实践

### 1. 使用 using 语句自动释放锁

```csharp
// ✅ 推荐：自动释放
await using var lock = await _lock.TryAcquireAsync(key, timeout);
if (lock != null)
{
    // 处理逻辑
}

// ❌ 避免：手动释放容易遗漏
var lock = await _lock.TryAcquireAsync(key, timeout);
try
{
    // 处理逻辑
}
finally
{
    await lock?.DisposeAsync();
}
```

### 2. 设置合理的超时时间

```csharp
// ✅ 推荐：根据业务设置超时
var timeout = TimeSpan.FromSeconds(30); // 支付处理
var lock = await _lock.TryAcquireAsync(key, timeout);

// ❌ 避免：超时过长或过短
var lock = await _lock.TryAcquireAsync(key, TimeSpan.FromHours(1)); // 太长
var lock = await _lock.TryAcquireAsync(key, TimeSpan.FromMilliseconds(10)); // 太短
```

### 3. 检查锁获取结果

```csharp
// ✅ 推荐：检查是否成功获取锁
var lock = await _lock.TryAcquireAsync(key, timeout);
if (lock == null)
{
    // 处理锁获取失败的情况
    return Result.Failure("Unable to acquire lock");
}

// ❌ 避免：假设锁总是能获取成功
var lock = await _lock.TryAcquireAsync(key, timeout);
await ProcessAsync(); // 可能 NullReferenceException
```

### 4. 使用有意义的锁键

```csharp
// ✅ 推荐：清晰的命名
await _lock.TryAcquireAsync($"payment:{orderId}", timeout);
await _lock.TryAcquireAsync($"user:profile:{userId}", timeout);

// ❌ 避免：模糊的命名
await _lock.TryAcquireAsync("lock1", timeout);
await _lock.TryAcquireAsync(orderId.ToString(), timeout);
```

---

## ⚙️ 实现对比

| 特性 | 内存锁 | Redis 锁 |
|------|--------|----------|
| **适用场景** | 单实例 | 分布式 |
| **性能** | 极快 | 快 |
| **可靠性** | 高 | 极高 |
| **持久化** | 否 | 是 |
| **跨进程** | 否 | 是 |
| **自动过期** | 否 | 是 |

---

## 🔧 高级用法

### 自定义锁键前缀

```csharp
public class LockKeyGenerator
{
    public static string ForPayment(long orderId) =>
        $"payment:{orderId}";

    public static string ForUser(long userId) =>
        $"user:{userId}";

    public static string ForResource(string resourceType, string resourceId) =>
        $"{resourceType}:{resourceId}";
}

// 使用
await _lock.TryAcquireAsync(
    LockKeyGenerator.ForPayment(orderId),
    timeout);
```

### 带重试的锁获取

```csharp
public async ValueTask<ILockHandle?> TryAcquireWithRetryAsync(
    string key,
    TimeSpan timeout,
    int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        var handle = await _lock.TryAcquireAsync(key, timeout);
        if (handle != null)
        {
            return handle;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100 * (i + 1)));
    }

    return null;
}
```

---

## 🐛 故障排查

### 锁无法获取

**问题**：`TryAcquireAsync` 总是返回 `null`

**可能原因**：
1. 锁已被其他进程持有
2. 超时时间过短
3. Redis 连接问题

**解决方案**：
- 增加超时时间
- 检查 Redis 连接
- 查看锁持有者日志

### 锁未正确释放

**问题**：锁一直被占用

**可能原因**：
1. 未使用 `using` 语句
2. 异常导致未释放
3. 进程崩溃

**解决方案**：
- 始终使用 `await using`
- Redis 锁会自动过期
- 设置合理的超时时间

---

## 📚 相关文档

- [Saga 模式](saga-pattern.md)
- [健康检查](health-check.md)
- [分布式系统最佳实践](distributed-systems.md)

---

## 🎯 性能特征

- **零 GC 压力** - 最小化内存分配
- **高并发** - 使用原子操作
- **低延迟** - 内存锁 < 1µs，Redis 锁 < 5ms
- **可靠性** - Redis 使用 Lua 脚本保证原子性

---

**需要帮助？** 查看 [Catga 文档](../README.md) 或提交 issue。

