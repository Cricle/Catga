using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Services;

/// <summary>
/// Compensation publisher for CreateOrderCommand.
/// Automatically publishes OrderFailedEvent when order creation fails.
/// </summary>
public sealed class CreateOrderCompensation : CompensationPublisher<CreateOrderCommand, OrderFailedEvent>
{
    protected override OrderFailedEvent? CreateCompensationEvent(CreateOrderCommand request, string? error)
        => new(
            OrderId: $"FAILED-{DateTime.UtcNow:yyyyMMddHHmmss}",
            CustomerId: request.CustomerId,
            Reason: error ?? "Unknown error",
            FailedAt: DateTime.UtcNow);
}

/// <summary>
/// Simulated distributed inventory service with rate limiting awareness.
/// </summary>
public sealed partial class DistributedInventoryService : IInventoryService
{
    private readonly IDistributedRateLimiter? _rateLimiter;
    private readonly ILogger<DistributedInventoryService> _logger;

    public DistributedInventoryService(
        ILogger<DistributedInventoryService> logger,
        IDistributedRateLimiter? rateLimiter = null)
    {
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public async ValueTask<CatgaResult> CheckStockAsync(List<Domain.OrderItem> items, CancellationToken ct = default)
    {
        // Apply rate limiting if available
        if (_rateLimiter != null)
        {
            var result = await _rateLimiter.TryAcquireAsync("inventory:check", ct: ct);
            if (!result.IsAcquired)
            {
                LogRateLimited(_logger, "CheckStock", result.RetryAfter?.TotalSeconds ?? 0);
                return CatgaResult.Failure("Rate limited. Please retry later.");
            }
        }

        // Simulate stock check
        await Task.Delay(10, ct);
        LogStockChecked(_logger, items.Count);
        return CatgaResult.Success();
    }

    public async ValueTask<CatgaResult> ReserveStockAsync(string orderId, List<Domain.OrderItem> items, CancellationToken ct = default)
    {
        if (_rateLimiter != null)
        {
            var result = await _rateLimiter.TryAcquireAsync("inventory:reserve", ct: ct);
            if (!result.IsAcquired)
            {
                return CatgaResult.Failure("Rate limited. Please retry later.");
            }
        }

        await Task.Delay(15, ct);
        LogStockReserved(_logger, orderId, items.Count);
        return CatgaResult.Success();
    }

    public async ValueTask<CatgaResult> ReleaseStockAsync(string orderId, List<Domain.OrderItem> items, CancellationToken ct = default)
    {
        await Task.Delay(5, ct);
        LogStockReleased(_logger, orderId, items.Count);
        return CatgaResult.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limited on {Operation}, retry after {RetryAfterSeconds}s")]
    private static partial void LogRateLimited(ILogger logger, string operation, double retryAfterSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock checked for {ItemCount} items")]
    private static partial void LogStockChecked(ILogger logger, int itemCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock reserved for order {OrderId}: {ItemCount} items")]
    private static partial void LogStockReserved(ILogger logger, string orderId, int itemCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock released for order {OrderId}: {ItemCount} items")]
    private static partial void LogStockReleased(ILogger logger, string orderId, int itemCount);
}

/// <summary>
/// Simulated payment service with failure simulation.
/// </summary>
public sealed partial class SimulatedPaymentService : IPaymentService
{
    private readonly ILogger<SimulatedPaymentService> _logger;

    public SimulatedPaymentService(ILogger<SimulatedPaymentService> logger)
    {
        _logger = logger;
    }

    public async ValueTask<CatgaResult> ProcessPaymentAsync(string orderId, decimal amount, string paymentMethod, CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        // Simulate payment failure for specific payment methods
        if (paymentMethod.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            LogPaymentFailed(_logger, orderId, paymentMethod);
            return CatgaResult.Failure($"Payment method '{paymentMethod}' rejected");
        }

        LogPaymentSuccess(_logger, orderId, amount);
        return CatgaResult.Success();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Payment failed for order {OrderId}: {PaymentMethod}")]
    private static partial void LogPaymentFailed(ILogger logger, string orderId, string paymentMethod);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment processed for order {OrderId}: {Amount:C}")]
    private static partial void LogPaymentSuccess(ILogger logger, string orderId, decimal amount);
}
