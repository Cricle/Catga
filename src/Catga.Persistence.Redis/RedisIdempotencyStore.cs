using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis idempotency store - production-grade high-performance (uses injected IMessageSerializer)
/// </summary>
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultExpiry;

    public RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        ILogger<RedisIdempotencyStore> logger,
        RedisIdempotencyOptions? options = null)
    {
        _redis = redis;
        _serializer = serializer;
        _logger = logger;
        _keyPrefix = options?.KeyPrefix ?? "idempotency:";
        _defaultExpiry = options?.Expiry ?? TimeSpan.FromHours(24);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(messageId);
        return await db.KeyExistsAsync(key);
    }

    /// <inheritdoc/>
    public async Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
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
            ResultBytes = result != null ? _serializer.Serialize(result) : null
        };

        var bytes = _serializer.Serialize(entry);
        await db.StringSetAsync(key, bytes, _defaultExpiry);

        _logger.LogDebug("Marked message {MessageId} as processed in Redis", messageId);
    }

    /// <inheritdoc/>
    public async Task<TResult?> GetCachedResultAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(messageId);

        var bytes = await db.StringGetAsync(key);
        if (!bytes.HasValue)
        {
            return default;
        }

        var entry = _serializer.Deserialize<IdempotencyEntry>((byte[])bytes!);
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

        return _serializer.Deserialize<TResult>(entry.ResultBytes);
    }

    // Optimize: Use Span to avoid string interpolation allocation
    private string GetKey(string messageId)
    {
        // For small keys, use stack allocation
        if (_keyPrefix.Length + messageId.Length <= 256)
        {
            Span<char> buffer = stackalloc char[256];
            _keyPrefix.AsSpan().CopyTo(buffer);
            messageId.AsSpan().CopyTo(buffer[_keyPrefix.Length..]);
            return new string(buffer[..(_keyPrefix.Length + messageId.Length)]);
        }

        // Fallback for large keys
        return $"{_keyPrefix}{messageId}";
    }

    /// <summary>
    /// Idempotency entry
    /// </summary>
    private class IdempotencyEntry
    {
        public string MessageId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public string? ResultType { get; set; }
        public byte[]? ResultBytes { get; set; }
    }
}

