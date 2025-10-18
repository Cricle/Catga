using System.Collections.Concurrent;
using Catga.Common;

namespace Catga.Idempotency;

/// <summary>High-performance sharded idempotency store (AOT-compatible, lock-free)</summary>
public class ShardedIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (DateTime ProcessedAt, Type? ResultType, string? ResultJson)>[] _shards;
    private readonly TimeSpan _retentionPeriod;
    private readonly int _shardCount;
    private long _lastCleanupTicks;

    public ShardedIdempotencyStore(int shardCount = 32, TimeSpan? retentionPeriod = null)
    {
        if (shardCount <= 0 || (shardCount & (shardCount - 1)) != 0)
            throw new ArgumentException("Shard count must be a power of 2", nameof(shardCount));

        _shardCount = shardCount;
        _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);
        _shards = new ConcurrentDictionary<string, (DateTime, Type?, string?)>[_shardCount];
        for (int i = 0; i < _shardCount; i++)
            _shards[i] = new ConcurrentDictionary<string, (DateTime, Type?, string?)>();
        _lastCleanupTicks = DateTime.UtcNow.Ticks;
    }

    private ConcurrentDictionary<string, (DateTime, Type?, string?)> GetShard(string messageId)
        => _shards[messageId.GetHashCode() & (_shardCount - 1)];

    public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        TryLazyCleanup();
        var shard = GetShard(messageId);
        if (shard.TryGetValue(messageId, out var entry) && !ExpirationHelper.IsExpired(entry.Item1, _retentionPeriod))
            return Task.FromResult(true);

        shard.TryRemove(messageId, out _);
        return Task.FromResult(false);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "InMemory store is for development/testing. Use Redis for production AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "InMemory store is for development/testing. Use Redis for production AOT.")]
    public Task MarkAsProcessedAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        GetShard(messageId)[messageId] = (now, typeof(TResult), null);

        // Store typed result (null stored as empty string to differentiate from "not found")
        var resultJson = result != null ? SerializationHelper.SerializeJson(result) : string.Empty;
        TypedIdempotencyCache<TResult>.Cache[messageId] = (now, resultJson);

        return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "InMemory store is for development/testing. Use Redis for production AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "InMemory store is for development/testing. Use Redis for production AOT.")]
    public Task<TResult?> GetCachedResultAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, CancellationToken cancellationToken = default)
    {
        if (TypedIdempotencyCache<TResult>.Cache.TryGetValue(messageId, out var entry))
        {
            if (ExpirationHelper.IsExpired(entry.Timestamp, _retentionPeriod))
            {
                TypedIdempotencyCache<TResult>.Cache.TryRemove(messageId, out _);
                GetShard(messageId).TryRemove(messageId, out _); // Also remove from main shard
                return Task.FromResult<TResult?>(default);
            }

            // Empty string means null result was explicitly stored
            if (string.IsNullOrEmpty(entry.Json))
                return Task.FromResult<TResult?>(default);

            return Task.FromResult(SerializationHelper.DeserializeJson<TResult>(entry.Json));
        }
        return Task.FromResult<TResult?>(default);
    }

    private void TryLazyCleanup()
    {
        var now = DateTime.UtcNow.Ticks;
        var lastCleanup = Interlocked.Read(ref _lastCleanupTicks);

        // Only cleanup every 5 minutes (3000000000L ticks = 300 seconds)
        if (now - lastCleanup < 3000000000L) return;

        // Try to acquire cleanup lock (CAS)
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now, lastCleanup) != lastCleanup) return;

        var cutoff = DateTime.UtcNow - _retentionPeriod;

        // Cleanup main shards
        foreach (var shard in _shards)
        {
            foreach (var kvp in shard)
            {
                if (kvp.Value.Item1 < cutoff)
                    shard.TryRemove(kvp.Key, out _);
            }
        }

        // Note: TypedIdempotencyCache<T> is cleaned up lazily when accessed (in GetCachedResultAsync)
        // This avoids reflection overhead in cleanup loop
    }
}
