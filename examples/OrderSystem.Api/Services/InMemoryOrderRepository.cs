using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

public class InMemoryOrderRepository : IOrderRepository, IDisposable
{
    private readonly ILogger<InMemoryOrderRepository> _logger;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, Order> _orders = new();
    private bool _disposed;

    public InMemoryOrderRepository(ILogger<InMemoryOrderRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            _lock.EnterReadLock();
            cancellationToken.ThrowIfCancellationRequested();
            var exists = _orders.TryGetValue(orderId, out var order);
            return ValueTask.FromResult(exists ? order : null);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            _lock.EnterReadLock();
            cancellationToken.ThrowIfCancellationRequested();
            var orders = _orders.Values.Where(o => o.CustomerId == customerId).ToList();
            return ValueTask.FromResult(orders);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public ValueTask SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            _lock.EnterWriteLock();
            cancellationToken.ThrowIfCancellationRequested();
            _orders[order.OrderId] = order;
            _logger.LogInformation("Order {OrderId} saved", order.OrderId);
            return ValueTask.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ValueTask UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            _lock.EnterWriteLock();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_orders.ContainsKey(order.OrderId))
                throw new InvalidOperationException($"Order {order.OrderId} not found");
            _orders[order.OrderId] = order;
            _logger.LogInformation("Order {OrderId} updated", order.OrderId);
            return ValueTask.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ValueTask DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            _lock.EnterWriteLock();
            cancellationToken.ThrowIfCancellationRequested();
            _orders.Remove(orderId, out _);
            return ValueTask.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
