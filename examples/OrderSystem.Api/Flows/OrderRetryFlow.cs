using Catga.Flow.Dsl;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Flows;

/// <summary>
/// Order processing flow with retry logic using While loops and Try-Catch.
/// Demonstrates loop constructs and exception handling in Flow DSL.
/// </summary>
public class OrderRetryFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("order-retry-processing");

        // Try to process the order with exception handling
        flow.Try()
            // Attempt to validate and process order
            .Send(s => new ValidateOrderCommand { OrderId = s.OrderId, Order = s.Order })
            .Into(s => s.ProcessingProgress = 0.2m)

            // Retry payment processing up to 3 times
            .Repeat(3)
                .Send(s => new ProcessPaymentCommand { OrderId = s.OrderId, Amount = s.Order.TotalAmount })
                .Into(s => s.PaymentAttempts++)
                .BreakIf(s => s.PaymentSucceeded)
            .EndRepeat()

            // Process items in order with validation
            .While(s => s.CurrentItemIndex < s.Order.Items.Count)
                .Send(s => new ProcessOrderItemCommand
                {
                    OrderId = s.OrderId,
                    Item = s.Order.Items[s.CurrentItemIndex]
                })
                .Into(s => s.CurrentItemIndex++)
                .Into(s => s.ProcessingProgress = 0.2m + (0.6m * s.CurrentItemIndex / s.Order.Items.Count))
            .EndWhile()

            // Finalize order
            .Send(s => new FinalizeOrderCommand { OrderId = s.OrderId })
            .Into(s => s.ProcessingProgress = 0.9m)

        .Catch<InvalidOperationException>((s, ex) =>
        {
            s.ErrorMessage = $"Invalid order: {ex.Message}";
            s.ProcessingProgress = 0.0m;
        })
        .Catch<TimeoutException>((s, ex) =>
        {
            s.ErrorMessage = "Order processing timeout";
            s.ProcessingProgress = 0.5m;
        })
        .Catch<Exception>((s, ex) =>
        {
            s.ErrorMessage = $"Unexpected error: {ex.Message}";
            s.ProcessingProgress = 0.0m;
        })
        .Finally(s =>
        {
            s.ProcessingCompleted = true;
            s.LastProcessedAt = DateTime.UtcNow;
        })
        .EndTry();

        // Publish completion event
        flow.Publish(s => new OrderProcessedEvent(s.OrderId, s.Order.CustomerId, s.ProcessingProgress, DateTime.UtcNow));
    }
}

/// <summary>
/// Order processing flow with conditional logic using When and If statements.
/// Demonstrates expression-based conditions and branching.
/// </summary>
public class OrderConditionalFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("order-conditional-processing");

        // Check if order is high-value
        flow.When(s => s.Order.TotalAmount > 10000)
            .Send(s => new ApproveHighValueOrderCommand { OrderId = s.OrderId })
            .Into(s => s.RequiresApproval = true)
        .EndWhen();

        // Standard conditional branching
        flow.If(s => s.Order.CustomerType == CustomerType.VIP)
            .Send(s => new AwardVIPBonusCommand { CustomerId = s.Order.CustomerId, BonusPoints = 1000 })
            .Into(s => s.VIPProcessed = true)
        .ElseIf(s => s.Order.CustomerType == CustomerType.Premium)
            .Send(s => new AwardPremiumBonusCommand { CustomerId = s.Order.CustomerId, BonusPoints = 500 })
            .Into(s => s.PremiumProcessed = true)
        .Else(f => f
            .Send(s => new AwardStandardBonusCommand { CustomerId = s.Order.CustomerId, BonusPoints = 100 })
            .Into(s => s.StandardProcessed = true)
        )
        .EndIf();

        // Process based on order status
        flow.Switch(s => s.Order.Status)
            .Case(OrderStatus.Pending, c => c
                .Send(s => new ProcessPendingOrderCommand { OrderId = s.OrderId })
                .Into(s => s.StatusProcessed = true)
            )
            .Case(OrderStatus.Confirmed, c => c
                .Send(s => new ProcessConfirmedOrderCommand { OrderId = s.OrderId })
                .Into(s => s.StatusProcessed = true)
            )
            .Default(c => c
                .Into(s => s.ErrorMessage = "Unknown order status")
            )
        .EndSwitch();

        // Publish final event
        flow.Publish(s => new OrderProcessedEvent(s.OrderId, s.Order.CustomerId, 1.0m, DateTime.UtcNow));
    }
}

/// <summary>
/// Order processing flow with DoWhile loop.
/// Demonstrates post-condition loop construct.
/// </summary>
public class OrderDoWhileFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("order-do-while-processing");

        // Initialize processing
        flow.Into(s => s.RetryCount = 0);

        // Do-While loop for retry logic
        flow.DoWhile()
            .Send(s => new ProcessOrderCommand { OrderId = s.OrderId })
            .Into(s => s.RetryCount++)
            .Into(s => s.LastProcessedAt = DateTime.UtcNow)
        .Until(s => s.OrderProcessed || s.RetryCount >= 3);

        // Check if processing succeeded
        flow.If(s => s.OrderProcessed)
            .Publish(s => new OrderSuccessEvent(s.OrderId, s.Order.CustomerId, DateTime.UtcNow))
        .Else(f => f
            .Publish(s => new OrderFailedEvent(s.OrderId, s.Order.CustomerId, "Max retries exceeded", DateTime.UtcNow))
        )
        .EndIf();
    }
}
