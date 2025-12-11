using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.E2E;

/// <summary>
/// Complete end-to-end tests covering complex real-world scenarios.
/// </summary>
public class FlowDslCompleteE2ETests
{
    private readonly ITestOutputHelper _output;

    public FlowDslCompleteE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task E2E_ECommerceOrderFlow_CompleteScenario()
    {
        // Arrange - Complete e-commerce order processing
        var services = new ServiceCollection();
        var mediator = SetupECommerceMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ECommerceOrderState, ECommerceOrderFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ECommerceOrderState, ECommerceOrderFlow>>();

        var order = new ECommerceOrderState
        {
            FlowId = "ecom-order-001",
            OrderId = "ORD-2024-12345",
            CustomerId = "CUST-VIP-001",
            CustomerType = CustomerType.VIP,
            Items = new List<OrderItem>
            {
                new() { ProductId = "LAPTOP-001", Name = "Gaming Laptop", Quantity = 1, Price = 1500.00m },
                new() { ProductId = "MOUSE-001", Name = "Gaming Mouse", Quantity = 2, Price = 75.00m },
                new() { ProductId = "KEYBOARD-001", Name = "Mechanical Keyboard", Quantity = 1, Price = 150.00m }
            },
            ShippingAddress = new Address
            {
                Street = "123 Tech Street",
                City = "Silicon Valley",
                State = "CA",
                ZipCode = "94000",
                Country = "USA"
            },
            PaymentMethod = PaymentMethod.CreditCard
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(order);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var finalState = result.State;

        // Verify complete flow execution
        finalState.TotalAmount.Should().Be(1800.00m); // 1500 + 150 + 150
        finalState.DiscountAmount.Should().Be(360.00m); // 20% VIP discount
        finalState.FinalAmount.Should().Be(1440.00m); // After discount
        finalState.TaxAmount.Should().BeGreaterThan(0); // Tax calculated
        finalState.ShippingCost.Should().Be(0); // Free shipping for VIP

        finalState.InventoryReserved.Should().BeTrue();
        finalState.PaymentProcessed.Should().BeTrue();
        finalState.PaymentTransactionId.Should().NotBeNullOrWhiteSpace();

        finalState.OrderConfirmed.Should().BeTrue();
        finalState.ConfirmationNumber.Should().NotBeNullOrWhiteSpace();

        finalState.ShippingScheduled.Should().BeTrue();
        finalState.TrackingNumber.Should().NotBeNullOrWhiteSpace();
        finalState.EstimatedDelivery.Should().BeAfter(DateTime.UtcNow);

        finalState.NotificationsSet.Should().Contain("email", "sms", "push");
        finalState.LoyaltyPointsAwarded.Should().BeGreaterThan(0);

        finalState.OrderStatus.Should().Be(OrderStatus.Completed);

        _output.WriteLine($"✓ E-commerce order processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Order Total: ${finalState.TotalAmount:F2}");
        _output.WriteLine($"  VIP Discount: ${finalState.DiscountAmount:F2}");
        _output.WriteLine($"  Final Amount: ${finalState.FinalAmount:F2}");
        _output.WriteLine($"  Loyalty Points: {finalState.LoyaltyPointsAwarded}");
    }

    [Fact]
    public async Task E2E_MicroserviceSaga_DistributedTransaction()
    {
        // Arrange - Distributed saga pattern
        var services = new ServiceCollection();
        var mediator = SetupSagaMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SagaState, DistributedSagaFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SagaState, DistributedSagaFlow>>();

        var saga = new SagaState
        {
            FlowId = "saga-001",
            TransactionId = Guid.NewGuid().ToString(),
            Amount = 5000.00m,
            SourceAccount = "ACC-001",
            TargetAccount = "ACC-002"
        };

        // Act
        var result = await executor!.RunAsync(saga);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify all saga steps completed
        result.State.AccountDebited.Should().BeTrue();
        result.State.AccountCredited.Should().BeTrue();
        result.State.AuditLogged.Should().BeTrue();
        result.State.NotificationSent.Should().BeTrue();
        result.State.ComplianceChecked.Should().BeTrue();

        result.State.SagaCompleted.Should().BeTrue();
        result.State.CompensationRequired.Should().BeFalse();

        _output.WriteLine($"✓ Distributed saga completed successfully");
        _output.WriteLine($"  Transaction: {result.State.TransactionId}");
        _output.WriteLine($"  Amount: ${result.State.Amount:F2}");
    }

    [Fact]
    public async Task E2E_DataPipeline_ETLProcessing()
    {
        // Arrange - ETL data pipeline
        var services = new ServiceCollection();
        var mediator = SetupETLMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ETLPipelineState, ETLPipelineFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ETLPipelineState, ETLPipelineFlow>>();

        // Generate test data
        var rawData = Enumerable.Range(1, 1000)
            .Select(i => new DataRecord
            {
                Id = i,
                Value = Random.Shared.Next(1, 100),
                Category = $"CAT-{Random.Shared.Next(1, 5)}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            }).ToList();

        var pipeline = new ETLPipelineState
        {
            FlowId = "etl-pipeline-001",
            RawData = rawData,
            BatchSize = 100
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(pipeline);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify ETL results
        result.State.ExtractedCount.Should().Be(1000);
        result.State.TransformedCount.Should().BeGreaterThan(0);
        result.State.LoadedCount.Should().Be(result.State.TransformedCount);
        result.State.ErrorCount.Should().BeLessThan(50); // Less than 5% errors

        result.State.Aggregations.Should().NotBeEmpty();
        result.State.Aggregations.Should().ContainKeys("CAT-1", "CAT-2", "CAT-3", "CAT-4");

        _output.WriteLine($"✓ ETL pipeline processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Records Extracted: {result.State.ExtractedCount}");
        _output.WriteLine($"  Records Transformed: {result.State.TransformedCount}");
        _output.WriteLine($"  Records Loaded: {result.State.LoadedCount}");
        _output.WriteLine($"  Errors: {result.State.ErrorCount}");
        _output.WriteLine($"  Throughput: {1000 * 1000 / stopwatch.ElapsedMilliseconds} records/sec");
    }

    [Fact]
    public async Task E2E_IoTDataProcessing_RealTimeStreaming()
    {
        // Arrange - IoT sensor data processing
        var services = new ServiceCollection();
        var mediator = SetupIoTMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<IoTProcessingState, IoTDataProcessingFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<IoTProcessingState, IoTDataProcessingFlow>>();

        // Simulate IoT sensor data
        var sensorData = new List<SensorReading>();
        for (int deviceId = 1; deviceId <= 10; deviceId++)
        {
            for (int reading = 0; reading < 100; reading++)
            {
                sensorData.Add(new SensorReading
                {
                    DeviceId = $"SENSOR-{deviceId:D3}",
                    Temperature = 20 + Random.Shared.NextDouble() * 10,
                    Humidity = 40 + Random.Shared.NextDouble() * 20,
                    Pressure = 1000 + Random.Shared.NextDouble() * 50,
                    Timestamp = DateTime.UtcNow.AddSeconds(-reading)
                });
            }
        }

        var iotState = new IoTProcessingState
        {
            FlowId = "iot-processing-001",
            SensorReadings = sensorData
        };

        // Act
        var result = await executor!.RunAsync(iotState);

        // Assert
        result.IsSuccess.Should().BeTrue();

        result.State.ProcessedReadings.Should().Be(1000);
        result.State.AnomaliesDetected.Should().BeGreaterThanOrEqualTo(0);
        result.State.AlertsTriggered.Should().BeGreaterThanOrEqualTo(0);

        result.State.DeviceStatistics.Should().HaveCount(10);
        foreach (var stat in result.State.DeviceStatistics.Values)
        {
            stat.AverageTemperature.Should().BeInRange(20, 30);
            stat.AverageHumidity.Should().BeInRange(40, 60);
        }

        _output.WriteLine($"✓ IoT data processing completed");
        _output.WriteLine($"  Readings Processed: {result.State.ProcessedReadings}");
        _output.WriteLine($"  Anomalies Detected: {result.State.AnomaliesDetected}");
        _output.WriteLine($"  Alerts Triggered: {result.State.AlertsTriggered}");
    }

    [Fact]
    public async Task E2E_MachineLearningPipeline_ModelTraining()
    {
        // Arrange - ML model training pipeline
        var services = new ServiceCollection();
        var mediator = SetupMLMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<MLPipelineState, MachineLearningPipelineFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<MLPipelineState, MachineLearningPipelineFlow>>();

        var mlState = new MLPipelineState
        {
            FlowId = "ml-pipeline-001",
            DatasetSize = 10000,
            Features = new[] { "feature1", "feature2", "feature3", "feature4", "feature5" },
            ModelType = "RandomForest",
            TrainTestSplit = 0.8
        };

        // Act
        var result = await executor!.RunAsync(mlState);

        // Assert
        result.IsSuccess.Should().BeTrue();

        result.State.DataLoaded.Should().BeTrue();
        result.State.DataPreprocessed.Should().BeTrue();
        result.State.FeaturesEngineered.Should().BeTrue();
        result.State.ModelTrained.Should().BeTrue();
        result.State.ModelEvaluated.Should().BeTrue();
        result.State.ModelDeployed.Should().BeTrue();

        result.State.TrainingAccuracy.Should().BeGreaterThan(0.7);
        result.State.ValidationAccuracy.Should().BeGreaterThan(0.6);
        result.State.ModelId.Should().NotBeNullOrWhiteSpace();
        result.State.DeploymentEndpoint.Should().NotBeNullOrWhiteSpace();

        _output.WriteLine($"✓ ML pipeline completed");
        _output.WriteLine($"  Model Type: {result.State.ModelType}");
        _output.WriteLine($"  Training Accuracy: {result.State.TrainingAccuracy:P}");
        _output.WriteLine($"  Validation Accuracy: {result.State.ValidationAccuracy:P}");
        _output.WriteLine($"  Model ID: {result.State.ModelId}");
    }

    [Fact]
    public async Task E2E_ComplexRecoveryScenario_MultipleFailurePoints()
    {
        // Arrange - Flow with multiple potential failure points
        var services = new ServiceCollection();
        var mediator = SetupRecoveryMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ComplexRecoveryState, ComplexRecoveryFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ComplexRecoveryState, ComplexRecoveryFlow>>();
        var store = provider.GetService<IDslFlowStore>();

        var state = new ComplexRecoveryState
        {
            FlowId = "recovery-complex-001",
            Steps = new[] { "Step1", "Step2", "FailStep", "Step3", "Step4", "Step5" }
        };

        // Act - First run (will fail)
        SetRecoveryMediatorToFail(mediator, "FailStep");
        var firstRun = await executor!.RunAsync(state);

        firstRun.IsSuccess.Should().BeFalse();
        firstRun.State.CompletedSteps.Should().Contain("Step1", "Step2");
        firstRun.State.CompletedSteps.Should().NotContain("FailStep");

        // Fix the issue and resume
        SetRecoveryMediatorToSucceed(mediator);
        var resumeRun = await executor.ResumeAsync(state.FlowId!);

        // Assert
        resumeRun.IsSuccess.Should().BeTrue();
        resumeRun.State.CompletedSteps.Should().Contain("Step1", "Step2", "FailStep", "Step3", "Step4", "Step5");
        resumeRun.State.RecoveryAttempts.Should().Be(1);

        _output.WriteLine($"✓ Complex recovery scenario completed");
        _output.WriteLine($"  Recovery Attempts: {resumeRun.State.RecoveryAttempts}");
        _output.WriteLine($"  Completed Steps: {string.Join(", ", resumeRun.State.CompletedSteps)}");
    }

    [Fact]
    public async Task E2E_PerformanceUnderLoad_1000ConcurrentFlows()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = SetupPerformanceMediatorMock();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<PerformanceTestState, PerformanceTestFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<PerformanceTestState, PerformanceTestFlow>>();

        const int flowCount = 1000;
        var flows = Enumerable.Range(0, flowCount)
            .Select(i => new PerformanceTestState
            {
                FlowId = $"perf-flow-{i:D4}",
                Items = Enumerable.Range(0, 10).Select(j => $"item-{j}").ToList()
            }).ToList();

        // Act - Run all flows concurrently
        var stopwatch = Stopwatch.StartNew();
        var tasks = flows.Select(flow => executor!.RunAsync(flow));
        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = results.Count(r => r.IsSuccess);
        successCount.Should().Be(flowCount, "all flows should complete successfully");

        var throughput = flowCount * 1000.0 / stopwatch.ElapsedMilliseconds;

        _output.WriteLine($"✓ Performance test completed");
        _output.WriteLine($"  Flows Processed: {flowCount}");
        _output.WriteLine($"  Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Throughput: {throughput:F0} flows/sec");
        _output.WriteLine($"  Average Time: {stopwatch.ElapsedMilliseconds / (double)flowCount:F2}ms per flow");

        throughput.Should().BeGreaterThan(100, "should process at least 100 flows per second");
    }

    // Helper methods for setting up mediator mocks
    private ICatgaMediator SetupECommerceMediatorMock()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        // Setup all command responses...
        mediator.SendAsync(Arg.Any<IRequest<object>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<object>>(CatgaResult<object>.Success(new object())));

        return mediator;
    }

    private ICatgaMediator SetupSagaMediatorMock()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync(Arg.Any<IRequest<bool>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupETLMediatorMock()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync(Arg.Any<IRequest<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(100)));

        return mediator;
    }

    private ICatgaMediator SetupIoTMediatorMock()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync(Arg.Any<IRequest<bool>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupMLMediatorMock()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync(Arg.Any<IRequest<double>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<double>>(CatgaResult<double>.Success(0.85)));

        mediator.SendAsync(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("model-123")));

        return mediator;
    }

    private ICatgaMediator SetupRecoveryMediatorMock()
    {
        return Substitute.For<ICatgaMediator>();
    }

    private void SetRecoveryMediatorToFail(ICatgaMediator mediator, string failStep)
    {
        mediator.SendAsync(Arg.Any<IRequest<bool>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Simplified - in real scenario would check step name
                return new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Failure("Simulated failure"));
            });
    }

    private void SetRecoveryMediatorToSucceed(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest<bool>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));
    }

    private ICatgaMediator SetupPerformanceMediatorMock()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("result")));

        return mediator;
    }
}

// The rest of the file would contain all the Flow configurations and State classes
// Due to length, I'll create them in a separate support file
