using System.Runtime.CompilerServices;
using ZiggyCreatures.Caching.Fusion;

namespace Catga.Idempotency;

/// <summary>
/// FusionCache-based idempotency store for QoS 2 (Exactly Once).
/// Uses FusionCache for automatic expiration and memory management.
/// </summary>
internal sealed class FusionCacheIdempotencyStore
{
    private readonly IFusionCache _cache;
    private readonly FusionCacheEntryOptions _options;
    private const string KeyPrefix = "idempotency:";

    public FusionCacheIdempotencyStore(IFusionCache cache, TimeSpan? retentionPeriod = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = new FusionCacheEntryOptions
        {
            Duration = retentionPeriod ?? TimeSpan.FromHours(24),
            Priority = CacheItemPriority.High // Idempotency is critical
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetKey(string messageId) => $"{KeyPrefix}{messageId}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsProcessed(string messageId)
        => _cache.TryGet<bool>(GetKey(messageId), out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkAsProcessed(string messageId)
        => _cache.Set(GetKey(messageId), true, _options);
}

