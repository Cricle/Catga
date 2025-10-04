using System.Collections.Concurrent;
using Catga.CatGa.Models;

namespace Catga.CatGa.Repository;

/// <summary>
/// 内存实现的 CatGa 仓储 - 高性能分片设计
/// Uses lazy cleanup strategy - cleans up expired entries on access
/// </summary>
public sealed class InMemoryCatGaRepository : ICatGaRepository
{
    private readonly ConcurrentDictionary<string, (DateTime ExpireAt, object? Result)>[] _idempotencyShards;
    private readonly ConcurrentDictionary<string, CatGaContext> _contextStore;
    private readonly int _shardCount;
    private readonly TimeSpan _expiry;
    private long _lastCleanupTicks;

    public InMemoryCatGaRepository(int shardCount = 128, TimeSpan? expiry = null)
    {
        if (shardCount <= 0 || (shardCount & (shardCount - 1)) != 0)
            throw new ArgumentException("Shard count must be a power of 2", nameof(shardCount));

        _shardCount = shardCount;
        _expiry = expiry ?? TimeSpan.FromHours(1);

        _idempotencyShards = new ConcurrentDictionary<string, (DateTime, object?)>[_shardCount];
        for (int i = 0; i < _shardCount; i++)
        {
            _idempotencyShards[i] = new ConcurrentDictionary<string, (DateTime, object?)>();
        }

        _contextStore = new ConcurrentDictionary<string, CatGaContext>();
        _lastCleanupTicks = DateTime.UtcNow.Ticks;
    }

    public bool IsProcessed(string idempotencyKey)
    {
        TryLazyCleanup(); // Lazy cleanup on access

        var shard = GetIdempotencyShard(idempotencyKey);
        if (shard.TryGetValue(idempotencyKey, out var entry))
        {
            if (entry.Item1 > DateTime.UtcNow)
                return true;

            // 过期，删除
            shard.TryRemove(idempotencyKey, out _);
        }
        return false;
    }

    public void MarkProcessed(string idempotencyKey)
    {
        var shard = GetIdempotencyShard(idempotencyKey);
        shard[idempotencyKey] = (DateTime.UtcNow.Add(_expiry), null);
    }

    public void CacheResult<T>(string idempotencyKey, T? result)
    {
        var shard = GetIdempotencyShard(idempotencyKey);
        shard[idempotencyKey] = (DateTime.UtcNow.Add(_expiry), result);
    }

    public bool TryGetCachedResult<T>(string idempotencyKey, out T? result)
    {
        var shard = GetIdempotencyShard(idempotencyKey);
        if (shard.TryGetValue(idempotencyKey, out var entry))
        {
            if (entry.Item1 > DateTime.UtcNow && entry.Item2 is T typedResult)
            {
                result = typedResult;
                return true;
            }

            // 过期或类型不匹配
            shard.TryRemove(idempotencyKey, out _);
        }

        result = default;
        return false;
    }

    public Task SaveContextAsync<TRequest, TResponse>(
        string transactionId,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default)
    {
        _contextStore[transactionId] = context;
        return Task.CompletedTask;
    }

    public Task<CatGaContext?> LoadContextAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        _contextStore.TryGetValue(transactionId, out var context);
        return Task.FromResult(context);
    }

    public Task DeleteContextAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        _contextStore.TryRemove(transactionId, out _);
        return Task.CompletedTask;
    }

    private ConcurrentDictionary<string, (DateTime, object?)> GetIdempotencyShard(string key)
    {
        var hash = key.GetHashCode();
        var shardIndex = (hash & int.MaxValue) % _shardCount;
        return _idempotencyShards[shardIndex];
    }

    /// <summary>
    /// Lazy cleanup: only runs every minute to save resources
    /// Zero-allocation cleanup: direct iteration, no LINQ
    /// </summary>
    private void TryLazyCleanup()
    {
        var now = DateTime.UtcNow.Ticks;
        var lastCleanup = Interlocked.Read(ref _lastCleanupTicks);

        // Only cleanup every minute (600M ticks = 60 * 10M)
        if (now - lastCleanup < 600000000L)
            return;

        // Try to acquire cleanup ownership (lock-free)
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now, lastCleanup) != lastCleanup)
            return;

        var cutoff = DateTime.UtcNow;

        // Zero-allocation cleanup: direct iteration, no LINQ
        foreach (var shard in _idempotencyShards)
        {
            foreach (var kvp in shard)
            {
                if (kvp.Value.Item1 <= cutoff)
                    shard.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        // No resources to dispose - Timer removed
    }
}
