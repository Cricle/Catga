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
        if (shard.TryGetValue(messageId, out var entry))
        {
            if (ExpirationHelper.IsExpired(entry.Item1, _retentionPeriod))
            {
                shard.TryRemove(messageId, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task MarkAsProcessedAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default)
    {
        var shard = GetShard(messageId);
        shard[messageId] = (DateTime.UtcNow, null, null);

        if (result != null)
        {
            var resultJson = SerializationHelper.SerializeJson(result);
            TypedIdempotencyCache<TResult>.Cache[messageId] = (DateTime.UtcNow, resultJson);
        }
        return Task.CompletedTask;
    }

    public Task<TResult?> GetCachedResultAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, CancellationToken cancellationToken = default)
    {
        if (TypedIdempotencyCache<TResult>.Cache.TryGetValue(messageId, out var entry))
        {
            if (ExpirationHelper.IsExpired(entry.Timestamp, _retentionPeriod))
            {
                TypedIdempotencyCache<TResult>.Cache.TryRemove(messageId, out _);
                return Task.FromResult<TResult?>(default);
            }
            return Task.FromResult(SerializationHelper.DeserializeJson<TResult>(entry.Json));
        }
        return Task.FromResult<TResult?>(default);
    }

    private void TryLazyCleanup()
    {
        var now = DateTime.UtcNow.Ticks;
        var lastCleanup = Interlocked.Read(ref _lastCleanupTicks);
        if (now - lastCleanup < 3000000000L) return;
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now, lastCleanup) != lastCleanup) return;

        var cutoff = DateTime.UtcNow - _retentionPeriod;
        foreach (var shard in _shards)
        {
            foreach (var kvp in shard)
            {
                if (kvp.Value.Item1 < cutoff)
                    shard.TryRemove(kvp.Key, out _);
            }
        }
    }
}
