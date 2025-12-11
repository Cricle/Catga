using System;
using System.Collections.Generic;
using System.Linq;
using Catga.Flow.Dsl;

namespace Catga.Tests.E2E;

// ========== E-Commerce Flow ==========

public class ECommerceOrderFlow : FlowConfig<ECommerceOrderState>
{
    protected override void Configure(IFlowBuilder<ECommerceOrderState> flow)
    {
        flow.Name("ecommerce-order-processing");

        // Calculate totals
        flow.Step("calculate-totals", s =>
        {
            s.TotalAmount = s.Items.Sum(i => i.Quantity * i.Price);
        });

        // Apply customer discounts
        flow.Switch(s => s.CustomerType)
            .Case(CustomerType.VIP, vip =>
            {
                vip.Step("vip-discount", s =>
                {
                    s.DiscountAmount = s.TotalAmount * 0.20m; // 20% discount
                    s.ShippingCost = 0; // Free shipping
                });
            })
            .Case(CustomerType.Premium, premium =>
            {
                premium.Step("premium-discount", s =>
                {
                    s.DiscountAmount = s.TotalAmount * 0.10m; // 10% discount
                    s.ShippingCost = s.TotalAmount > 100 ? 0 : 10;
                });
            })
            .Default(regular =>
            {
                regular.Step("regular-pricing", s =>
                {
                    s.DiscountAmount = s.TotalAmount > 500 ? s.TotalAmount * 0.05m : 0;
                    s.ShippingCost = 15;
                });
            })
            .EndSwitch();

        // Calculate final amount
        flow.Step("calculate-final", s =>
        {
            s.FinalAmount = s.TotalAmount - s.DiscountAmount + s.ShippingCost;
            s.TaxAmount = s.FinalAmount * 0.08m; // 8% tax
            s.FinalAmount += s.TaxAmount;
        });

        // Parallel operations
        flow.WhenAll(
            // Reserve inventory
            f => f,

            // Process payment
            f => f.Step("process-payment", s =>
            {
                s.PaymentProcessed = true;
                s.PaymentTransactionId = $"TXN-{Guid.NewGuid():N}".Substring(0, 20);
            }),

            // Generate order confirmation
            f => f.Step("confirm-order", s =>
            {
                s.OrderConfirmed = true;
                s.ConfirmationNumber = $"CONF-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            })
        );

        // Schedule shipping
        flow.Step("schedule-shipping", s =>
        {
            s.ShippingScheduled = true;
            s.TrackingNumber = $"TRACK-{Guid.NewGuid():N}".Substring(0, 15);
            s.EstimatedDelivery = DateTime.UtcNow.AddDays(s.CustomerType == CustomerType.VIP ? 2 : 5);
        });

        // Send notifications
        flow.Step("send-notifications", s =>
        {
            s.NotificationsSet.Add("email");
            s.NotificationsSet.Add("sms");
            if (s.CustomerType == CustomerType.VIP)
            {
                s.NotificationsSet.Add("push");
            }
        });

        // Award loyalty points
        flow.Step("award-points", s =>
        {
            s.LoyaltyPointsAwarded = (int)(s.FinalAmount * (s.CustomerType == CustomerType.VIP ? 2 : 1));
        });

        // Final status
        flow;
    }
}

public class ECommerceOrderState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public CustomerType CustomerType { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public Address ShippingAddress { get; set; } = new();
    public PaymentMethod PaymentMethod { get; set; }

    // Calculated fields
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal FinalAmount { get; set; }

    // Processing status
    public bool InventoryReserved { get; set; }
    public bool PaymentProcessed { get; set; }
    public string PaymentTransactionId { get; set; } = string.Empty;
    public bool OrderConfirmed { get; set; }
    public string ConfirmationNumber { get; set; } = string.Empty;
    public bool ShippingScheduled { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public DateTime EstimatedDelivery { get; set; }
    public HashSet<string> NotificationsSet { get; set; } = new();
    public int LoyaltyPointsAwarded { get; set; }
    public OrderStatus OrderStatus { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Distributed Saga Flow ==========

public class DistributedSagaFlow : FlowConfig<SagaState>
{
    protected override void Configure(IFlowBuilder<SagaState> flow)
    {
        flow.Name("distributed-saga");

        // Step 1: Debit source account
        flow
            .Compensate(s => s.CompensationSteps.Add("Refund to source account"));

        // Step 2: Credit target account
        flow
            .Compensate(s => s.CompensationSteps.Add("Reverse credit"));

        // Step 3: Log audit trail
        flow;

        // Step 4: Send notifications
        flow;

        // Step 5: Compliance check
        flow;

        // Mark saga complete
        flow;
    }
}

public class SagaState : IFlowState
{
    public string? FlowId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string SourceAccount { get; set; } = string.Empty;
    public string TargetAccount { get; set; } = string.Empty;

    public bool AccountDebited { get; set; }
    public bool AccountCredited { get; set; }
    public bool AuditLogged { get; set; }
    public bool NotificationSent { get; set; }
    public bool ComplianceChecked { get; set; }
    public bool SagaCompleted { get; set; }
    public bool CompensationRequired { get; set; }
    public List<string> CompensationSteps { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== ETL Pipeline Flow ==========

public class ETLPipelineFlow : FlowConfig<ETLPipelineState>
{
    protected override void Configure(IFlowBuilder<ETLPipelineState> flow)
    {
        flow.Name("etl-pipeline");

        // Extract phase
        flow.Step("extract", s =>
        {
            s.ExtractedCount = s.RawData.Count;
        });

        // Transform phase - Process in batches
        flow.ForEach(s => s.RawData.Chunk(s.BatchSize))
            .WithParallelism(4)
            .Configure((batch, f) =>
            {
                f.Step("transform-batch", s =>
                {
                    foreach (var record in batch)
                    {
                        // Simulate transformation with some failures
                        if (record.Value > 90)
                        {
                            s.ErrorCount++;
                        }
                        else
                        {
                            s.TransformedCount++;

                            // Aggregate by category
                            if (!s.Aggregations.ContainsKey(record.Category))
                                s.Aggregations[record.Category] = new AggregateData();

                            s.Aggregations[record.Category].Count++;
                            s.Aggregations[record.Category].Sum += record.Value;
                        }
                    }
                });
            })
            .ContinueOnFailure()
            .EndForEach();

        // Load phase
        flow.Step("load", s =>
        {
            s.LoadedCount = s.TransformedCount;
        });

        // Calculate statistics
        flow.Step("statistics", s =>
        {
            foreach (var agg in s.Aggregations.Values)
            {
                agg.Average = agg.Count > 0 ? agg.Sum / agg.Count : 0;
            }
        });
    }
}

public class ETLPipelineState : IFlowState
{
    public string? FlowId { get; set; }
    public List<DataRecord> RawData { get; set; } = new();
    public int BatchSize { get; set; } = 100;

    public int ExtractedCount { get; set; }
    public int TransformedCount { get; set; }
    public int LoadedCount { get; set; }
    public int ErrorCount { get; set; }

    public Dictionary<string, AggregateData> Aggregations { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== IoT Processing Flow ==========

public class IoTDataProcessingFlow : FlowConfig<IoTProcessingState>
{
    protected override void Configure(IFlowBuilder<IoTProcessingState> flow)
    {
        flow.Name("iot-processing");

        // Group by device
        var deviceGroups = flow.State.SensorReadings.GroupBy(r => r.DeviceId);

        flow.ForEach(s => s.SensorReadings.GroupBy(r => r.DeviceId))
            .WithParallelism(5)
            .Configure((deviceReadings, f) =>
            {
                f.Step("process-device", s =>
                {
                    var deviceId = deviceReadings.Key;
                    var readings = deviceReadings.ToList();

                    s.ProcessedReadings += readings.Count;

                    // Calculate statistics
                    var stats = new DeviceStatistics
                    {
                        DeviceId = deviceId,
                        ReadingCount = readings.Count,
                        AverageTemperature = readings.Average(r => r.Temperature),
                        AverageHumidity = readings.Average(r => r.Humidity),
                        AveragePressure = readings.Average(r => r.Pressure)
                    };

                    s.DeviceStatistics[deviceId] = stats;

                    // Detect anomalies
                    foreach (var reading in readings)
                    {
                        if (reading.Temperature > 35 || reading.Temperature < 10)
                        {
                            s.AnomaliesDetected++;

                            if (reading.Temperature > 40)
                            {
                                s.AlertsTriggered++;
                            }
                        }
                    }
                });
            })
            .EndForEach();
    }
}

public class IoTProcessingState : IFlowState
{
    public string? FlowId { get; set; }
    public List<SensorReading> SensorReadings { get; set; } = new();

    public int ProcessedReadings { get; set; }
    public int AnomaliesDetected { get; set; }
    public int AlertsTriggered { get; set; }

    public Dictionary<string, DeviceStatistics> DeviceStatistics { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Machine Learning Pipeline Flow ==========

public class MachineLearningPipelineFlow : FlowConfig<MLPipelineState>
{
    protected override void Configure(IFlowBuilder<MLPipelineState> flow)
    {
        flow.Name("ml-pipeline");

        // Data loading
        flow;

        // Data preprocessing
        flow;

        // Feature engineering
        flow;

        // Model training
        flow.Step("train-model", s =>
        {
            s.ModelTrained = true;
            s.TrainingAccuracy = 0.85 + Random.Shared.NextDouble() * 0.1;
        });

        // Model evaluation
        flow.Step("evaluate", s =>
        {
            s.ModelEvaluated = true;
            s.ValidationAccuracy = s.TrainingAccuracy - Random.Shared.NextDouble() * 0.1;
        });

        // Model deployment
        flow.If(s => s.ValidationAccuracy > 0.7)
            .Step("deploy", s =>
            {
                s.ModelDeployed = true;
                s.ModelId = $"model-{Guid.NewGuid():N}".Substring(0, 12);
                s.DeploymentEndpoint = $"https://api.ml.com/models/{s.ModelId}";
            })
        .Else()
            .Step("reject", s =>
            {
                s.ModelDeployed = false;
                s.RejectionReason = "Accuracy below threshold";
            })
        .EndIf();
    }
}

public class MLPipelineState : IFlowState
{
    public string? FlowId { get; set; }
    public int DatasetSize { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
    public string ModelType { get; set; } = string.Empty;
    public double TrainTestSplit { get; set; }

    public bool DataLoaded { get; set; }
    public bool DataPreprocessed { get; set; }
    public bool FeaturesEngineered { get; set; }
    public bool ModelTrained { get; set; }
    public bool ModelEvaluated { get; set; }
    public bool ModelDeployed { get; set; }

    public double TrainingAccuracy { get; set; }
    public double ValidationAccuracy { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string DeploymentEndpoint { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Complex Recovery Flow ==========

public class ComplexRecoveryFlow : FlowConfig<ComplexRecoveryState>
{
    protected override void Configure(IFlowBuilder<ComplexRecoveryState> flow)
    {
        flow.Name("complex-recovery");

        flow.ForEach(s => s.Steps)
            .Configure((step, f) =>
            {
                f.Step($"execute-{step}", s =>
                {
                    // Simulate step execution
                    s.CompletedSteps.Add(step);
                });
            })
            .StopOnFirstFailure()
            .OnItemFail((state, step, error) =>
            {
                state.FailedStep = step;
                state.RecoveryAttempts++;
            })
            .EndForEach();
    }
}

public class ComplexRecoveryState : IFlowState
{
    public string? FlowId { get; set; }
    public string[] Steps { get; set; } = Array.Empty<string>();
    public List<string> CompletedSteps { get; set; } = new();
    public string FailedStep { get; set; } = string.Empty;
    public int RecoveryAttempts { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Performance Test Flow ==========

public class PerformanceTestFlow : FlowConfig<PerformanceTestState>
{
    protected override void Configure(IFlowBuilder<PerformanceTestState> flow)
    {
        flow.Name("performance-test");

        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) =>
            {
                f.Step($"process-{item}", s =>
                {
                    s.ProcessedItems.Add(item);
                });
            })
            .EndForEach();
    }
}

public class PerformanceTestState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Supporting Types ==========

public enum CustomerType
{
    Regular,
    Premium,
    VIP
}

public enum OrderStatus
{
    Pending,
    Processing,
    Completed,
    Cancelled
}

public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    PayPal,
    BankTransfer
}

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class DataRecord
{
    public int Id { get; set; }
    public int Value { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class AggregateData
{
    public int Count { get; set; }
    public double Sum { get; set; }
    public double Average { get; set; }
}

public class SensorReading
{
    public string DeviceId { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double Pressure { get; set; }
    public DateTime Timestamp { get; set; }
}

public class DeviceStatistics
{
    public string DeviceId { get; set; } = string.Empty;
    public int ReadingCount { get; set; }
    public double AverageTemperature { get; set; }
    public double AverageHumidity { get; set; }
    public double AveragePressure { get; set; }
}
