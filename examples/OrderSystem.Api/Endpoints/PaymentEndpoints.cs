using System.Diagnostics.CodeAnalysis;
using OrderSystem.Api;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Payment endpoints for order payment processing
/// </summary>
public static class PaymentEndpoints
{
    [RequiresDynamicCode("Uses reflection for endpoint mapping")]
    [RequiresUnreferencedCode("Uses reflection for endpoint mapping")]
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/payments")
            .WithTags("Payments");

        group.MapPost("/process", ProcessPayment)
            .WithName("ProcessPayment")
            .WithDescription("Process payment for an order");

        group.MapPost("/{paymentId}/refund", RefundPayment)
            .WithName("RefundPayment")
            .WithDescription("Refund a completed payment");

        group.MapGet("/{paymentId}", GetPayment)
            .WithName("GetPayment")
            .WithDescription("Get payment details");
    }

    private static async Task<IResult> ProcessPayment(
        PaymentRequest request,
        IOrderRepository orderRepository,
        PaymentProcessor paymentProcessor)
    {
        // Verify order exists
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order == null)
        {
            return Results.NotFound(new MessageResponse("Order not found"));
        }

        // Verify order is in correct status for payment
        if (order.Status != OrderStatus.Pending)
        {
            return Results.BadRequest(new MessageResponse("Order is not in pending status"));
        }

        // Process payment
        var payment = await paymentProcessor.ProcessPaymentAsync(
            request.OrderId,
            order.TotalAmount,
            request.Method,
            request.TransactionId ?? Guid.NewGuid().ToString());

        if (payment.Status == PaymentStatus.Completed)
        {
            // Update order status to paid
            order.Status = OrderStatus.Paid;
            order.PaidAt = DateTime.UtcNow;
            await orderRepository.SaveAsync(order);

            return Results.Ok(new
            {
                paymentId = payment.PaymentId,
                orderId = payment.OrderId,
                amount = payment.Amount,
                method = payment.Method.ToString(),
                status = payment.Status.ToString(),
                transactionId = payment.TransactionId,
                completedAt = payment.CompletedAt
            });
        }

        return Results.BadRequest(new
        {
            paymentId = payment.PaymentId,
            status = payment.Status.ToString(),
            reason = payment.FailureReason
        });
    }

    private static async Task<IResult> RefundPayment(
        string paymentId,
        RefundRequest request,
        PaymentProcessor paymentProcessor)
    {
        // In a real app, retrieve payment from database
        // For now, simulate with a mock payment
        var payment = new Payment
        {
            PaymentId = paymentId,
            Status = PaymentStatus.Completed,
            Amount = 100m
        };

        var refunded = await paymentProcessor.RefundPaymentAsync(payment, request.Reason);

        return Results.Ok(new
        {
            paymentId = refunded.PaymentId,
            refundId = refunded.RefundId,
            status = refunded.Status.ToString(),
            amount = refunded.Amount
        });
    }

    private static Task<IResult> GetPayment(string paymentId)
    {
        // In a real app, retrieve from database
        var payment = new
        {
            paymentId,
            status = "Completed",
            amount = 100m,
            method = "Alipay",
            transactionId = Guid.NewGuid().ToString(),
            createdAt = DateTime.UtcNow
        };

        return Task.FromResult(Results.Ok(payment));
    }
}

/// <summary>
/// Payment request DTO
/// </summary>
public class PaymentRequest
{
    public string OrderId { get; set; } = string.Empty;
    public PaymentMethod Method { get; set; }
    public string? TransactionId { get; set; }
}

/// <summary>
/// Refund request DTO
/// </summary>
public class RefundRequest
{
    public string Reason { get; set; } = string.Empty;
}
