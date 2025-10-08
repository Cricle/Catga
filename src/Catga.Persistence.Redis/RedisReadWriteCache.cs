using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Persistence.Redis;

/// <summary>
/// Read-write separated Redis cache with local memory cache
/// - Reads: Local cache → Redis (read replica) → DB
/// - Writes: DB → Redis (master) → Local cache invalidation
/// </summary>
public class RedisReadWriteCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisReadWriteCache> _logger;

    // Local L1 cache (memory)
    private readonly ConcurrentDictionary<string, CacheEntry> _localCache = new();
    private readonly TimeSpan _localCacheTtl;

    // Redis connection endpoints
    private readonly IDatabase _readDb;   // Read from replica
    private readonly IDatabase _writeDb;  // Write to master

    public RedisReadWriteCache(
        IConnectionMultiplexer redis,
        ILogger<RedisReadWriteCache> logger,
        TimeSpan? localCacheTtl = null)
    {
        _redis = redis;
        _logger = logger;
        _localCacheTtl = localCacheTtl ?? TimeSpan.FromMinutes(5);

        // For simplicity, using same DB for read/write
        // In production, configure separate read replica
        _readDb = redis.GetDatabase();
        _writeDb = redis.GetDatabase();
    }

    /// <summary>
    /// Get value with multi-level caching
    /// L1 (Memory) → L2 (Redis) → DB
    /// </summary>
    public async Task<string?> GetAsync(
        string key,
        Func<Task<string?>>? fetchFromDb = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        // L1: Check local cache
        if (_localCache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _logger.LogTrace("Cache hit (L1): {Key}", key);
            return entry.Value;
        }

        // L2: Check Redis
        var redisValue = await _readDb.StringGetAsync(key);
        if (redisValue.HasValue)
        {
            var value = redisValue.ToString();

            // Update L1 cache
            _localCache[key] = new CacheEntry(value, _localCacheTtl);

            _logger.LogTrace("Cache hit (L2): {Key}", key);
            return value;
        }

        // L3: Fetch from DB (if provided)
        if (fetchFromDb != null)
        {
            var dbValue = await fetchFromDb();
            if (dbValue != null)
            {
                // Write to both caches
                await SetAsync(key, dbValue, expiry, cancellationToken);

                _logger.LogTrace("Cache miss, loaded from DB: {Key}", key);
                return dbValue;
            }
        }

        _logger.LogTrace("Cache miss: {Key}", key);
        return null;
    }

    /// <summary>
    /// Set value with write-through caching
    /// DB → Redis (master) → Local cache
    /// </summary>
    public async Task SetAsync(
        string key,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        // Write to Redis (master)
        await _writeDb.StringSetAsync(key, value, expiry);

        // Write to L1 cache
        _localCache[key] = new CacheEntry(value, _localCacheTtl);

        _logger.LogTrace("Cache set: {Key}", key);
    }

    /// <summary>
    /// Delete value with cache invalidation
    /// </summary>
    public async Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        // Delete from Redis
        await _writeDb.KeyDeleteAsync(key);

        // Invalidate L1 cache
        _localCache.TryRemove(key, out _);

        _logger.LogTrace("Cache deleted: {Key}", key);
    }

    /// <summary>
    /// Invalidate local cache (e.g., after external update)
    /// </summary>
    public void InvalidateLocal(string key)
    {
        _localCache.TryRemove(key, out _);
    }

    /// <summary>
    /// Clear all local cache
    /// </summary>
    public void ClearLocal()
    {
        _localCache.Clear();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            LocalCacheCount = _localCache.Count,
            LocalCacheSize = _localCache.Count * 100 // Rough estimate
        };
    }

    private class CacheEntry
    {
        public string Value { get; }
        public DateTime ExpiresAt { get; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        public CacheEntry(string value, TimeSpan ttl)
        {
            Value = value;
            ExpiresAt = DateTime.UtcNow + ttl;
        }
    }
}

public class CacheStatistics
{
    public int LocalCacheCount { get; init; }
    public long LocalCacheSize { get; init; }
}

