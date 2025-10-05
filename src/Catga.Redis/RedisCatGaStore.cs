using Catga.CatGa.Models;
using Catga.Redis.Serialization;
using StackExchange.Redis;

namespace Catga.Redis;

/// <summary>
/// Redis 实现的 CatGa 持久化存储
/// 用于分布式环境下的幂等性和结果缓存
/// </summary>
public sealed class RedisCatGaStore : IDisposable
{
    private readonly IDatabase _database;
    private readonly RedisCatgaOptions _options;

    public RedisCatGaStore(
        IConnectionMultiplexer redis,
        RedisCatgaOptions options)
    {
        _database = redis.GetDatabase();
        _options = options;
    }

    /// <summary>
    /// 检查是否已处理（幂等性检查）
    /// </summary>
    public async Task<bool> IsProcessedAsync(string key, CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        return await _database.KeyExistsAsync(redisKey);
    }

    /// <summary>
    /// 标记为已处理并缓存结果
    /// </summary>
    public async Task MarkAsProcessedAsync<TResult>(
        string key,
        TResult result,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        var value = RedisJsonSerializer.Serialize(result);
        var actualExpiry = expiry ?? _options.IdempotencyExpiry;

        await _database.StringSetAsync(
            redisKey,
            value,
            actualExpiry);
    }

    /// <summary>
    /// 获取缓存的结果
    /// </summary>
    public async Task<TResult?> GetCachedResultAsync<TResult>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        var value = await _database.StringGetAsync(redisKey);

        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return RedisJsonSerializer.Deserialize<TResult>(value!);
    }

    /// <summary>
    /// 尝试获取缓存的结果
    /// </summary>
    public async Task<(bool Found, TResult? Result)> TryGetCachedResultAsync<TResult>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        var value = await _database.StringGetAsync(redisKey);

        if (value.IsNullOrEmpty)
        {
            return (false, default);
        }

        var result = RedisJsonSerializer.Deserialize<TResult>(value!);
        return (true, result);
    }

    /// <summary>
    /// 删除缓存
    /// </summary>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        await _database.KeyDeleteAsync(redisKey);
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    public async Task DeleteBatchAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var redisKeys = keys.Select(k => (RedisKey)GetRedisKey(k)).ToArray();
        await _database.KeyDeleteAsync(redisKeys);
    }

    /// <summary>
    /// 获取带 TTL 的结果
    /// </summary>
    public async Task<(bool Found, TResult? Result, TimeSpan? TTL)> GetWithTTLAsync<TResult>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);

        var batch = _database.CreateBatch();
        var valueTask = batch.StringGetAsync(redisKey);
        var ttlTask = batch.KeyTimeToLiveAsync(redisKey);
        batch.Execute();

        await Task.WhenAll(valueTask, ttlTask);

        var value = await valueTask;
        var ttl = await ttlTask;

        if (value.IsNullOrEmpty)
        {
            return (false, default, null);
        }

        var result = RedisJsonSerializer.Deserialize<TResult>(value!);
        return (true, result, ttl);
    }

    /// <summary>
    /// 原子性的设置（仅当不存在时）
    /// </summary>
    public async Task<bool> SetIfNotExistsAsync<TResult>(
        string key,
        TResult result,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        var value = RedisJsonSerializer.Serialize(result);
        var actualExpiry = expiry ?? _options.IdempotencyExpiry;

        return await _database.StringSetAsync(
            redisKey,
            value,
            actualExpiry,
            When.NotExists);
    }

    /// <summary>
    /// 刷新过期时间
    /// </summary>
    public async Task<bool> RefreshExpiryAsync(
        string key,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var redisKey = GetRedisKey(key);
        var actualExpiry = expiry ?? _options.IdempotencyExpiry;

        return await _database.KeyExpireAsync(redisKey, actualExpiry);
    }

    // 获取 Redis 键
    private string GetRedisKey(string key)
    {
        return $"catga:{key}";
    }

    public void Dispose()
    {
        // Connection 由外部管理，这里不需要释放
    }
}

