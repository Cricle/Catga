# Catga.Nats

NATS 传输扩展 - 为 Catga 提供分布式消息支持

## ✨ 特性

- ✅ **完整 Pipeline Behaviors** - 订阅端支持所有 Behaviors
- ✅ **Request-Reply 模式** - NATS 请求响应
- ✅ **Pub-Sub 模式** - NATS 发布订阅
- ✅ **分布式追踪** - ActivitySource 支持
- ✅ **自动订阅管理** - 简单的订阅 API
- ✅ **100% AOT 兼容** - 零反射

## 🚀 快速开始

### 1. 安装

```bash
dotnet add package Catga.Nats
```

### 2. 发送端配置

```csharp
services.AddNatsCatga("nats://localhost:4222", options =>
{
    options.WithHighPerformance();
    options.EnableCircuitBreaker = true;
    options.EnableRateLimiting = true;
});
```

### 3. 订阅端配置

```csharp
// 注册 NATS Transit
services.AddNatsCatga("nats://localhost:4222", options =>
{
    options.EnableLogging = true;
    options.EnableTracing = true;
    options.EnableIdempotency = true;
    options.EnableRetry = true;
});

// 注册处理器
services.AddRequestHandler<GetUserQuery, User, GetUserHandler>();

// 订阅请求
services.SubscribeToNatsRequest<GetUserQuery, User>();

// 订阅事件
services.AddEventHandler<UserCreatedEvent, SendEmailHandler>();
services.SubscribeToNatsEvent<UserCreatedEvent>();
```

### 4. 启动订阅

```csharp
var app = builder.Build();

// 启动请求订阅
var requestSubscriber = app.Services
    .GetRequiredService<NatsRequestSubscriber<GetUserQuery, User>>();
requestSubscriber.Start();

// 启动事件订阅
var eventSubscriber = app.Services
    .GetRequiredService<NatsEventSubscriber<UserCreatedEvent>>();
eventSubscriber.Start();

await app.RunAsync();
```

## 📊 架构

### Request-Reply 模式

```
Client                    NATS                    Server
  |                        |                        |
  |--SendAsync(request)--->|                        |
  |                        |--subscribe----------->|
  |                        |                        |
  |                        |<--Pipeline Behaviors--|
  |                        |   (Logging, Tracing,  |
  |                        |    Idempotency, etc)  |
  |                        |                        |
  |<--CatgaResult--------|<--reply---------------|
```

### Pub-Sub 模式

```
Publisher                 NATS                Subscriber 1, 2, N
  |                        |                        |
  |--PublishAsync(event)-->|                        |
  |                        |--broadcast---------->|
  |                        |                        |
  |                        |      (Parallel event handlers)
```

## 🎯 完整示例

### 定义消息

```csharp
public record GetUserQuery(long UserId) : IQuery<User>;
public record UserCreatedEvent(long UserId, string Name) : IEvent;
```

### 定义处理器

```csharp
public class GetUserHandler : IRequestHandler<GetUserQuery, User>
{
    public async Task<CatgaResult<User>> HandleAsync(
        GetUserQuery query,
        CancellationToken ct)
    {
        var user = await _db.GetUserAsync(query.UserId);
        return CatgaResult<User>.Success(user);
    }
}
```

### 发送端

```csharp
public class UserService(ICatgaMediator mediator)
{
    public async Task<User> GetUserAsync(long id)
    {
        var result = await mediator.SendAsync<GetUserQuery, User>(
            new GetUserQuery(id));
        return result.Value;
    }

    public async Task PublishUserCreatedAsync(long userId, string name)
    {
        await mediator.PublishAsync(new UserCreatedEvent(userId, name));
    }
}
```

### 订阅端

```csharp
// Startup.cs
services.AddNatsCatga("nats://localhost:4222");
services.AddRequestHandler<GetUserQuery, User, GetUserHandler>();
services.SubscribeToNatsRequest<GetUserQuery, User>();

// Program.cs
var subscriber = app.Services
    .GetRequiredService<NatsRequestSubscriber<GetUserQuery, User>>();
subscriber.Start();
```

## 🔧 配置选项

```csharp
services.AddNatsCatga("nats://localhost:4222", options =>
{
    // Pipeline Behaviors
    options.EnableLogging = true;
    options.EnableTracing = true;
    options.EnableIdempotency = true;
    options.EnableValidation = true;
    options.EnableRetry = true;

    // 性能
    options.MaxConcurrentRequests = 1000;
    options.IdempotencyShardCount = 32;

    // 弹性
    options.EnableCircuitBreaker = true;
    options.EnableRateLimiting = true;
    options.EnableDeadLetterQueue = true;

    // 或使用预设
    // options.WithHighPerformance();
    // options.WithResilience();
});
```

## 📈 性能指标

- **延迟**: < 5ms (本地网络)
- **吞吐量**: 50K+ msg/s
- **并发**: 5000+ 并发请求
- **可靠性**: 自动重试 + 熔断器 + 死信队列

## 🎨 最佳实践

### 1. 发送端（Client）

```csharp
// 使用熔断器和限流保护
services.AddNatsCatga("nats://localhost:4222", opt =>
{
    opt.EnableCircuitBreaker = true;
    opt.EnableRateLimiting = true;
    opt.MaxConcurrentRequests = 1000;
});
```

### 2. 订阅端（Server）

```csharp
// 启用完整 Pipeline Behaviors
services.AddNatsCatga("nats://localhost:4222", opt =>
{
    opt.EnableLogging = true;
    opt.EnableTracing = true;
    opt.EnableIdempotency = true;
    opt.EnableRetry = true;
    opt.MaxConcurrentRequests = 2000;
});
```

### 3. 事件驱动

```csharp
// 一个事件，多个订阅者
services.AddEventHandler<OrderCreatedEvent, SendEmailHandler>();
services.AddEventHandler<OrderCreatedEvent, UpdateInventoryHandler>();
services.AddEventHandler<OrderCreatedEvent, NotifyAdminHandler>();
services.SubscribeToNatsEvent<OrderCreatedEvent>();
```

## 🔍 可观测性

### 分布式追踪

自动集成 ActivitySource：

```csharp
options.EnableTracing = true;
```

每个请求/事件都会创建 Activity：
- `transit.message_id`
- `transit.correlation_id`
- `transit.message_type`
- `transit.success`

### 死信队列

自动捕获失败消息：

```csharp
public class AdminController(IDeadLetterQueue dlq)
{
    public async Task<List<DeadLetterMessage>> GetFailed()
    {
        return await dlq.GetFailedMessagesAsync(100);
    }
}
```

## 📄 License

MIT

