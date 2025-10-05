using Catga.Redis.Serialization;
using Catga.Idempotency;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Redis;

/// <summary>
/// Redis 幂等性存储 - 生产级高性能
/// </summary>
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultExpiry;

    public RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        ILogger<RedisIdempotencyStore> logger,
        RedisCatgaOptions? options = null)
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = options?.IdempotencyKeyPrefix ?? "idempotency:";
        _defaultExpiry = options?.IdempotencyExpiry ?? TimeSpan.FromHours(24);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(messageId);
        return await db.KeyExistsAsync(key);
    }

    /// <inheritdoc/>
    public async Task MarkAsProcessedAsync<TResult>(
        string messageId,
        TResult? result = default,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(messageId);

        var entry = new IdempotencyEntry
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow,
            ResultType = result?.GetType().AssemblyQualifiedName,
            ResultJson = result != null ? RedisJsonSerializer.Serialize(result) : null
        };

        var json = RedisJsonSerializer.Serialize(entry);
        await db.StringSetAsync(key, json, _defaultExpiry);

        _logger.LogDebug("Marked message {MessageId} as processed in Redis", messageId);
    }

    /// <inheritdoc/>
    public async Task<TResult?> GetCachedResultAsync<TResult>(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(messageId);

        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
        {
            return default;
        }

        var entry = RedisJsonSerializer.Deserialize<IdempotencyEntry>(json!);
        if (entry?.ResultJson == null)
        {
            return default;
        }

        // 验证类型匹配
        var expectedType = typeof(TResult).AssemblyQualifiedName;
        if (entry.ResultType != expectedType)
        {
            _logger.LogWarning("Type mismatch for message {MessageId}: expected {Expected}, got {Actual}",
                messageId, expectedType, entry.ResultType);
            return default;
        }

        return RedisJsonSerializer.Deserialize<TResult>(entry.ResultJson);
    }

    private string GetKey(string messageId) => $"{_keyPrefix}{messageId}";

    /// <summary>
    /// 幂等性条目
    /// </summary>
    private class IdempotencyEntry
    {
        public string MessageId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public string? ResultType { get; set; }
        public string? ResultJson { get; set; }
    }
}

