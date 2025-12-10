using Catga.Abstractions;
using Catga.Flow.Dsl;

namespace Catga.Examples.ForEachExamples;

/// <summary>
/// Real-world example: Processing order items with inventory management and payment processing.
/// </summary>
public class OrderProcessingExample
{
    // Domain Models
    public record OrderItem(string ProductId, int Quantity, decimal Price);
    public record InventoryReservation(string ReservationId, string ProductId, int Quantity);
    public record PaymentCharge(string ChargeId, decimal Amount, string Status);

    // Commands
    public record ReserveInventoryCommand(string ProductId, int Quantity) : IRequest<InventoryReservation>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record ChargePaymentCommand(string ProductId, decimal Amount) : IRequest<PaymentCharge>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record ReleaseInventoryCommand(string ReservationId) : IRequest<bool>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record RefundPaymentCommand(string ChargeId) : IRequest<bool>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    // State
    public class OrderProcessingState : IFlowState
    {
        public string? FlowId { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public List<OrderItem> Items { get; set; } = [];
        public Dictionary<string, InventoryReservation> Reservations { get; set; } = [];
        public Dictionary<string, PaymentCharge> Charges { get; set; } = [];
        public List<string> FailedItems { get; set; } = [];
        public List<string> CompensatedItems { get; set; } = [];
        public bool AllItemsProcessed { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Processing";

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    /// <summary>
    /// Example 1: Basic order processing with inventory and payment.
    /// </summary>
    public class BasicOrderProcessingFlow : FlowConfig<OrderProcessingState>
    {
        protected override void Configure(IFlowBuilder<OrderProcessingState> flow)
        {
            flow.Name("basic-order-processing");

            // Process each order item
            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Reserve inventory first
                    f.Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity))
                     .Into(s => s.Reservations[item.ProductId]);

                    // Then charge payment
                    f.Send(s => new ChargePaymentCommand(item.ProductId, item.Price * item.Quantity))
                     .Into(s => s.Charges[item.ProductId]);
                })
                .WithBatchSize(10)
                .ContinueOnFailure() // Continue processing other items if one fails
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalAmount += item.Price * item.Quantity;
                })
                .OnItemFail((state, item, error) =>
                {
                    state.FailedItems.Add(item.ProductId);
                })
                .OnComplete(s =>
                {
                    s.AllItemsProcessed = true;
                    s.Status = s.FailedItems.Count == 0 ? "Completed" : "Partially Failed";
                })
            .EndForEach();
        }
    }

    /// <summary>
    /// Example 2: Advanced order processing with compensation (Saga pattern).
    /// </summary>
    public class SagaOrderProcessingFlow : FlowConfig<OrderProcessingState>
    {
        protected override void Configure(IFlowBuilder<OrderProcessingState> flow)
        {
            flow.Name("saga-order-processing");

            // Process items with automatic compensation on failure
            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Reserve inventory
                    f.Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity))
                     .Into(s => s.Reservations[item.ProductId])
                     .IfFail(s => new ReleaseInventoryCommand(s.Reservations[item.ProductId].ReservationId));

                    // Charge payment with compensation
                    f.Send(s => new ChargePaymentCommand(item.ProductId, item.Price * item.Quantity))
                     .Into(s => s.Charges[item.ProductId])
                     .IfFail(s => new RefundPaymentCommand(s.Charges[item.ProductId].ChargeId));
                })
                .WithBatchSize(5) // Smaller batches for better error isolation
                .StopOnFirstFailure() // Stop processing if any item fails
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalAmount += item.Price * item.Quantity;
                })
                .OnItemFail((state, item, error) =>
                {
                    state.FailedItems.Add(item.ProductId);
                    state.Status = "Failed - Compensating";
                })
                .OnComplete(s =>
                {
                    s.AllItemsProcessed = true;
                    if (s.FailedItems.Count == 0)
                    {
                        s.Status = "Completed Successfully";
                    }
                })
            .EndForEach();

            // Compensation logic if any items failed
            flow.If(s => s.FailedItems.Count > 0)
                .ForEach<string>(s => s.Reservations.Keys.ToList())
                    .Configure((productId, f) =>
                    {
                        f.Send(s => new ReleaseInventoryCommand(s.Reservations[productId].ReservationId));
                    })
                    .OnItemSuccess((state, productId, result) =>
                    {
                        state.CompensatedItems.Add(productId);
                    })
                .EndForEach()
                .Send(s => s.Status = "Compensated")
            .EndIf();
        }
    }

    /// <summary>
    /// Example 3: High-volume order processing with performance optimization.
    /// </summary>
    public class HighVolumeOrderProcessingFlow : FlowConfig<OrderProcessingState>
    {
        protected override void Configure(IFlowBuilder<OrderProcessingState> flow)
        {
            flow.Name("high-volume-order-processing");

            // Optimized for high-volume processing
            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Batch operations for better performance
                    f.Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity))
                     .Into(s => s.Reservations[item.ProductId]);

                    f.Send(s => new ChargePaymentCommand(item.ProductId, item.Price * item.Quantity))
                     .Into(s => s.Charges[item.ProductId]);
                })
                .WithBatchSize(100) // Large batches for high throughput
                .ContinueOnFailure() // Don't let individual failures stop the entire batch
                .OnItemSuccess((state, item, result) =>
                {
                    // Minimal processing in callbacks for performance
                    state.TotalAmount += item.Price * item.Quantity;
                })
                .OnItemFail((state, item, error) =>
                {
                    // Just track failures, handle them later
                    state.FailedItems.Add(item.ProductId);
                })
                .OnComplete(s =>
                {
                    s.AllItemsProcessed = true;
                    s.Status = $"Processed {s.Items.Count - s.FailedItems.Count}/{s.Items.Count} items";
                })
            .EndForEach();

            // Handle failed items in a separate batch
            flow.If(s => s.FailedItems.Count > 0)
                .Send(s => s.Status = $"Retrying {s.FailedItems.Count} failed items")
                .ForEach<string>(s => s.FailedItems)
                    .Configure((productId, f) =>
                    {
                        // Retry logic for failed items
                        var item = f.GetState().Items.First(i => i.ProductId == productId);
                        f.Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity));
                    })
                    .WithBatchSize(10) // Smaller batches for retries
                    .StopOnFirstFailure() // Be more careful with retries
                .EndForEach()
            .EndIf();
        }
    }

    /// <summary>
    /// Example 4: Conditional processing based on item properties.
    /// </summary>
    public class ConditionalOrderProcessingFlow : FlowConfig<OrderProcessingState>
    {
        protected override void Configure(IFlowBuilder<OrderProcessingState> flow)
        {
            flow.Name("conditional-order-processing");

            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Different processing based on item value
                    f.If(s => item.Price * item.Quantity > 1000) // High-value items
                        .Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity))
                         .Into(s => s.Reservations[item.ProductId])
                        .Send(s => new ChargePaymentCommand(item.ProductId, item.Price * item.Quantity))
                         .Into(s => s.Charges[item.ProductId])
                    .Else() // Low-value items - simplified processing
                        .Send(s => new ChargePaymentCommand(item.ProductId, item.Price * item.Quantity))
                         .Into(s => s.Charges[item.ProductId])
                    .EndIf();
                })
                .WithBatchSize(20)
                .ContinueOnFailure()
                .OnItemSuccess((state, item, result) =>
                {
                    state.TotalAmount += item.Price * item.Quantity;
                })
                .OnItemFail((state, item, error) =>
                {
                    state.FailedItems.Add(item.ProductId);
                })
                .OnComplete(s =>
                {
                    s.AllItemsProcessed = true;
                    s.Status = "Processing Complete";
                })
            .EndForEach();
        }
    }
}
