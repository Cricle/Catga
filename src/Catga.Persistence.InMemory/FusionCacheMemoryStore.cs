using System.Runtime.CompilerServices;
using ZiggyCreatures.Caching.Fusion;

namespace Catga.Persistence;

/// <summary>
/// Base class for FusionCache-based in-memory stores.
/// Provides automatic expiration, memory management, and high performance.
/// </summary>
public abstract class FusionCacheMemoryStore<TMessage> where TMessage : class
{
    protected readonly IFusionCache Cache;
    protected readonly FusionCacheEntryOptions DefaultOptions;

    protected FusionCacheMemoryStore(IFusionCache cache, TimeSpan? defaultExpiration = null)
    {
        Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        DefaultOptions = new FusionCacheEntryOptions
        {
            Duration = defaultExpiration ?? TimeSpan.FromHours(24),
            Priority = CacheItemPriority.Normal
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string GetKey(string messageId) => $"{typeof(TMessage).Name}:{messageId}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask SetAsync(string messageId, TMessage message, CancellationToken cancellationToken = default)
    {
        Cache.Set(GetKey(messageId), message, DefaultOptions);
        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask SetAsync(string messageId, TMessage message, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var options = new FusionCacheEntryOptions { Duration = expiration };
        Cache.Set(GetKey(messageId), message, options);
        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask<TMessage?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var value = Cache.TryGet<TMessage>(GetKey(messageId), out var result) ? result : null;
        return ValueTask.FromResult(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var exists = Cache.TryGet<TMessage>(GetKey(messageId), out _);
        return ValueTask.FromResult(exists);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask RemoveAsync(string messageId, CancellationToken cancellationToken = default)
    {
        Cache.Remove(GetKey(messageId));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Get or set a value atomically using FusionCache's factory pattern
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask<TMessage> GetOrSetAsync(
        string messageId,
        Func<CancellationToken, TMessage> factory,
        CancellationToken cancellationToken = default)
    {
        var value = Cache.GetOrSet(GetKey(messageId), _ => factory(cancellationToken), DefaultOptions);
        return ValueTask.FromResult(value);
    }
}

