using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catga.Idempotency;

/// <summary>
/// High-performance sharded idempotency store using ConcurrentDictionary (AOT-compatible)
/// Reduces lock contention by sharding across multiple partitions
/// Uses lazy cleanup strategy - cleans up expired entries on access
/// </summary>
public class ShardedIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (DateTime ProcessedAt, Type? ResultType, string? ResultJson)>[] _shards;
    private readonly TimeSpan _retentionPeriod;
    private readonly int _shardCount;
    private readonly JsonSerializerOptions _jsonOptions;
    private long _lastCleanupTicks;

    public ShardedIdempotencyStore(int shardCount = 32, TimeSpan? retentionPeriod = null)
    {
        if (shardCount <= 0 || (shardCount & (shardCount - 1)) != 0)
            throw new ArgumentException("Shard count must be a power of 2", nameof(shardCount));

        _shardCount = shardCount;
        _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _shards = new ConcurrentDictionary<string, (DateTime, Type?, string?)>[_shardCount];
        for (int i = 0; i < _shardCount; i++)
        {
            _shards[i] = new ConcurrentDictionary<string, (DateTime, Type?, string?)>();
        }

        _lastCleanupTicks = DateTime.UtcNow.Ticks;
    }

    private ConcurrentDictionary<string, (DateTime, Type?, string?)> GetShard(string messageId)
    {
        var hash = messageId.GetHashCode();
        var shardIndex = hash & (_shardCount - 1);
        return _shards[shardIndex];
    }

    public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        TryLazyCleanup(); // Lazy cleanup on access

        var shard = GetShard(messageId);
        if (shard.TryGetValue(messageId, out var entry))
        {
            // Check if expired
            if (DateTime.UtcNow - entry.Item1 > _retentionPeriod)
            {
                shard.TryRemove(messageId, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
    public Task MarkAsProcessedAsync<TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default)
    {
        var shard = GetShard(messageId);

        string? resultJson = null;
        Type? resultType = null;

        if (result != null)
        {
            resultType = typeof(TResult);
            resultJson = JsonSerializer.Serialize(result, _jsonOptions);
        }

        shard[messageId] = (DateTime.UtcNow, resultType, resultJson);
        return Task.CompletedTask;
    }

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
    public Task<TResult?> GetCachedResultAsync<TResult>(string messageId, CancellationToken cancellationToken = default)
    {
        var shard = GetShard(messageId);

        if (shard.TryGetValue(messageId, out var entry))
        {
            // Check if expired
            if (DateTime.UtcNow - entry.Item1 > _retentionPeriod)
            {
                shard.TryRemove(messageId, out _);
                return Task.FromResult<TResult?>(default);
            }

            if (entry.Item3 != null && entry.Item2 == typeof(TResult))
            {
                return Task.FromResult(JsonSerializer.Deserialize<TResult>(entry.Item3, _jsonOptions));
            }
        }

        return Task.FromResult<TResult?>(default);
    }

    /// <summary>
    /// Lazy cleanup: runs every 5 minutes to save resources
    /// No LINQ allocations - direct iteration for zero-GC cleanup
    /// </summary>
    private void TryLazyCleanup()
    {
        var now = DateTime.UtcNow.Ticks;
        var lastCleanup = Interlocked.Read(ref _lastCleanupTicks);

        // Cleanup every 5 minutes (300M ticks = 5 * 60 * 10M)
        if (now - lastCleanup < 3000000000L)
            return;

        // Try to acquire cleanup ownership (lock-free)
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now, lastCleanup) != lastCleanup)
            return;

        var cutoff = DateTime.UtcNow - _retentionPeriod;

        // Zero-allocation cleanup: direct iteration, no LINQ
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
