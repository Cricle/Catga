using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Handler for ReserveInventoryCommand - demonstrates Flow compensation
/// </summary>
public sealed partial class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand, ReserveInventoryResult>
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(IInventoryService inventoryService, ILogger<ReserveInventoryHandler> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task<CatgaResult<ReserveInventoryResult>> HandleAsync(
        ReserveInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        LogReservingInventory(_logger, request.OrderId, request.Items.Count);

        var result = await _inventoryService.ReserveStockAsync(request.OrderId, request.Items, cancellationToken);

        if (!result.IsSuccess)
        {
            LogReservationFailed(_logger, request.OrderId, result.Error ?? "Unknown error");
            return CatgaResult<ReserveInventoryResult>.Failure(result.Error ?? "Failed to reserve inventory");
        }

        var reservationId = $"RES-{request.OrderId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        LogReservationSuccess(_logger, request.OrderId, reservationId);

        return CatgaResult<ReserveInventoryResult>.Success(new ReserveInventoryResult(reservationId));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "📦 Reserving inventory for order {OrderId}, {ItemCount} items")]
    static partial void LogReservingInventory(ILogger logger, string orderId, int itemCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "❌ Inventory reservation failed for order {OrderId}: {Error}")]
    static partial void LogReservationFailed(ILogger logger, string orderId, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "✅ Inventory reserved for order {OrderId}, reservation: {ReservationId}")]
    static partial void LogReservationSuccess(ILogger logger, string orderId, string reservationId);
}

/// <summary>
/// Handler for ReleaseInventoryCommand - compensation handler
/// </summary>
public sealed partial class ReleaseInventoryHandler : IRequestHandler<ReleaseInventoryCommand>
{
    private readonly ILogger<ReleaseInventoryHandler> _logger;

    public ReleaseInventoryHandler(ILogger<ReleaseInventoryHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult> HandleAsync(
        ReleaseInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        LogReleasingInventory(_logger, request.ReservationId);
        // In real implementation, would call _inventoryService.ReleaseStockAsync
        return Task.FromResult(CatgaResult.Success());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "🔄 COMPENSATION: Releasing inventory reservation {ReservationId}")]
    static partial void LogReleasingInventory(ILogger logger, string reservationId);
}

/// <summary>
/// Handler for ProcessPaymentCommand - demonstrates Flow compensation
/// </summary>
public sealed partial class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, ProcessPaymentResult>
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ProcessPaymentHandler> _logger;

    public ProcessPaymentHandler(IPaymentService paymentService, ILogger<ProcessPaymentHandler> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    public async Task<CatgaResult<ProcessPaymentResult>> HandleAsync(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        LogProcessingPayment(_logger, request.OrderId, request.Amount, request.PaymentMethod);

        // Simulate payment failure for demo
        if (request.PaymentMethod.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            LogPaymentFailed(_logger, request.OrderId, "Payment declined");
            return CatgaResult<ProcessPaymentResult>.Failure("Payment declined by processor");
        }

        var result = await _paymentService.ProcessPaymentAsync(
            request.OrderId,
            request.Amount,
            request.PaymentMethod,
            cancellationToken);

        if (!result.IsSuccess)
        {
            LogPaymentFailed(_logger, request.OrderId, result.Error ?? "Unknown error");
            return CatgaResult<ProcessPaymentResult>.Failure(result.Error ?? "Payment processing failed");
        }

        var paymentId = $"PAY-{request.OrderId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        LogPaymentSuccess(_logger, request.OrderId, paymentId);

        return CatgaResult<ProcessPaymentResult>.Success(new ProcessPaymentResult(paymentId));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "💳 Processing payment for order {OrderId}: {Amount:C} via {PaymentMethod}")]
    static partial void LogProcessingPayment(ILogger logger, string orderId, decimal amount, string paymentMethod);

    [LoggerMessage(Level = LogLevel.Warning, Message = "❌ Payment failed for order {OrderId}: {Error}")]
    static partial void LogPaymentFailed(ILogger logger, string orderId, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "✅ Payment processed for order {OrderId}, payment: {PaymentId}")]
    static partial void LogPaymentSuccess(ILogger logger, string orderId, string paymentId);
}

/// <summary>
/// Handler for RefundPaymentCommand - compensation handler
/// </summary>
public sealed partial class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand>
{
    private readonly ILogger<RefundPaymentHandler> _logger;

    public RefundPaymentHandler(ILogger<RefundPaymentHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult> HandleAsync(
        RefundPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        LogRefundingPayment(_logger, request.PaymentId);
        // In real implementation, would call payment gateway to refund
        return Task.FromResult(CatgaResult.Success());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "🔄 COMPENSATION: Refunding payment {PaymentId}")]
    static partial void LogRefundingPayment(ILogger logger, string paymentId);
}
