# 分布式缓存指南

Catga 提供了简单而强大的分布式缓存支持，基于 Cache-Aside 模式，自动缓存请求响应。

---

## 📚 目录

- [快速开始](#快速开始)
- [基本用法](#基本用法)
- [高级配置](#高级配置)
- [缓存策略](#缓存策略)
- [最佳实践](#最佳实践)
- [性能优化](#性能优化)

---

## 快速开始

### 1. 安装依赖

```bash
# Catga 核心
dotnet add package Catga

# Redis 缓存支持
dotnet add package Catga.Persistence.Redis
```

### 2. 注册服务

```csharp
using Catga.Caching;
using Catga.Persistence.Redis.DependencyInjection;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 注册 Redis 连接
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));

// 注册分布式缓存
builder.Services.AddRedisDistributedCache();

// 添加缓存 Behavior
builder.Services.AddCachingBehavior();

var app = builder.Build();
app.Run();
```

### 3. 定义可缓存请求

```csharp
using Catga.Caching;
using Catga.Messages;

public record GetUserQuery(string UserId) : IRequest<UserDto>, ICacheable
{
    // 定义缓存键
    public string GetCacheKey() => $"user:{UserId}";

    // 设置过期时间 (默认 5 分钟)
    public TimeSpan CacheExpiration => TimeSpan.FromMinutes(10);
}

public record UserDto(string Id, string Name, string Email);
```

### 4. 实现 Handler

```csharp
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _repository;

    public GetUserQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<UserDto> Handle(
        GetUserQuery request,
        CancellationToken cancellationToken)
    {
        // 如果缓存未命中，这个方法才会执行
        var user = await _repository.GetByIdAsync(request.UserId);
        
        return new UserDto(user.Id, user.Name, user.Email);
    }
}
```

### 5. 使用

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public UsersController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetUser(string userId)
    {
        // 第一次调用 - 执行 Handler，缓存结果
        // 后续调用 - 直接从缓存返回
        var user = await _mediator.SendAsync(new GetUserQuery(userId));
        
        return Ok(user);
    }
}
```

---

## 基本用法

### ICacheable 接口

```csharp
public interface ICacheable
{
    // 获取缓存键
    string GetCacheKey();

    // 缓存过期时间 (默认 5 分钟)
    TimeSpan CacheExpiration => TimeSpan.FromMinutes(5);
}
```

### 缓存键设计

#### 简单键

```csharp
public record GetProductQuery(string ProductId) : IRequest<ProductDto>, ICacheable
{
    public string GetCacheKey() => $"product:{ProductId}";
}
```

#### 复合键

```csharp
public record SearchProductsQuery(
    string Category,
    int Page,
    int PageSize) : IRequest<ProductListDto>, ICacheable
{
    public string GetCacheKey() => $"products:{Category}:{Page}:{PageSize}";
}
```

#### 包含用户的键

```csharp
public record GetUserOrdersQuery(
    string UserId,
    DateTime From,
    DateTime To) : IRequest<OrderListDto>, ICacheable
{
    public string GetCacheKey() => 
        $"orders:{UserId}:{From:yyyy-MM-dd}:{To:yyyy-MM-dd}";
}
```

---

## 高级配置

### 自定义过期时间

```csharp
public record GetProductQuery(string ProductId) : IRequest<ProductDto>, ICacheable
{
    public string GetCacheKey() => $"product:{ProductId}";

    // 热门产品缓存 1 小时
    public TimeSpan CacheExpiration => TimeSpan.FromHours(1);
}

public record GetRealtimePriceQuery(string Symbol) : IRequest<decimal>, ICacheable
{
    public string GetCacheKey() => $"price:{Symbol}";

    // 实时价格缓存 10 秒
    public TimeSpan CacheExpiration => TimeSpan.FromSeconds(10);
}
```

### 条件缓存

```csharp
public record SearchQuery(string Term, bool IncludeDrafts) : IRequest<SearchResultDto>, ICacheable
{
    // 只缓存不包含草稿的搜索
    public string GetCacheKey() => IncludeDrafts 
        ? string.Empty  // 空键 = 不缓存
        : $"search:{Term}";
    
    public TimeSpan CacheExpiration => TimeSpan.FromMinutes(15);
}
```

### 手动缓存管理

```csharp
public class ProductService
{
    private readonly IDistributedCache _cache;
    private readonly IProductRepository _repository;

    public ProductService(
        IDistributedCache cache,
        IProductRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }

    // 手动缓存
    public async Task<ProductDto> GetProductAsync(string productId)
    {
        var cacheKey = $"product:{productId}";

        // 尝试从缓存获取
        var cached = await _cache.GetAsync<ProductDto>(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        // 缓存未命中，从数据库加载
        var product = await _repository.GetByIdAsync(productId);
        var dto = MapToDto(product);

        // 写入缓存
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(10));

        return dto;
    }

    // 更新产品并清除缓存
    public async Task UpdateProductAsync(string productId, UpdateProductDto dto)
    {
        await _repository.UpdateAsync(productId, dto);

        // 清除缓存
        var cacheKey = $"product:{productId}";
        await _cache.RemoveAsync(cacheKey);
    }
}
```

---

## 缓存策略

### 1. Cache-Aside (读穿透)

**Catga 默认策略**

```csharp
public async Task<T> GetAsync<T>(string key)
{
    // 1. 尝试从缓存读取
    var cached = await _cache.GetAsync<T>(key);
    if (cached != null)
        return cached;

    // 2. 缓存未命中，从数据源加载
    var data = await _dataSource.LoadAsync(key);

    // 3. 写入缓存
    await _cache.SetAsync(key, data, expiration);

    return data;
}
```

### 2. Write-Through (写穿透)

```csharp
public async Task UpdateAsync(string key, T value)
{
    // 1. 更新数据源
    await _dataSource.UpdateAsync(key, value);

    // 2. 更新缓存
    await _cache.SetAsync(key, value, expiration);
}
```

### 3. Write-Behind (写回)

```csharp
public async Task UpdateAsync(string key, T value)
{
    // 1. 更新缓存
    await _cache.SetAsync(key, value, expiration);

    // 2. 异步更新数据源
    _ = Task.Run(() => _dataSource.UpdateAsync(key, value));
}
```

### 4. Refresh-Ahead (预刷新)

```csharp
public async Task<T> GetAsync<T>(string key)
{
    var cached = await _cache.GetAsync<T>(key);
    
    if (cached != null)
    {
        // 如果缓存快过期，后台刷新
        var ttl = await _cache.GetTtlAsync(key);
        if (ttl < TimeSpan.FromMinutes(1))
        {
            _ = Task.Run(async () =>
            {
                var fresh = await _dataSource.LoadAsync(key);
                await _cache.SetAsync(key, fresh, expiration);
            });
        }
        
        return cached;
    }

    // 缓存未命中
    var data = await _dataSource.LoadAsync(key);
    await _cache.SetAsync(key, data, expiration);
    return data;
}
```

---

## 最佳实践

### 1. 缓存键设计

✅ **好的设计**
```csharp
// 清晰的命名空间
"user:{userId}"
"product:{productId}"
"cart:{userId}"

// 版本化
"user:v2:{userId}"

// 包含租户
"tenant:{tenantId}:user:{userId}"
```

❌ **不好的设计**
```csharp
// 太短
"u:{id}"

// 没有命名空间
"{userId}"

// 不稳定的哈希
"{object.GetHashCode()}"
```

### 2. 过期时间选择

```csharp
// 静态数据 - 长过期
public TimeSpan CacheExpiration => TimeSpan.FromHours(24);

// 半静态数据 - 中等过期
public TimeSpan CacheExpiration => TimeSpan.FromMinutes(30);

// 动态数据 - 短过期
public TimeSpan CacheExpiration => TimeSpan.FromMinutes(5);

// 实时数据 - 极短过期
public TimeSpan CacheExpiration => TimeSpan.FromSeconds(10);

// 不缓存
public string GetCacheKey() => string.Empty;
```

### 3. 缓存失效

```csharp
// 更新时失效
public async Task UpdateUserAsync(string userId, UpdateUserDto dto)
{
    await _repository.UpdateAsync(userId, dto);
    
    // 删除相关缓存
    await _cache.RemoveAsync($"user:{userId}");
    await _cache.RemoveAsync($"user-list");
}

// 批量失效
public async Task InvalidateUserCacheAsync(string userId)
{
    var keys = new[]
    {
        $"user:{userId}",
        $"user-profile:{userId}",
        $"user-orders:{userId}",
        $"user-settings:{userId}"
    };

    foreach (var key in keys)
    {
        await _cache.RemoveAsync(key);
    }
}
```

### 4. 缓存预热

```csharp
public class CacheWarmupService : IHostedService
{
    private readonly IDistributedCache _cache;
    private readonly IProductRepository _repository;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 预加载热门产品
        var hotProducts = await _repository.GetHotProductsAsync(100);
        
        foreach (var product in hotProducts)
        {
            var key = $"product:{product.Id}";
            await _cache.SetAsync(key, product, TimeSpan.FromHours(1));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

---

## 性能优化

### 1. 批量操作

```csharp
// ❌ 低效 - N 次网络调用
foreach (var id in userIds)
{
    var user = await _cache.GetAsync<UserDto>($"user:{id}");
}

// ✅ 高效 - 1 次网络调用 (需要实现批量接口)
var keys = userIds.Select(id => $"user:{id}").ToArray();
var users = await _cache.GetBatchAsync<UserDto>(keys);
```

### 2. 压缩大对象

```csharp
public class CompressedCache : IDistributedCache
{
    private readonly IDistributedCache _innerCache;

    public async ValueTask SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);
        
        // 大于 1KB 时压缩
        if (json.Length > 1024)
        {
            var compressed = Compress(json);
            await _innerCache.SetAsync(key + ":gz", compressed, expiration, ct);
        }
        else
        {
            await _innerCache.SetAsync(key, value, expiration, ct);
        }
    }
}
```

### 3. 本地缓存 + 分布式缓存

```csharp
public class TwoLevelCache : IDistributedCache
{
    private readonly IMemoryCache _l1Cache;  // 本地
    private readonly IDistributedCache _l2Cache;  // Redis

    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        // L1: 本地缓存
        if (_l1Cache.TryGetValue<T>(key, out var cached))
        {
            return cached;
        }

        // L2: Redis
        var value = await _l2Cache.GetAsync<T>(key, ct);
        if (value != null)
        {
            // 回填到 L1
            _l1Cache.Set(key, value, TimeSpan.FromMinutes(1));
        }

        return value;
    }
}
```

---

## 监控和调试

### 1. 缓存命中率

```csharp
public class CacheMetrics
{
    private long _hits;
    private long _misses;

    public void RecordHit() => Interlocked.Increment(ref _hits);
    public void RecordMiss() => Interlocked.Increment(ref _misses);

    public double HitRate => _hits + _misses > 0 
        ? (double)_hits / (_hits + _misses) 
        : 0;
}

public class MonitoredCache : IDistributedCache
{
    private readonly IDistributedCache _innerCache;
    private readonly CacheMetrics _metrics;

    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await _innerCache.GetAsync<T>(key, ct);
        
        if (value != null)
            _metrics.RecordHit();
        else
            _metrics.RecordMiss();

        return value;
    }
}
```

### 2. 日志记录

```csharp
public class LoggedCache : IDistributedCache
{
    private readonly IDistributedCache _innerCache;
    private readonly ILogger _logger;

    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var value = await _innerCache.GetAsync<T>(key, ct);
        
        _logger.LogDebug(
            "Cache {Result} for key {Key} in {Elapsed}ms",
            value != null ? "HIT" : "MISS",
            key,
            sw.ElapsedMilliseconds);

        return value;
    }
}
```

---

## 故障排查

### 问题 1: 缓存穿透

**现象**: 查询不存在的数据，每次都查数据库

**解决**: 缓存空值
```csharp
var user = await _repository.GetByIdAsync(userId);

// 即使为 null 也缓存
await _cache.SetAsync(
    $"user:{userId}",
    user ?? new UserDto(),  // 空对象
    TimeSpan.FromMinutes(5));
```

### 问题 2: 缓存雪崩

**现象**: 大量缓存同时过期，DB 负载飙升

**解决**: 添加随机偏移
```csharp
var baseExpiration = TimeSpan.FromMinutes(10);
var randomOffset = TimeSpan.FromSeconds(Random.Shared.Next(0, 60));
var expiration = baseExpiration + randomOffset;

await _cache.SetAsync(key, value, expiration);
```

### 问题 3: 缓存击穿

**现象**: 热点数据过期，瞬间大量请求打到 DB

**解决**: 使用分布式锁
```csharp
var cached = await _cache.GetAsync<T>(key);
if (cached != null)
    return cached;

// 尝试获取锁
await using var lockHandle = await _distributedLock.TryAcquireAsync(
    $"lock:{key}",
    TimeSpan.FromSeconds(5));

if (lockHandle != null)
{
    // 获取锁成功，加载数据
    var data = await _dataSource.LoadAsync(key);
    await _cache.SetAsync(key, data, expiration);
    return data;
}
else
{
    // 等待其他线程加载完成
    await Task.Delay(100);
    return await GetAsync<T>(key);
}
```

---

## 与其他 Catga 功能集成

### 与 Event Sourcing 集成

```csharp
public class CachedEventStore : IEventStore
{
    private readonly IEventStore _innerStore;
    private readonly IDistributedCache _cache;

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken ct = default)
    {
        // 缓存整个事件流
        var cacheKey = $"eventstream:{streamId}";
        
        var cached = await _cache.GetAsync<EventStream>(cacheKey, ct);
        if (cached != null && fromVersion == 0)
        {
            return cached;
        }

        var stream = await _innerStore.ReadAsync(streamId, fromVersion, maxCount, ct);

        // 缓存短时间
        if (fromVersion == 0)
        {
            await _cache.SetAsync(cacheKey, stream, TimeSpan.FromSeconds(30), ct);
        }

        return stream;
    }
}
```

### 与分布式锁集成

见"问题 3: 缓存击穿"解决方案

---

## 总结

Catga 的分布式缓存提供：

✅ **简单易用**
- ICacheable 接口自动缓存
- 最少代码实现缓存

✅ **灵活配置**
- 自定义缓存键
- 可配置过期时间
- 条件缓存

✅ **高性能**
- Redis 后端
- 批量操作支持
- 多级缓存

✅ **生产就绪**
- 监控和指标
- 故障排查工具
- 最佳实践

---

**相关文档**:
- [Event Sourcing](./event-sourcing.md)
- [分布式锁](./distributed-lock.md)
- [Saga 模式](./saga-pattern.md)

