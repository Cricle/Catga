using MemoryPack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Infrastructure.Caching;

public class CachedOrderRepository : IOrderRepository
{
    private readonly IOrderRepository _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedOrderRepository> _logger;
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
    };

    public CachedOrderRepository(IOrderRepository inner, IDistributedCache cache, ILogger<CachedOrderRepository> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"order:{orderId}";
        try
        {
            var bytes = await _cache.GetAsync(cacheKey, cancellationToken);
            if (bytes is { Length: > 0 })
            {
                _logger.LogDebug("Cache hit for order {OrderId}", orderId);
                return MemoryPackSerializer.Deserialize<Order>(bytes);
            }

            var order = await _inner.GetByIdAsync(orderId, cancellationToken);
            if (order != null)
            {
                var data = MemoryPackSerializer.Serialize(order);
                await _cache.SetAsync(cacheKey, data, CacheOptions, cancellationToken);
            }
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accessing cache for order {OrderId}", orderId);
            // Fall back to inner repository on cache failure
            return await _inner.GetByIdAsync(orderId, cancellationToken);
        }
    }

    public async ValueTask SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _inner.SaveAsync(order, cancellationToken);
        await InvalidateCacheAsync(order.OrderId, cancellationToken);
    }

    public async ValueTask UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _inner.UpdateAsync(order, cancellationToken);
        await InvalidateCacheAsync(order.OrderId, cancellationToken);
    }

    public async ValueTask DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        await _inner.DeleteAsync(orderId, cancellationToken);
        await InvalidateCacheAsync(orderId, cancellationToken);
    }

    private async ValueTask InvalidateCacheAsync(string orderId, CancellationToken cancellationToken)
    {
        var cacheKey = $"order:{orderId}";
        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogDebug("Invalidated cache for order {OrderId}", orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache for order {OrderId}", orderId);
        }
    }
}
