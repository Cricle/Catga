using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Catga.Core;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Net.Http;

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

public class MockInventoryService : IInventoryService, IDisposable
{
    private static readonly Counter<long> _inventoryOperationCounter = Telemetry.Meter.CreateCounter<long>("inventory_operations", "number", "Operation type");
    private static readonly Histogram<double> _inventoryOperationDuration = Telemetry.Meter.CreateHistogram<double>("inventory_operation_duration", "ms", "Operation duration in milliseconds");
    private static readonly KeyValuePair<string, object?>[] TagCheckStock = new[] { new KeyValuePair<string, object?>("operation", "check_stock") };
    private static readonly KeyValuePair<string, object?>[] TagReserveStock = new[] { new KeyValuePair<string, object?>("operation", "reserve_stock") };
    private static readonly KeyValuePair<string, object?>[] TagReleaseStock = new[] { new KeyValuePair<string, object?>("operation", "release_stock") };

    private readonly ILogger<MockInventoryService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Random _random = new();
    private bool _disposed;

    public MockInventoryService(HttpClient httpClient, ILogger<MockInventoryService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Mock inventory service initialized");
    }

    public async ValueTask<CatgaResult> CheckStockAsync(List<OrderItem> items, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("CheckStock");
        using var timer = _inventoryOperationDuration.Measure();

        _inventoryOperationCounter.Add(1, TagCheckStock);

        try
        {
            // Simulate network/database latency
            await Task.Delay(_random.Next(50, 200), cancellationToken);

            // Randomly fail 5% of the time to test resilience
            if (_random.NextDouble() < 0.05)
            {
                _logger.LogWarning("Stock check failed for {ItemCount} items", items.Count);
                return CatgaResult.Failure("Failed to check inventory");
            }

            _logger.LogDebug("Stock checked for {ItemCount} items", items.Count);
            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking stock for {ItemCount} items", items.Count);
            return CatgaResult.Failure($"Stock check failed: {ex.Message}");
        }
    }

    public async ValueTask<CatgaResult> ReserveStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ReserveStock");
        using var timer = _inventoryOperationDuration.Measure();

        _inventoryOperationCounter.Add(1, TagReserveStock);

        try
        {
            // Simulate network/database latency
            await Task.Delay(_random.Next(100, 300), cancellationToken);

            // Randomly fail 3% of the time to test resilience
            if (_random.NextDouble() < 0.03)
            {
                _logger.LogWarning("Stock reservation failed for order {OrderId}", orderId);
                return CatgaResult.Failure("Failed to reserve inventory");
            }

            _logger.LogInformation("Stock reserved for order {OrderId} with {ItemCount} items", orderId, items.Count);
            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reserving stock for order {OrderId}", orderId);
            return CatgaResult.Failure($"Stock reservation failed: {ex.Message}");
        }
    }

    public async ValueTask<CatgaResult> ReleaseStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ReleaseStock");
        using var timer = _inventoryOperationDuration.Measure();

        _inventoryOperationCounter.Add(1, TagReleaseStock);

        try
        {
            // Simulate network/database latency
            await Task.Delay(_random.Next(50, 150), cancellationToken);

            _logger.LogInformation("Stock released for order {OrderId} with {ItemCount} items", orderId, items.Count);
            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing stock for order {OrderId}", orderId);
            return CatgaResult.Failure($"Failed to release stock: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

}

public class MockPaymentService : IPaymentService, IDisposable
{
    private static readonly Counter<long> _paymentCounter = Telemetry.Meter.CreateCounter<long>("payment_operations", "number", "Operation type");
    private static readonly Histogram<double> _paymentDuration = Telemetry.Meter.CreateHistogram<double>("payment_operation_duration", "ms", "Payment processing duration in milliseconds");
    private static readonly KeyValuePair<string, object?>[] TagProcessPayment = new[] { new KeyValuePair<string, object?>("operation", "process_payment") };

    private readonly ILogger<MockPaymentService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Random _random = new();
    private bool _disposed;

    public MockPaymentService(HttpClient httpClient, ILogger<MockPaymentService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Mock payment service initialized");
    }

    public async ValueTask<CatgaResult> ProcessPaymentAsync(string orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("ProcessPayment");
        using var timer = _paymentDuration.Measure();

        _paymentCounter.Add(1, TagProcessPayment);

        try
        {
            // Simulate payment processing time
            await Task.Delay(_random.Next(100, 500), cancellationToken);

            // Randomly fail 2% of the time to test resilience
            if (_random.NextDouble() < 0.02)
            {
                _logger.LogWarning("Payment failed for order {OrderId} with {Amount:C}", orderId, amount);
                return CatgaResult.Failure("Payment processing failed");
            }

            // Special case for testing failure scenarios
            if (paymentMethod?.Contains("fail", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Payment method {PaymentMethod} is configured to fail", paymentMethod);
                return CatgaResult.Failure($"Payment method {paymentMethod} is not supported");
            }

            _logger.LogInformation("Successfully processed payment of {Amount:C} for order {OrderId} using {PaymentMethod}",
                amount, orderId, paymentMethod);

            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment for order {OrderId}", orderId);
            return CatgaResult.Failure($"Payment processing error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
