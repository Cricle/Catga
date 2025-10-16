using Catga;
using Catga.Core;
using Catga.Messages;
using Catga.Results;
using MemoryPack;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Batch order operations - demonstrates BatchOperationExtensions usage for performance optimization
/// </summary>

/// <summary>Batch create orders command</summary>
[MemoryPackable]
public partial record BatchCreateOrdersCommand(
    List<OrderBatchItem> Orders
) : IRequest<BatchCreateOrdersResult>;

/// <summary>Single order item in batch</summary>
[MemoryPackable]
public partial record OrderBatchItem(
    string CustomerId,
    List<OrderItem> Items,
    string ShippingAddress,
    string PaymentMethod
);

/// <summary>Batch create result</summary>
[MemoryPackable]
public partial record BatchCreateOrdersResult(
    int SuccessCount,
    int FailureCount,
    List<string> CreatedOrderIds,
    List<string> Errors
);

/// <summary>Batch order handler - uses BatchOperationExtensions for optimized parallel processing</summary>
public class BatchCreateOrdersHandler : SafeRequestHandler<BatchCreateOrdersCommand, BatchCreateOrdersResult>
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<BatchCreateOrdersHandler> _logger;

    public BatchCreateOrdersHandler(
        IOrderRepository repository,
        ILogger<BatchCreateOrdersHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    protected override async Task<BatchCreateOrdersResult> HandleCoreAsync(
        BatchCreateOrdersCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing batch of {Count} orders", request.Orders.Count);

        var createdOrderIds = new List<string>();
        var errors = new List<string>();
        int successCount = 0;
        int failureCount = 0;

        // Use BatchOperationExtensions for optimized parallel processing
        try
        {
            await request.Orders.ExecuteBatchAsync(
                async orderItem =>
                {
                    try
                    {
                        // Create order
                        var order = Order.Create(
                            orderItem.CustomerId,
                            orderItem.Items,
                            orderItem.ShippingAddress,
                            orderItem.PaymentMethod);

                        // Save to repository
                        await _repository.SaveAsync(order, cancellationToken);

                        // Track success
                        lock (createdOrderIds)
                        {
                            createdOrderIds.Add(order.Id);
                            successCount++;
                        }

                        _logger.LogInformation(
                            "Order {OrderId} created for customer {CustomerId}",
                            order.Id, orderItem.CustomerId);
                    }
                    catch (Exception ex)
                    {
                        // Track failure
                        lock (errors)
                        {
                            errors.Add($"Customer {orderItem.CustomerId}: {ex.Message}");
                            failureCount++;
                        }

                        _logger.LogError(ex,
                            "Failed to create order for customer {CustomerId}",
                            orderItem.CustomerId);
                    }
                },
                maxConcurrency: 10,        // Process up to 10 orders in parallel
                batchSize: 100,            // Group into batches of 100
                arrayPoolThreshold: 50,    // Use ArrayPool for batches > 50
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Batch processing completed: {Success} succeeded, {Failed} failed",
                successCount, failureCount);

            return new BatchCreateOrdersResult(
                successCount,
                failureCount,
                createdOrderIds,
                errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processing failed");
            throw; // SafeRequestHandler will wrap this
        }
    }
}

/// <summary>
/// Batch query orders - demonstrates read optimization
/// </summary>
[MemoryPackable]
public partial record BatchGetOrdersQuery(
    List<string> OrderIds
) : IRequest<List<Order?>>;

/// <summary>Batch get orders handler</summary>
public class BatchGetOrdersHandler : SafeRequestHandler<BatchGetOrdersQuery, List<Order?>>
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<BatchGetOrdersHandler> _logger;

    public BatchGetOrdersHandler(
        IOrderRepository repository,
        ILogger<BatchGetOrdersHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    protected override async Task<List<Order?>> HandleCoreAsync(
        BatchGetOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching batch of {Count} orders", request.OrderIds.Count);

        var orders = new List<Order?>(request.OrderIds.Count);

        try
        {
            // Use BatchOperationExtensions for parallel reads
            var results = await request.OrderIds.ExecuteBatchAsync(
                async orderId =>
                {
                    return await _repository.GetByIdAsync(orderId, cancellationToken);
                },
                maxConcurrency: 20,        // Higher concurrency for reads
                batchSize: 50,
                arrayPoolThreshold: 30,
                cancellationToken: cancellationToken);

            orders.AddRange(results);

            var foundCount = orders.Count(o => o != null);
            _logger.LogInformation(
                "Batch fetch completed: {Found}/{Total} orders found",
                foundCount, request.OrderIds.Count);

            return orders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch fetch failed");
            throw; // SafeRequestHandler will wrap this
        }
    }
}

