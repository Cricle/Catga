using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Infrastructure.Telemetry;

namespace OrderSystem.Api.Services;

public class InMemoryOrderRepository : IOrderRepository, IDisposable
{
    private static readonly Counter<long> _operationCounter = Telemetry.Meter.CreateCounter<long>("order_operations", "number", "Operation type");
    private static readonly Histogram<double> _operationDuration = Telemetry.Meter.CreateHistogram<double>("order_operation_duration", "ms", "Operation duration in milliseconds");
    private static readonly KeyValuePair<string, object?>[] TagGet = new[] { new KeyValuePair<string, object?>("operation", "get") };
    private static readonly KeyValuePair<string, object?>[] TagSave = new[] { new KeyValuePair<string, object?>("operation", "save") };
    private static readonly KeyValuePair<string, object?>[] TagUpdate = new[] { new KeyValuePair<string, object?>("operation", "update") };
    private static readonly KeyValuePair<string, object?>[] TagDelete = new[] { new KeyValuePair<string, object?>("operation", "delete") };

    private readonly ILogger<InMemoryOrderRepository> _logger;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, Order> _orders = new();
    private bool _disposed;

    public InMemoryOrderRepository(ILogger<InMemoryOrderRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("In-memory order repository initialized");
    }

    public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GetOrderById");
        using var timer = _operationDuration.Measure();

        _operationCounter.Add(1, TagGet);

        try
        {
            _lock.EnterReadLock();
            cancellationToken.ThrowIfCancellationRequested();

            var exists = _orders.TryGetValue(orderId, out var order);
            _logger.LogDebug("Get order {OrderId} - {Status}", orderId, exists ? "Found" : "Not Found");

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
        using var activity = Telemetry.ActivitySource.StartActivity("SaveOrder");
        using var timer = _operationDuration.Measure();

        _operationCounter.Add(1, TagSave);

        try
        {
            _lock.EnterWriteLock();
            cancellationToken.ThrowIfCancellationRequested();

            _orders[order.OrderId] = order;
            _logger.LogInformation("Order {OrderId} saved successfully", order.OrderId);

            return ValueTask.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ValueTask UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("UpdateOrder");
        using var timer = _operationDuration.Measure();

        _operationCounter.Add(1, TagUpdate);

        try
        {
            _lock.EnterWriteLock();
            cancellationToken.ThrowIfCancellationRequested();

            if (!_orders.ContainsKey(order.OrderId))
            {
                _logger.LogWarning("Attempted to update non-existent order {OrderId}", order.OrderId);
                throw new InvalidOperationException($"Order with ID {order.OrderId} not found");
            }

            _orders[order.OrderId] = order;
            _logger.LogInformation("Order {OrderId} updated successfully", order.OrderId);

            return ValueTask.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ValueTask DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("DeleteOrder");
        using var timer = _operationDuration.Measure();

        _operationCounter.Add(1, TagDelete);

        try
        {
            _lock.EnterWriteLock();
            cancellationToken.ThrowIfCancellationRequested();

            if (_orders.Remove(orderId, out _))
            {
                _logger.LogInformation("Order {OrderId} deleted successfully", orderId);
            }
            else
            {
                _logger.LogWarning("Attempted to delete non-existent order {OrderId}", orderId);
            }

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

