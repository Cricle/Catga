namespace OrderSystem.Api.Domain;

/// <summary>
/// Payment method enumeration
/// </summary>
public enum PaymentMethod
{
    Alipay = 0,
    WeChat = 1,
    CreditCard = 2,
    BankTransfer = 3,
    Cash = 4
}

/// <summary>
/// Payment status enumeration
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Refunded = 4
}

/// <summary>
/// Payment record for order
/// </summary>
public class Payment
{
    public string PaymentId { get; set; } = Guid.NewGuid().ToString();
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string TransactionId { get; set; } = string.Empty;
    public string? RefundId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Payment processor for handling order payments
/// </summary>
public class PaymentProcessor
{
    private readonly Random _random = new();

    /// <summary>
    /// Process payment for order
    /// </summary>
    public async Task<Payment> ProcessPaymentAsync(
        string orderId,
        decimal amount,
        PaymentMethod method,
        string transactionId)
    {
        var payment = new Payment
        {
            OrderId = orderId,
            Amount = amount,
            Method = method,
            TransactionId = transactionId,
            Status = PaymentStatus.Processing
        };

        // Simulate payment gateway call
        await Task.Delay(_random.Next(500, 2000));

        // 95% success rate
        if (_random.Next(100) < 95)
        {
            payment.Status = PaymentStatus.Completed;
            payment.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = "Payment gateway timeout";
        }

        return payment;
    }

    /// <summary>
    /// Refund payment
    /// </summary>
    public async Task<Payment> RefundPaymentAsync(Payment payment, string reason)
    {
        if (payment.Status != PaymentStatus.Completed)
        {
            throw new InvalidOperationException("Only completed payments can be refunded");
        }

        payment.RefundId = Guid.NewGuid().ToString();
        payment.Status = PaymentStatus.Refunded;

        // Simulate refund processing
        await Task.Delay(_random.Next(500, 1500));

        return payment;
    }
}
