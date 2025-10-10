# Redis 示例 - Catga 分布式锁和缓存

## 📖 简介

这是一个极简的 Redis 示例，演示 Catga 的：
- 🔐 **分布式锁** - 防止并发问题
- 📦 **分布式缓存** - 提升查询性能

## 🚀 快速开始

### 1. 启动 Redis

```bash
docker run -d -p 6379:6379 redis:latest
```

### 2. 运行示例

```bash
cd examples/RedisExample
dotnet run
```

### 3. 测试 API

访问 Swagger: `https://localhost:5001/swagger`

**创建订单（带分布式锁）**:
```bash
curl -X POST https://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": "PROD-001", "quantity": 2}'
```

**查询订单（带缓存）**:
```bash
curl https://localhost:5001/orders/123
```

## 🎯 核心特性

### 分布式锁

```csharp
await using var lockHandle = await _lock.AcquireAsync(lockKey, TimeSpan.FromSeconds(10), ct);

if (lockHandle == null)
{
    return CatgaResult<OrderResponse>.Failure("系统繁忙，请稍后重试");
}

// 在锁保护下执行业务逻辑
```

**特点**:
- ✅ 自动释放（IAsyncDisposable）
- ✅ 超时控制
- ✅ 并发安全

### 分布式缓存

```csharp
// 读缓存
var cached = await _cache.GetAsync<OrderResponse>(cacheKey, ct);

// 写缓存（带过期时间）
await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(1), ct);
```

**特点**:
- ✅ 泛型支持
- ✅ 过期控制
- ✅ 零序列化代码

## 📊 性能

- **缓存命中**: ~0.5ms
- **缓存未命中**: ~10ms
- **分布式锁**: ~1ms

## 🔧 配置

修改 Redis 连接字符串:

```csharp
var redisConnection = ConnectionMultiplexer.Connect("your-redis-host:6379");
```

## 📚 相关文档

- [Catga 快速开始](../../QUICK_START.md)
- [架构说明](../../ARCHITECTURE.md)

