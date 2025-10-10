# Redis 示例 - Catga 分布式锁和缓存

## 📖 简介

完整的 Redis 示例，演示 Catga 的：
- ✨ **源生成器自动注册** - 零手动配置
- 🔐 **Redis 分布式锁** - 防止并发问题
- 📦 **Redis 分布式缓存** - 提升查询性能

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

访问 Swagger: `https://localhost:5001/swagger`

### 3. 测试 API

**创建订单（带分布式锁）**:
```bash
curl -X POST https://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": "PROD-001", "quantity": 2}'
```

**查询订单（带缓存）**:
```bash
# 第一次查询：从数据库读取，写入缓存
curl https://localhost:5001/orders/123

# 第二次查询：直接从缓存读取（快！）
curl https://localhost:5001/orders/123
```

## 🎯 核心特性

### 1. Redis 分布式锁

```csharp
// 🔐 使用分布式锁防止并发问题
var lockKey = $"order:product:{cmd.ProductId}";
await using var lockHandle = await _lock.TryAcquireAsync(lockKey, TimeSpan.FromSeconds(10), ct);

if (lockHandle == null)
{
    return CatgaResult<OrderResponse>.Failure("系统繁忙，请稍后重试");
}

// 在锁保护下执行业务逻辑
```

**特点**:
- ✅ 自动释放（IAsyncDisposable）
- ✅ 超时控制
- ✅ 跨服务器并发安全
- ✅ 基于 Redis SETNX 实现

**使用场景**:
- 防止重复下单
- 库存扣减
- 限流控制
- 分布式任务调度

### 2. Redis 分布式缓存

```csharp
// 📦 读缓存
var cached = await _cache.GetAsync<OrderResponse>(cacheKey, ct);

// 💾 写缓存（带过期时间）
await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(1), ct);
```

**特点**:
- ✅ 泛型支持，自动序列化
- ✅ 过期时间控制
- ✅ 跨服务器共享缓存
- ✅ 基于 Redis String 实现

**使用场景**:
- 热点数据缓存
- 查询结果缓存
- 会话数据存储
- 分布式配置

### 3. 源生成器自动注册

```csharp
// ✨ 只需调用一次
builder.Services.AddGeneratedHandlers();
```

**所有 Handler 自动发现，无需手动注册！**

## 📊 性能对比

| 操作 | 无缓存 | 有缓存 | 提升 |
|------|--------|--------|------|
| 查询订单 | ~10ms | ~0.5ms | **20x** |
| 创建订单（无锁） | ~5ms | - | - |
| 创建订单（有锁） | ~6ms | - | +1ms |

## 🔧 配置

### 修改 Redis 连接

**方式 1: appsettings.json**
```json
{
  "Redis": {
    "Connection": "your-redis-host:6379"
  }
}
```

**方式 2: 环境变量**
```bash
export Redis__Connection="your-redis-host:6379"
```

**方式 3: 代码**
```csharp
var redis = ConnectionMultiplexer.Connect("your-redis-host:6379,password=yourpassword");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
```

## 🧪 测试并发场景

### 测试分布式锁

使用 Apache Bench 并发测试：
```bash
# 100 个并发请求
ab -n 100 -c 10 -p order.json -T application/json \
  https://localhost:5001/orders
```

**预期结果**: 所有请求都能正确处理，无重复订单

### 测试缓存效果

```bash
# 第一次查询（慢，从数据库）
time curl https://localhost:5001/orders/123

# 第二次查询（快，从缓存）
time curl https://localhost:5001/orders/123
```

**预期结果**: 第二次查询速度显著提升（~20倍）

## 📚 相关文档

- [Catga 快速开始](../../QUICK_START.md)
- [架构说明](../../ARCHITECTURE.md)
- [Redis 分布式锁实现](../../src/Catga.Persistence.Redis/RedisDistributedLock.cs)
- [Redis 分布式缓存实现](../../src/Catga.Persistence.Redis/RedisDistributedCache.cs)

---

**Redis 示例 - 生产级分布式锁和缓存！** 🚀
