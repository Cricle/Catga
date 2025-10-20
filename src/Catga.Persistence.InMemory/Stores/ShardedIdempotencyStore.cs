using System.Collections.Concurrent;
using System.Text;
using Catga.Abstractions;
using Catga.Common;

namespace Catga.Idempotency;

/// <summary>High-performance sharded idempotency store (lock-free)</summary>
public class ShardedIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<long, (DateTime ProcessedAt, Type? ResultType, byte[]? ResultData)>[] _shards;
    private readonly IMessageSerializer _serializer;
    private readonly TimeSpan _retentionPeriod;
    private readonly int _shardCount;
    private long _lastCleanupTicks;

    public ShardedIdempotencyStore(
        IMessageSerializer serializer,
        int shardCount = 32, 
        TimeSpan? retentionPeriod = null)
    {
        if (shardCount <= 0 || (shardCount & (shardCount - 1)) != 0)
            throw new ArgumentException("Shard count must be a power of 2", nameof(shardCount));

        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _shardCount = shardCount;
        _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);
        _shards = new ConcurrentDictionary<long, (DateTime, Type?, byte[]?)>[_shardCount];
        for (int i = 0; i < _shardCount; i++)
            _shards[i] = new ConcurrentDictionary<long, (DateTime, Type?, byte[]?)>();
        _lastCleanupTicks = DateTime.UtcNow.Ticks;
    }

    private ConcurrentDictionary<long, (DateTime, Type?, byte[]?)> GetShard(long messageId)
        => _shards[messageId.GetHashCode() & (_shardCount - 1)];

    public Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        TryLazyCleanup();
        var shard = GetShard(messageId);
        if (shard.TryGetValue(messageId, out var entry) && !ExpirationHelper.IsExpired(entry.Item1, _retentionPeriod))
            return Task.FromResult(true);

        shard.TryRemove(messageId, out _);
        return Task.FromResult(false);
    }

    public Task MarkAsProcessedAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(long messageId, TResult? result = default, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        GetShard(messageId)[messageId] = (now, typeof(TResult), null);

        // Store typed result (null stored as empty byte array to differentiate from "not found")
        var resultData = result != null ? _serializer.Serialize(result) : Array.Empty<byte>();
        TypedIdempotencyCache<TResult>.Cache[messageId] = (now, resultData);

        return Task.CompletedTask;
    }

    public Task<TResult?> GetCachedResultAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(long messageId, CancellationToken cancellationToken = default)
    {
        if (TypedIdempotencyCache<TResult>.Cache.TryGetValue(messageId, out var entry))
        {
            if (ExpirationHelper.IsExpired(entry.Timestamp, _retentionPeriod))
            {
                TypedIdempotencyCache<TResult>.Cache.TryRemove(messageId, out _);
                GetShard(messageId).TryRemove(messageId, out _); // Also remove from main shard
                return Task.FromResult<TResult?>(default);
            }

            // Empty byte array means null result was explicitly stored
            if (entry.Data == null || entry.Data.Length == 0)
                return Task.FromResult<TResult?>(default);

            return Task.FromResult(_serializer.Deserialize<TResult>(entry.Data));
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
