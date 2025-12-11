using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Buffers;
using Catga.Flow.Dsl;
using Catga.Abstractions;

namespace Catga.Tests.E2E;

// ========== Flow Configurations ==========

public class OrderProcessingE2EFlow : FlowConfig<OrderE2EState>
{
    protected override void Configure(IFlowBuilder<OrderE2EState> flow)
    {
        flow.Name("order-processing-e2e");

        // Calculate total
        flow.Step("calculate-total", s =>
        {
            s.TotalAmount = s.Items.Sum(i => i.Quantity * i.Price);
        });

        // Check inventory for all items
        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new CheckInventoryCommand { ProductId = item.ProductId, Quantity = item.Quantity })
                    .Into((s, result) => s.AllItemsAvailable = s.AllItemsAvailable && result.Value);
            })
            .OnComplete(s => s.InventoryChecked = true)
            .EndForEach();

        // Process payment
        flow.Send(s => new ProcessPaymentCommand { Amount = s.TotalAmount })
            .Into((s, result) =>
            {
                s.PaymentProcessed = true;
                s.PaymentTransactionId = result.Value;
            });

        // Confirm order
        flow.Send(s => new ConfirmOrderCommand { OrderId = s.OrderId })
            .Into((s, result) =>
            {
                s.OrderConfirmed = true;
                s.ConfirmationNumber = result.Value;
            });

        // Schedule shipping
        flow.Send(s => new ScheduleShippingCommand { OrderId = s.OrderId })
            .Into((s, result) =>
            {
                s.ShippingScheduled = true;
                s.TrackingNumber = result.Value;
            });

        // Send confirmation email
        flow.Send(s => new SendEmailCommand { CustomerId = s.CustomerId, OrderId = s.OrderId })
            .Into((s, result) => s.EmailSent = result.Value);
    }
}

public class ConditionalE2EFlow : FlowConfig<ConditionalE2EState>
{
    protected override void Configure(IFlowBuilder<ConditionalE2EState> flow)
    {
        flow.Name("conditional-e2e");

        // Customer type based processing
        flow.Switch(s => s.CustomerType)
            .Case("VIP", vip =>
            {
                vip.Send(s => new ApplyDiscountCommand { Percentage = 0.20m })
                   .Into((s, r) => s.DiscountApplied = r.Value);

                vip.Send(s => new SetShippingCommand { Type = "Express" })
                   .Into((s, r) => s.ShippingType = r.Value);

                vip.Send(s => new AwardPointsCommand { Points = (int)(s.OrderAmount * 0.2) })
                   .Into((s, r) => s.LoyaltyPointsAwarded = r.Value);
            })
            .Case("Regular", regular =>
            {
                regular.Send(s => new ApplyDiscountCommand { Percentage = 0.05m })
                       .Into((s, r) => s.DiscountApplied = r.Value);

                regular.Send(s => new SetShippingCommand { Type = "Standard" })
                       .Into((s, r) => s.ShippingType = r.Value);

                regular.Send(s => new AwardPointsCommand { Points = (int)(s.OrderAmount * 0.1) })
                       .Into((s, r) => s.LoyaltyPointsAwarded = r.Value);
            })
            .Case("New", newCustomer =>
            {
                newCustomer.Send(s => new ApplyDiscountCommand { Percentage = 0.10m })
                           .Into((s, r) => s.DiscountApplied = r.Value);

                newCustomer.Send(s => new SendWelcomeCommand { })
                           .Into((s, r) => s.WelcomeEmailSent = r.Value);

                newCustomer.Send(s => new SetShippingCommand { Type = "Standard" })
                           .Into((s, r) => s.ShippingType = r.Value);

                newCustomer.Send(s => new AwardPointsCommand { Points = (int)(s.OrderAmount * 0.2) }) // Bonus
                           .Into((s, r) => s.LoyaltyPointsAwarded = r.Value);
            })
            .EndSwitch();
    }
}

public class ParallelProcessingE2EFlow : FlowConfig<ParallelE2EState>
{
    protected override void Configure(IFlowBuilder<ParallelE2EState> flow)
    {
        flow.Name("parallel-processing-e2e");

        flow.ForEach(s => s.Items)
            .WithParallelism(10) // Process 10 items at a time
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessItemCommand { ItemId = item.Id, Value = item.Value })
                    .Into((s, result) => s.Results[item.Id] = result.Value);
            })
            .OnItemSuccess((state, item, result) =>
            {
                state.ProcessedItems.Add(item.Id);
            })
            .OnItemFail((state, item, error) =>
            {
                state.FailedItems.Add(item.Id);
            })
            .ContinueOnFailure()
            .EndForEach();
    }
}

public class RecoveryE2EFlow : FlowConfig<RecoveryE2EState>
{
    protected override void Configure(IFlowBuilder<RecoveryE2EState> flow)
    {
        flow.Name("recovery-e2e");

        flow.ForEach(s => s.Steps)
            .Configure((step, f) =>
            {
                f.Send(s => new ExecuteStepCommand { StepName = step })
                    .Into((s, result) =>
                    {
                        if (result.Value)
                        {
                            s.CompletedSteps.Add(step);
                        }
                    });
            })
            .StopOnFirstFailure()
            .OnComplete(s => s.AllStepsCompleted = true)
            .EndForEach();
    }
}

public class CoordinationE2EFlow : FlowConfig<CoordinationE2EState>
{
    protected override void Configure(IFlowBuilder<CoordinationE2EState> flow)
    {
        flow.Name("coordination-e2e");

        flow.WhenAll(
            f => f.Send(s => new GenerateInvoiceCommand { OrderId = s.OrderId })
                  .Into((s, r) =>
                  {
                      s.InvoiceGenerated = true;
                      s.InvoiceNumber = r.Value;
                  }),

            f => f.Send(s => new UpdateInventoryCommand { OrderId = s.OrderId })
                  .Into((s, r) => s.InventoryUpdated = r.Value),

            f => f.Send(s => new NotifyCustomerCommand { OrderId = s.OrderId })
                  .Into((s, r) => s.CustomerNotified = r.Value),

            f => f.Send(s => new ArrangeShippingCommand { OrderId = s.OrderId })
                  .Into((s, r) =>
                  {
                      s.ShippingArranged = true;
                      s.ShippingCarrier = r.Value;
                  })
        );

        flow;
    }
}

public class RaceConditionE2EFlow : FlowConfig<RaceE2EState>
{
    protected override void Configure(IFlowBuilder<RaceE2EState> flow)
    {
        flow.Name("race-condition-e2e");

        var startTime = DateTime.UtcNow;

        flow.WhenAny(
            f => f.Send(s => new PaymentProviderCommand { Provider = "Provider1", Amount = s.Amount }),
            f => f.Send(s => new PaymentProviderCommand { Provider = "Provider2", Amount = s.Amount }),
            f => f.Send(s => new PaymentProviderCommand { Provider = "Provider3", Amount = s.Amount })
        ).Into((s, result) =>
        {
            s.PaymentProcessed = result.IsSuccess;
            if (result.IsSuccess && result.Value != null)
            {
                s.WinningProvider = result.Value.Provider;
                s.TransactionId = result.Value.TransactionId;
                s.ProcessingTime = result.Value.ProcessingTime;
            }
        });
    }
}

// ========== States ==========

public class OrderE2EState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public List<OrderItemE2E> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public bool InventoryChecked { get; set; }
    public bool AllItemsAvailable { get; set; } = true;
    public bool PaymentProcessed { get; set; }
    public string PaymentTransactionId { get; set; } = string.Empty;
    public bool OrderConfirmed { get; set; }
    public string ConfirmationNumber { get; set; } = string.Empty;
    public bool ShippingScheduled { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public bool EmailSent { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ConditionalE2EState : IFlowState
{
    public string? FlowId { get; set; }
    public string CustomerType { get; set; } = string.Empty;
    public decimal OrderAmount { get; set; }
    public decimal DiscountApplied { get; set; }
    public string ShippingType { get; set; } = string.Empty;
    public int LoyaltyPointsAwarded { get; set; }
    public bool WelcomeEmailSent { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ParallelE2EState : IFlowState
{
    public string? FlowId { get; set; }
    public List<ProcessingItemE2E> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();
    public List<string> FailedItems { get; set; } = new();
    public Dictionary<string, int> Results { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class RecoveryE2EState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Steps { get; set; } = new();
    public List<string> CompletedSteps { get; set; } = new();
    public bool AllStepsCompleted { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class CoordinationE2EState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public bool InvoiceGenerated { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public bool InventoryUpdated { get; set; }
    public bool CustomerNotified { get; set; }
    public bool ShippingArranged { get; set; }
    public string ShippingCarrier { get; set; } = string.Empty;
    public bool AllOperationsCompleted { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class RaceE2EState : IFlowState
{
    public string? FlowId { get; set; }
    public decimal Amount { get; set; }
    public bool PaymentProcessed { get; set; }
    public string WinningProvider { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Supporting Models ==========

public class OrderItemE2E
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class ProcessingItemE2E
{
    public string Id { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class PaymentResult
{
    public string Provider { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
}

// ========== Commands ==========

public record CheckInventoryCommand : IRequest<bool>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ProcessPaymentCommand : IRequest<string>
{
    public decimal Amount { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ConfirmOrderCommand : IRequest<string>
{
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ScheduleShippingCommand : IRequest<string>
{
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record SendEmailCommand : IRequest<bool>
{
    public string CustomerId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ApplyDiscountCommand : IRequest<decimal>
{
    public decimal Percentage { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record SetShippingCommand : IRequest<string>
{
    public string Type { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record AwardPointsCommand : IRequest<int>
{
    public int Points { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record SendWelcomeCommand : IRequest<bool>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ProcessItemCommand : IRequest<int>
{
    public string ItemId { get; init; } = string.Empty;
    public int Value { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ExecuteStepCommand : IRequest<bool>
{
    public string StepName { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record GenerateInvoiceCommand : IRequest<string>
{
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record UpdateInventoryCommand : IRequest<bool>
{
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record NotifyCustomerCommand : IRequest<bool>
{
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ArrangeShippingCommand : IRequest<string>
{
    public string OrderId { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record PaymentProviderCommand : IRequest<PaymentResult>
{
    public string Provider { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// ========== Test Serializer ==========

public class TestMessageSerializer : IMessageSerializer
{
    public string Name => "e2e-json";

    public byte[] Serialize<T>(T value)
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeof(T));
    }

    public T Deserialize<T>(byte[] data)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(data)!;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(data)!;
    }

    public void Serialize<T>(T value, IBufferWriter<byte> bufferWriter)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeof(T));
        bufferWriter.Write(bytes);
    }

    public byte[] Serialize(object value, Type type)
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
    }

    public object? Deserialize(byte[] data, Type type)
    {
        return System.Text.Json.JsonSerializer.Deserialize(data, type);
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        return System.Text.Json.JsonSerializer.Deserialize(data, type);
    }

    public void Serialize(object value, Type type, IBufferWriter<byte> bufferWriter)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        bufferWriter.Write(bytes);
    }
}
