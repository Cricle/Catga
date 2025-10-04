# Catga.Redis

Redis 持久化支持，为 CatGa 和幂等性提供生产级的持久化能力。

## ✨ 特性

- ✅ **Saga 持久化**：支持 Saga 状态持久化到 Redis
- ✅ **幂等性存储**：高性能的幂等性检查
- ✅ **乐观锁**：Saga 版本控制
- ✅ **状态索引**：支持按状态查询 Saga
- ✅ **自动过期**：自动清理过期数据
- ✅ **高性能**：利用 Redis 的高性能特性
- ✅ **连接池**：复用 Redis 连接

## 📦 安装

```bash
dotnet add package Catga.Redis
```

## 🚀 快速开始

### 1. 基础配置

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 添加 Transit
services.AddCatga();

// 添加 Redis 持久化
services.AddRedisCatga(options =>
{
    options.ConnectionString = "localhost:6379";
    options.SagaExpiry = TimeSpan.FromDays(7);
    options.IdempotencyExpiry = TimeSpan.FromHours(24);
});
```

### 2. 使用 Saga

```csharp
// Saga 会自动持久化到 Redis
var orchestrator = new SagaOrchestrator<OrderSagaData>(repository, logger);

orchestrator
    .AddStep(new ProcessPaymentStep())
    .AddStep(new ReserveInventoryStep())
    .AddStep(new ScheduleShipmentStep());

var saga = new OrderSaga { Data = new OrderSagaData { /* ... */ } };

// 执行 Saga（自动持久化）
var result = await orchestrator.ExecuteAsync(saga);

// 从 Redis 恢复 Saga
var recovered = await repository.GetAsync<OrderSagaData>(saga.CorrelationId);
```

### 3. 幂等性检查

```csharp
// 幂等性会自动使用 Redis
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<CatgaResult<Guid>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // IdempotencyBehavior 会自动使用 Redis 检查
        var orderId = Guid.NewGuid();
        // ... 处理订单 ...
        return CatgaResult<Guid>.Success(orderId);
    }
}
```

## ⚙️ 配置选项

### Redis 连接配置

```csharp
services.AddRedisCatga(options =>
{
    // Redis 连接字符串
    options.ConnectionString = "localhost:6379,password=secret,ssl=true";

    // 连接超时（毫秒）
    options.ConnectTimeout = 5000;

    // 同步超时（毫秒）
    options.SyncTimeout = 5000;

    // 重试次数
    options.ConnectRetry = 3;

    // 保持连接
    options.KeepAlive = 60;

    // SSL 配置
    options.UseSsl = true;
    options.SslHost = "redis.example.com";
});
```

### Saga 配置

```csharp
services.AddRedisCatga(options =>
{
    // Saga 键前缀
    options.SagaKeyPrefix = "myapp:saga:";

    // Saga 过期时间
    options.SagaExpiry = TimeSpan.FromDays(30);
});
```

### 幂等性配置

```csharp
services.AddRedisCatga(options =>
{
    // 幂等性键前缀
    options.IdempotencyKeyPrefix = "myapp:idempotency:";

    // 幂等性过期时间
    options.IdempotencyExpiry = TimeSpan.FromHours(48);
});
```

## 🔧 高级用法

### 1. 单独使用 Saga 仓储

```csharp
// 只使用 Redis Saga 仓储
services.AddRedisSagaRepository(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

### 2. 单独使用幂等性存储

```csharp
// 只使用 Redis 幂等性存储
services.AddRedisIdempotencyStore(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

### 3. 自定义 Redis 连接

```csharp
// 注册自定义 Redis 连接
services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = ConfigurationOptions.Parse("localhost:6379");
    config.DefaultDatabase = 1; // 使用数据库 1
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// 然后添加 Transit 组件
services.AddSingleton<ISagaRepository, RedisSagaRepository>();
services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
```

## 📊 Redis 数据结构

### Saga 存储

```
键格式: saga:{correlationId}
类型: Hash

字段:
- correlationId: Guid
- state: int (枚举值)
- version: int
- createdAt: long (Ticks)
- updatedAt: long (Ticks)
- type: string (类型全名)
- data: string (JSON)
```

### Saga 状态索引

```
键格式: saga:state:{state}
类型: Set

成员: correlationId 列表
```

### 幂等性存储

```
键格式: idempotency:{messageId}
类型: String (JSON)

内容:
{
  "messageId": "...",
  "processedAt": "2024-01-01T12:00:00Z",
  "resultType": "System.Guid, ...",
  "resultJson": "\"...\""
}
```

## 🔐 生产环境最佳实践

### 1. 连接字符串安全

```csharp
// 使用配置文件或环境变量
var connectionString = builder.Configuration["Redis:ConnectionString"];

services.AddRedisCatga(options =>
{
    options.ConnectionString = connectionString;
});
```

### 2. 连接池配置

```csharp
services.AddRedisCatga(options =>
{
    options.ConnectionString = "localhost:6379";
    options.ConnectRetry = 5; // 增加重试次数
    options.KeepAlive = 30; // 保持连接
});
```

### 3. 过期时间优化

```csharp
services.AddRedisCatga(options =>
{
    // Saga 保留更长时间（用于审计）
    options.SagaExpiry = TimeSpan.FromDays(30);

    // 幂等性保留时间根据业务需求调整
    options.IdempotencyExpiry = TimeSpan.FromHours(24);
});
```

### 4. 键前缀隔离

```csharp
services.AddRedisCatga(options =>
{
    // 使用应用名称作为前缀，避免键冲突
    options.SagaKeyPrefix = "myapp:production:saga:";
    options.IdempotencyKeyPrefix = "myapp:production:idempotency:";
});
```

## 🚀 性能优化

### 1. 使用 Pipeline

Redis 客户端自动使用连接池和 Pipeline 优化性能。

### 2. 批量操作

```csharp
// Redis 事务自动批量执行
var transaction = db.CreateTransaction();
transaction.HashSetAsync(key1, ...);
transaction.HashSetAsync(key2, ...);
await transaction.ExecuteAsync();
```

### 3. 过期时间策略

- Saga：7-30 天（根据审计需求）
- 幂等性：1-24 小时（根据重试窗口）

## 📈 监控和诊断

### 1. 连接健康检查

```csharp
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis is healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unhealthy", ex);
        }
    }
}

// 注册健康检查
services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis");
```

### 2. 日志记录

```csharp
// Redis 组件会自动记录日志
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
```

## 🔄 迁移指南

### 从内存存储迁移

```csharp
// 之前：使用内存存储
services.AddSingleton<ISagaRepository, InMemorySagaRepository>();

// 之后：使用 Redis 存储
services.AddRedisSagaRepository(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

### 混合使用

```csharp
// 开发环境：使用内存存储
if (builder.Environment.IsDevelopment())
{
    services.AddSingleton<ISagaRepository, InMemorySagaRepository>();
}
// 生产环境：使用 Redis 存储
else
{
    services.AddRedisSagaRepository(options =>
    {
        options.ConnectionString = builder.Configuration["Redis:ConnectionString"];
    });
}
```

## 📚 相关文档

- [Saga 使用指南](../../docs/SAGA_AND_STATE_MACHINE.md)
- [幂等性说明](../../docs/FINAL_FEATURES.md)
- [StackExchange.Redis 文档](https://stackexchange.github.io/StackExchange.Redis/)

