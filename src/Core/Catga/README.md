# Catga

**简单、高性能、AOT 兼容的 CQRS 和分布式事务框架**

## ✨ 核心特性

- ✅ **100% AOT 兼容** - 零反射，完全 NativeAOT 支持
- ✅ **无锁设计** - 原子操作 + ConcurrentDictionary
- ✅ **非阻塞异步** - 全异步，零阻塞
- ✅ **极简 API** - 最少配置，合理默认值
- ✅ **高性能** - 分片存储、并发控制、限流
- ✅ **可观测性** - 分布式追踪、日志、指标
- ✅ **弹性设计** - 熔断器、重试、死信队列
- ✅ **双传输** - 内存 / NATS

## 🚀 快速开始

### 1. 定义消息

```csharp
// 查询
public record GetUserQuery(long UserId) : IQuery<User>;

// 命令
public record CreateUserCommand(string Name) : ICommand<long>;

// 事件
public record UserCreatedEvent(long UserId) : IEvent;
```

### 2. 定义处理器

```csharp
public class GetUserHandler : IRequestHandler<GetUserQuery, User>
{
    public async Task<CatgaResult<User>> HandleAsync(
        GetUserQuery request,
        CancellationToken ct)
    {
        var user = await _db.GetUserAsync(request.UserId);
        return CatgaResult<User>.Success(user);
    }
}
```

### 3. 注册服务

```csharp
// 默认配置（推荐）
services.AddCatga();

// 自定义
services.AddCatga(options =>
{
    options.MaxConcurrentRequests = 2000;
    options.EnableCircuitBreaker = true;
});

// 或使用预设
services.AddCatga(opt => opt.WithHighPerformance());
services.AddCatga(opt => opt.WithResilience());
services.AddCatga(opt => opt.ForDevelopment());

// 注册处理器
services.AddRequestHandler<GetUserQuery, User, GetUserHandler>();
services.AddEventHandler<UserCreatedEvent, SendEmailHandler>();
```

### 4. 使用

```csharp
public class UserService(ICatgaMediator mediator)
{
    public async Task<User> GetUserAsync(long id)
    {
        var result = await mediator.SendAsync<GetUserQuery, User>(
            new GetUserQuery(id));
        return result.Value;
    }
}
```

## 📊 功能特性

### 🔄 Pipeline Behaviors（自动启用）

- **Logging** - 结构化日志记录
- **Tracing** - 分布式追踪（ActivitySource）
- **Idempotency** - 消息去重（分片存储）
- **Validation** - 请求验证
- **Retry** - 指数退避重试

### 🛡️ 弹性机制

```csharp
services.AddCatga(options =>
{
    // 重试
    options.EnableRetry = true;
    options.MaxRetryAttempts = 3;
    options.RetryDelayMs = 100; // 指数退避

    // 熔断器
    options.EnableCircuitBreaker = true;
    options.CircuitBreakerFailureThreshold = 10;

    // 限流
    options.EnableRateLimiting = true;
    options.RateLimitRequestsPerSecond = 1000;

    // 死信队列
    options.EnableDeadLetterQueue = true;
});
```

### ⚡ 性能优化

```csharp
// 无锁并发控制
options.MaxConcurrentRequests = 5000;

// 分片幂等存储（减少锁竞争）
options.IdempotencyShardCount = 64;
options.IdempotencyRetentionHours = 24;
```

### 📈 可观测性

**分布式追踪**（自动）
```csharp
options.EnableTracing = true; // ActivitySource
```

**死信队列检查**
```csharp
public class AdminController(IDeadLetterQueue dlq)
{
    public async Task<List<DeadLetterMessage>> GetFailedMessages()
    {
        return await dlq.GetFailedMessagesAsync(maxCount: 100);
    }
}
```

## 🎯 配置预设

```csharp
// 开发环境（所有日志，无限流）
services.AddCatga(opt => opt.ForDevelopment());

// 高性能（5000 并发，64 分片）
services.AddCatga(opt => opt.WithHighPerformance());

// 完整弹性（熔断器 + 限流）
services.AddCatga(opt => opt.WithResilience());

// 最小化（零开销，最快）
services.AddCatga(opt => opt.Minimal());
```

## 🌐 NATS 传输

```csharp
services.AddNatsCatga("nats://localhost:4222", opt =>
{
    opt.MaxConcurrentRequests = 1000;
    opt.EnableCircuitBreaker = true;
});
```

## 🔧 AOT 兼容性

- ✅ 零反射
- ✅ 显式泛型注册
- ✅ 编译时类型检查
- ✅ 无 `object` 装箱
- ✅ 强类型字典

## 📈 性能指标

| 传输 | 延迟 | 吞吐量 | 并发 |
|------|------|--------|------|
| Memory | < 1ms | 100K+ msg/s | 5000+ |
| NATS | < 5ms | 50K+ msg/s | 5000+ |

## 🏗️ 设计原则

1. **简单优先** - 最少配置，开箱即用
2. **性能优先** - 无锁、非阻塞、零分配
3. **AOT 友好** - 显式注册，零反射
4. **可观测性** - 内置追踪、日志、DLQ

## 📚 高级特性

### 自定义 Validator

```csharp
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public Task<List<string>> ValidateAsync(CreateUserCommand cmd, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(cmd.Name))
            errors.Add("Name is required");
        return Task.FromResult(errors);
    }
}

services.AddValidator<CreateUserCommand, CreateUserValidator>();
```

### 死信队列处理

```csharp
// 自动发送到 DLQ（重试失败后）
// 检查失败消息
var failed = await dlq.GetFailedMessagesAsync();
foreach (var msg in failed)
{
    Console.WriteLine($"Failed: {msg.MessageType} - {msg.ExceptionMessage}");
}
```

## 📄 License

MIT
