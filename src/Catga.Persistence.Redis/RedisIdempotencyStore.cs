using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Catga.Observability;
using Catga.Resilience;
using MemoryPack;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>Redis idempotency store.</summary>
public partial class RedisIdempotencyStore(
    IConnectionMultiplexer redis,
    IMessageSerializer serializer,
    IResiliencePipelineProvider provider,
    IOptions<RedisPersistenceOptions>? options = null) : RedisStoreBase(redis, serializer, options?.Value.IdempotencyKeyPrefix ?? "catga:idempotency:"), IIdempotencyStore
{
    private readonly TimeSpan _expiry = options?.Value.IdempotencyExpiry ?? TimeSpan.FromHours(24);

    public async Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async c =>
        {
            using var activity = MetricsHelper.StartPersistenceActivity("Redis.Idempotency", "HasBeenProcessed");
            var exists = await GetDatabase().KeyExistsAsync(BuildKey(messageId));
            MetricsHelper.RecordIdempotency(exists);
            return exists;
        }, ct);

    public async Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId, TResult? result = default, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async c =>
        {
            using var activity = MetricsHelper.StartPersistenceActivity("Redis.Idempotency", "MarkProcessed");
            var entry = new Entry { MessageId = messageId, ProcessedAt = DateTime.UtcNow, ResultType = result?.GetType().AssemblyQualifiedName, ResultBytes = result != null ? Serializer.Serialize(result, typeof(TResult)) : null };
            await GetDatabase().StringSetAsync(BuildKey(messageId), Serializer.Serialize(entry, typeof(Entry)), _expiry);
        }, ct);

    public async Task<TResult?> GetCachedResultAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(long messageId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async c =>
        {
            using var activity = MetricsHelper.StartPersistenceActivity("Redis.Idempotency", "GetCachedResult");
            var bytes = await GetDatabase().StringGetAsync(BuildKey(messageId));
            if (!bytes.HasValue) { MetricsHelper.RecordIdempotencyMiss(); return default(TResult?); }
            var entry = (Entry?)Serializer.Deserialize((byte[])bytes!, typeof(Entry));
            if (entry?.ResultBytes == null) { MetricsHelper.RecordIdempotencyMiss(); return default(TResult?); }
            if (entry.ResultType != typeof(TResult).AssemblyQualifiedName) { MetricsHelper.RecordIdempotencyMiss(); return default(TResult?); }
            var result = (TResult?)Serializer.Deserialize(entry.ResultBytes, typeof(TResult));
            MetricsHelper.RecordIdempotency(result is not null);
            return result;
        }, ct);

    [MemoryPackable]
    private partial class Entry { public long MessageId { get; set; } public DateTime ProcessedAt { get; set; } public string? ResultType { get; set; } public byte[]? ResultBytes { get; set; } }
}
