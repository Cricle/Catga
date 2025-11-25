using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Catga.Resilience;
using System.Diagnostics;
using Catga.Observability;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis idempotency store - production-grade high-performance (uses injected IMessageSerializer)
/// </summary>
public class RedisIdempotencyStore : RedisStoreBase, IIdempotencyStore
{
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private readonly TimeSpan _defaultExpiry;
    private readonly IResiliencePipelineProvider _provider;

    public RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        ILogger<RedisIdempotencyStore> logger,
        RedisIdempotencyOptions? options = null,
        IResiliencePipelineProvider? provider = null)
        : base(redis, serializer, options?.KeyPrefix ?? "idempotency:")
    {
        _logger = logger;
        _defaultExpiry = options?.Expiry ?? TimeSpan.FromHours(24);
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc/>
    public async Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Idempotency.HasBeenProcessed", ActivityKind.Internal);
            var db = GetDatabase();
            var key = BuildKey(messageId);
            var exists = await db.KeyExistsAsync(key);
            if (exists) CatgaDiagnostics.IdempotencyHits.Add(1); else CatgaDiagnostics.IdempotencyMisses.Add(1);
            return exists;
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        TResult? result = default,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Idempotency.MarkProcessed", ActivityKind.Internal);
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
            CatgaDiagnostics.IdempotencyMarked.Add(1);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult?> GetCachedResultAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Idempotency.GetCachedResult", ActivityKind.Internal);
            var db = GetDatabase();
            var key = BuildKey(messageId);

            var bytes = await db.StringGetAsync(key);
            if (!bytes.HasValue)
            {
                CatgaDiagnostics.IdempotencyCacheMisses.Add(1);
                return default(TResult?);
            }

            var entry = (IdempotencyEntry?)Serializer.Deserialize((byte[])bytes!, typeof(IdempotencyEntry));
            if (entry?.ResultBytes == null)
            {
                CatgaDiagnostics.IdempotencyCacheMisses.Add(1);
                return default(TResult?);
            }

            // Verify type match
            var expectedType = typeof(TResult).AssemblyQualifiedName;
            if (entry.ResultType != expectedType)
            {
                _logger.LogWarning("Type mismatch for message {MessageId}: expected {Expected}, got {Actual}",
                    messageId, expectedType, entry.ResultType);
                CatgaDiagnostics.IdempotencyCacheMisses.Add(1);
                return default(TResult?);
            }

            var result = (TResult?)Serializer.Deserialize(entry.ResultBytes, typeof(TResult));
            if (result is null) CatgaDiagnostics.IdempotencyCacheMisses.Add(1); else CatgaDiagnostics.IdempotencyCacheHits.Add(1);
            return result;
        }, cancellationToken);
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

