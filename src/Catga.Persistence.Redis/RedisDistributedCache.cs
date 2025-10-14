using System.Diagnostics.CodeAnalysis;
using Catga.Caching;
using Catga.Serialization;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis-based distributed cache implementation (uses injected IMessageSerializer for consistency)
/// </summary>
public sealed class RedisDistributedCache : IDistributedCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;

    public RedisDistributedCache(IConnectionMultiplexer redis, IMessageSerializer serializer)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization warnings are marked on IMessageSerializer interface")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization warnings are marked on IMessageSerializer interface")]
    public async ValueTask<T?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        string key,
        CancellationToken cancellationToken = default)
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
            return _serializer.Deserialize<T>((byte[])value!);
        }
        catch
        {
            return default;
        }
    }
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization warnings are marked on IMessageSerializer interface")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization warnings are marked on IMessageSerializer interface")]
    public async ValueTask SetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        var bytes = _serializer.Serialize(value);

        await db.StringSetAsync(key, bytes, expiration);
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

