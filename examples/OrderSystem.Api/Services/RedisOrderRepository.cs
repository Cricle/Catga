using MemoryPack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

/// <summary>
/// Redis-based order repository for distributed cluster mode.
/// All orders are stored in Redis, enabling cross-node visibility.
/// </summary>
public class RedisOrderRepository : IOrderRepository
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisOrderRepository> _logger;
    private const string OrderPrefix = "order:";
    private const string CustomerOrdersPrefix = "customer-orders:";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    public RedisOrderRepository(IDistributedCache cache, ILogger<RedisOrderRepository> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await _cache.GetAsync($"{OrderPrefix}{orderId}", cancellationToken);
            if (bytes is { Length: > 0 })
            {
                return MemoryPackSerializer.Deserialize<Order>(bytes);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order {OrderId} from Redis", orderId);
            return null;
        }
    }

    public async ValueTask SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = MemoryPackSerializer.Serialize(order);
            await _cache.SetAsync($"{OrderPrefix}{order.OrderId}", bytes, CacheOptions, cancellationToken);

            // Also track by customer ID
            await AddToCustomerOrdersAsync(order.CustomerId, order.OrderId, cancellationToken);

            _logger.LogDebug("Saved order {OrderId} to Redis", order.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save order {OrderId} to Redis", order.OrderId);
            throw;
        }
    }

    public async ValueTask UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SaveAsync(order, cancellationToken);
    }

    public async ValueTask DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync($"{OrderPrefix}{orderId}", cancellationToken);
            _logger.LogDebug("Deleted order {OrderId} from Redis", orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete order {OrderId} from Redis", orderId);
        }
    }

    public async ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var orders = new List<Order>();
        try
        {
            var orderIdsBytes = await _cache.GetAsync($"{CustomerOrdersPrefix}{customerId}", cancellationToken);
            if (orderIdsBytes is { Length: > 0 })
            {
                var orderIds = MemoryPackSerializer.Deserialize<List<string>>(orderIdsBytes);
                if (orderIds != null)
                {
                    foreach (var orderId in orderIds)
                    {
                        var order = await GetByIdAsync(orderId, cancellationToken);
                        if (order != null)
                        {
                            orders.Add(order);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get orders for customer {CustomerId} from Redis", customerId);
        }
        return orders;
    }

    private async ValueTask AddToCustomerOrdersAsync(string customerId, string orderId, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"{CustomerOrdersPrefix}{customerId}";
            var orderIds = new List<string>();

            var existingBytes = await _cache.GetAsync(key, cancellationToken);
            if (existingBytes is { Length: > 0 })
            {
                orderIds = MemoryPackSerializer.Deserialize<List<string>>(existingBytes) ?? new List<string>();
            }

            if (!orderIds.Contains(orderId))
            {
                orderIds.Add(orderId);
                var bytes = MemoryPackSerializer.Serialize(orderIds);
                await _cache.SetAsync(key, bytes, CacheOptions, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add order {OrderId} to customer {CustomerId} list", orderId, customerId);
        }
    }
}
