using Catga;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

namespace SimpleWebApi.Sagas;

// ============================================================================
// Saga Example: Order Processing with Compensation
// ============================================================================
// This example demonstrates a distributed transaction using the Saga pattern.
// Steps: Reserve Inventory -> Process Payment -> Confirm Order
// If any step fails, compensation actions are executed in reverse order.

#region Saga Messages

/// <summary>
/// Command to start order processing saga
/// </summary>
public record ProcessOrderSagaCommand : IRequest<ProcessOrderSagaResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string ProductId { get; init; }
    public required int Quantity { get; init; }
    public required decimal Amount { get; init; }
}

public record ProcessOrderSagaResponse
{
    public required string OrderId { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
}

// Saga step commands (public for source generator)
public record ReserveInventoryCommand : IRequest<ReserveInventoryResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string OrderId { get; init; }
    public required string ProductId { get; init; }
    public required int Quantity { get; init; }
}

public record ReserveInventoryResponse
{
    public required bool Success { get; init; }
    public required string ReservationId { get; init; }
}

public record ProcessPaymentCommand : IRequest<ProcessPaymentResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

public record ProcessPaymentResponse
{
    public required bool Success { get; init; }
    public required string TransactionId { get; init; }
}

public record ConfirmOrderCommand : IRequest<ConfirmOrderResponse>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string OrderId { get; init; }
}

public record ConfirmOrderResponse
{
    public required bool Success { get; init; }
}

// Compensation commands
public record CancelInventoryReservationCommand : IRequest
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string ReservationId { get; init; }
}

public record RefundPaymentCommand : IRequest
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public required string TransactionId { get; init; }
}

#endregion

#region Saga Orchestrator

/// <summary>
/// Saga orchestrator for order processing
/// Coordinates all steps and handles compensation
/// </summary>
public class ProcessOrderSaga : IRequestHandler<ProcessOrderSagaCommand, ProcessOrderSagaResponse>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ProcessOrderSaga> _logger;

    public ProcessOrderSaga(ICatgaMediator mediator, ILogger<ProcessOrderSaga> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<ProcessOrderSagaResponse>> HandleAsync(
        ProcessOrderSagaCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting order saga for OrderId: {OrderId}", request.OrderId);

        string? reservationId = null;
        string? transactionId = null;

        try
        {
            // Step 1: Reserve Inventory
            _logger.LogInformation("Step 1/3: Reserving inventory...");
            var inventoryResult = await _mediator.SendAsync<ReserveInventoryCommand, ReserveInventoryResponse>(new ReserveInventoryCommand
            {
                OrderId = request.OrderId,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            }, cancellationToken);

            if (!inventoryResult.IsSuccess || inventoryResult.Value == null || !inventoryResult.Value.Success)
            {
                return CatgaResult<ProcessOrderSagaResponse>.Success(new ProcessOrderSagaResponse
                {
                    OrderId = request.OrderId,
                    Status = "Failed",
                    ErrorMessage = "Inventory reservation failed"
                });
            }

            reservationId = inventoryResult.Value!.ReservationId;
            _logger.LogInformation("Inventory reserved: {ReservationId}", reservationId);

            // Step 2: Process Payment
            _logger.LogInformation("Step 2/3: Processing payment...");
            var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, ProcessPaymentResponse>(new ProcessPaymentCommand
            {
                OrderId = request.OrderId,
                CustomerId = request.CustomerId,
                Amount = request.Amount
            }, cancellationToken);

            if (!paymentResult.IsSuccess || paymentResult.Value == null || !paymentResult.Value.Success)
            {
                // Compensate: Cancel inventory reservation
                _logger.LogWarning("Payment failed, compensating inventory reservation...");
                await CompensateInventory(reservationId, cancellationToken);

                return CatgaResult<ProcessOrderSagaResponse>.Success(new ProcessOrderSagaResponse
                {
                    OrderId = request.OrderId,
                    Status = "Failed",
                    ErrorMessage = "Payment processing failed"
                });
            }

            transactionId = paymentResult.Value!.TransactionId;
            _logger.LogInformation("Payment processed: {TransactionId}", transactionId);

            // Step 3: Confirm Order
            _logger.LogInformation("Step 3/3: Confirming order...");
            var confirmResult = await _mediator.SendAsync<ConfirmOrderCommand, ConfirmOrderResponse>(new ConfirmOrderCommand
            {
                OrderId = request.OrderId
            }, cancellationToken);

            if (!confirmResult.IsSuccess || confirmResult.Value == null || !confirmResult.Value.Success)
            {
                // Compensate: Refund payment and cancel inventory
                _logger.LogWarning("Order confirmation failed, compensating all steps...");
                await CompensatePayment(transactionId, cancellationToken);
                await CompensateInventory(reservationId, cancellationToken);

                return CatgaResult<ProcessOrderSagaResponse>.Success(new ProcessOrderSagaResponse
                {
                    OrderId = request.OrderId,
                    Status = "Failed",
                    ErrorMessage = "Order confirmation failed"
                });
            }

            _logger.LogInformation("Order saga completed successfully: {OrderId}", request.OrderId);

            return CatgaResult<ProcessOrderSagaResponse>.Success(new ProcessOrderSagaResponse
            {
                OrderId = request.OrderId,
                Status = "Completed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order saga failed with exception: {OrderId}", request.OrderId);

            // Compensate all completed steps
            if (transactionId != null)
            {
                await CompensatePayment(transactionId, cancellationToken);
            }
            if (reservationId != null)
            {
                await CompensateInventory(reservationId, cancellationToken);
            }

            return CatgaResult<ProcessOrderSagaResponse>.Failure(
                "Order saga failed",
                new Catga.Exceptions.CatgaException("Saga execution error", ex));
        }
    }

    private async Task CompensateInventory(string reservationId, CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.SendAsync(new CancelInventoryReservationCommand
            {
                ReservationId = reservationId
            }, cancellationToken);

            _logger.LogInformation("Inventory reservation cancelled: {ReservationId}", reservationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compensate inventory: {ReservationId}", reservationId);
            // In production, this should trigger a compensating transaction retry mechanism
        }
    }

    private async Task CompensatePayment(string transactionId, CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.SendAsync(new RefundPaymentCommand
            {
                TransactionId = transactionId
            }, cancellationToken);

            _logger.LogInformation("Payment refunded: {TransactionId}", transactionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compensate payment: {TransactionId}", transactionId);
            // In production, this should trigger a compensating transaction retry mechanism
        }
    }
}

#endregion

#region Step Handlers

/// <summary>
/// Handler for inventory reservation
/// Simulates checking and reserving inventory
/// </summary>
public class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand, ReserveInventoryResponse>
{
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(ILogger<ReserveInventoryHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<ReserveInventoryResponse>> HandleAsync(
        ReserveInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reserving inventory for Product: {ProductId}, Quantity: {Quantity}",
            request.ProductId, request.Quantity);

        // Simulate inventory check (in production, this would query a database)
        await Task.Delay(50, cancellationToken); // Simulate I/O

        // Simulate inventory availability (90% success rate)
        var success = Random.Shared.Next(100) < 90;

        if (success)
        {
            var reservationId = $"INV-{Guid.NewGuid():N}";
            _logger.LogInformation("Inventory reserved: {ReservationId}", reservationId);

            return CatgaResult<ReserveInventoryResponse>.Success(new ReserveInventoryResponse
            {
                Success = true,
                ReservationId = reservationId
            });
        }
        else
        {
            _logger.LogWarning("Insufficient inventory for Product: {ProductId}", request.ProductId);

            return CatgaResult<ReserveInventoryResponse>.Success(new ReserveInventoryResponse
            {
                Success = false,
                ReservationId = string.Empty
            });
        }
    }
}

/// <summary>
/// Handler for payment processing
/// Simulates payment gateway interaction
/// </summary>
public class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, ProcessPaymentResponse>
{
    private readonly ILogger<ProcessPaymentHandler> _logger;

    public ProcessPaymentHandler(ILogger<ProcessPaymentHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<ProcessPaymentResponse>> HandleAsync(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment for Customer: {CustomerId}, Amount: {Amount}",
            request.CustomerId, request.Amount);

        // Simulate payment gateway call
        await Task.Delay(100, cancellationToken);

        // Simulate payment success (95% success rate)
        var success = Random.Shared.Next(100) < 95;

        if (success)
        {
            var transactionId = $"TXN-{Guid.NewGuid():N}";
            _logger.LogInformation("Payment processed: {TransactionId}", transactionId);

            return CatgaResult<ProcessPaymentResponse>.Success(new ProcessPaymentResponse
            {
                Success = true,
                TransactionId = transactionId
            });
        }
        else
        {
            _logger.LogWarning("Payment declined for Customer: {CustomerId}", request.CustomerId);

            return CatgaResult<ProcessPaymentResponse>.Success(new ProcessPaymentResponse
            {
                Success = false,
                TransactionId = string.Empty
            });
        }
    }
}

/// <summary>
/// Handler for order confirmation
/// Finalizes the order
/// </summary>
public class ConfirmOrderHandler : IRequestHandler<ConfirmOrderCommand, ConfirmOrderResponse>
{
    private readonly ILogger<ConfirmOrderHandler> _logger;

    public ConfirmOrderHandler(ILogger<ConfirmOrderHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<ConfirmOrderResponse>> HandleAsync(
        ConfirmOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Confirming order: {OrderId}", request.OrderId);

        // Simulate order confirmation
        await Task.Delay(30, cancellationToken);

        // In production, this would save to database, send notifications, etc.

        _logger.LogInformation("Order confirmed: {OrderId}", request.OrderId);

        return CatgaResult<ConfirmOrderResponse>.Success(new ConfirmOrderResponse
        {
            Success = true
        });
    }
}

/// <summary>
/// Handler for inventory compensation
/// </summary>
public class CancelInventoryReservationHandler : IRequestHandler<CancelInventoryReservationCommand>
{
    private readonly ILogger<CancelInventoryReservationHandler> _logger;

    public CancelInventoryReservationHandler(ILogger<CancelInventoryReservationHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult> HandleAsync(
        CancelInventoryReservationCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling inventory reservation: {ReservationId}", request.ReservationId);

        // Simulate cancellation
        await Task.Delay(20, cancellationToken);

        _logger.LogInformation("Inventory reservation cancelled: {ReservationId}", request.ReservationId);

        return CatgaResult.Success();
    }
}

/// <summary>
/// Handler for payment compensation
/// </summary>
public class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand>
{
    private readonly ILogger<RefundPaymentHandler> _logger;

    public RefundPaymentHandler(ILogger<RefundPaymentHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult> HandleAsync(
        RefundPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refunding payment: {TransactionId}", request.TransactionId);

        // Simulate refund
        await Task.Delay(80, cancellationToken);

        _logger.LogInformation("Payment refunded: {TransactionId}", request.TransactionId);

        return CatgaResult.Success();
    }
}

#endregion

