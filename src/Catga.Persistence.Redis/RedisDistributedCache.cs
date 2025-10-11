using Catga.Caching;
using StackExchange.Redis;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis-based distributed cache implementation
/// </summary>
public sealed class RedisDistributedCache : IDistributedCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisDistributedCache(IConnectionMultiplexer redis)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [RequiresUnreferencedCode("Cache serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Cache serialization may require runtime code generation")]
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);

        if (!value.HasValue)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value.ToString(), _jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    [RequiresUnreferencedCode("Cache serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Cache serialization may require runtime code generation")]
    public async ValueTask SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        await db.StringSetAsync(key, json, expiration);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(key);
    }

    public async ValueTask RefreshAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        await db.KeyExpireAsync(key, expiration);
    }
}

