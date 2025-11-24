using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis idempotency store - production-grade high-performance (uses injected IMessageSerializer)
/// </summary>
public class RedisIdempotencyStore : RedisStoreBase, IIdempotencyStore
{
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private readonly TimeSpan _defaultExpiry;

    public RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        ILogger<RedisIdempotencyStore> logger,
        RedisIdempotencyOptions? options = null)
        : base(redis, serializer, options?.KeyPrefix ?? "idempotency:")
    {
        _logger = logger;
        _defaultExpiry = options?.Expiry ?? TimeSpan.FromHours(24);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = BuildKey(messageId);
        return await db.KeyExistsAsync(key);
    }

    /// <inheritdoc/>
    public async Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        TResult? result = default,
        CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = BuildKey(messageId);

        var entry = new IdempotencyEntry
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow,
            ResultType = result?.GetType().AssemblyQualifiedName,
            ResultBytes = result != null ? Serializer.Serialize(result, typeof(TResult)) : null
        };

        var bytes = Serializer.Serialize(entry, typeof(IdempotencyEntry));
        await db.StringSetAsync(key, bytes, _defaultExpiry);

        _logger.LogDebug("Marked message {MessageId} as processed in Redis", messageId);
    }

    /// <inheritdoc/>
    public async Task<TResult?> GetCachedResultAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = BuildKey(messageId);

        var bytes = await db.StringGetAsync(key);
        if (!bytes.HasValue)
        {
            return default;
        }

        var entry = (IdempotencyEntry?)Serializer.Deserialize((byte[])bytes!, typeof(IdempotencyEntry));
        if (entry?.ResultBytes == null)
        {
            return default;
        }

        // Verify type match
        var expectedType = typeof(TResult).AssemblyQualifiedName;
        if (entry.ResultType != expectedType)
        {
            _logger.LogWarning("Type mismatch for message {MessageId}: expected {Expected}, got {Actual}",
                messageId, expectedType, entry.ResultType);
            return default;
        }

        return (TResult?)Serializer.Deserialize(entry.ResultBytes, typeof(TResult));
    }

    /// <summary>
    /// Idempotency entry
    /// </summary>
    private class IdempotencyEntry
    {
        public long MessageId { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string? ResultType { get; set; }
        public byte[]? ResultBytes { get; set; }
    }
}

