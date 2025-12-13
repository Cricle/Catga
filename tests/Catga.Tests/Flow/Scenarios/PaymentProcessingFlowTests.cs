using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Payment processing flow scenarios with retry, timeout, and circuit breaker patterns.
/// </summary>
public class PaymentProcessingFlowTests
{
    #region Test State

    public class PaymentState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string PaymentId { get; set; } = "";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public string CustomerId { get; set; } = "";

        // Processing stages
        public bool FraudCheckPassed { get; set; }
        public bool PaymentAuthorized { get; set; }
        public bool PaymentCaptured { get; set; }
        public bool ReceiptSent { get; set; }

        // Gateway response
        public string? AuthorizationCode { get; set; }
        public string? TransactionId { get; set; }
        public string? FailureReason { get; set; }

        // Retry tracking
        public int RetryCount { get; set; }
    }

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PaymentFlow_SuccessfulPayment_CompletesAllStages()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<PaymentState>("payment-processing")
            .Step("fraud-check", async (state, ct) =>
            {
                // Simulate fraud check
                await Task.Delay(10, ct);
                state.FraudCheckPassed = state.Amount < 10000; // Simple rule
                return state.FraudCheckPassed;
            })
            .Step("authorize-payment", async (state, ct) =>
            {
                // Simulate payment authorization
                await Task.Delay(10, ct);
                state.AuthorizationCode = $"AUTH-{Guid.NewGuid():N}"[..12];
                state.PaymentAuthorized = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                // Void the authorization
                state.PaymentAuthorized = false;
                state.AuthorizationCode = null;
            })
            .Step("capture-payment", async (state, ct) =>
            {
                // Simulate payment capture
                await Task.Delay(10, ct);
                state.TransactionId = $"TXN-{Guid.NewGuid():N}"[..16];
                state.PaymentCaptured = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                // Refund the captured amount
                state.PaymentCaptured = false;
                state.TransactionId = null;
            })
            .Step("send-receipt", async (state, ct) =>
            {
                state.ReceiptSent = true;
                return true;
            })
            .Build();

        var initialState = new PaymentState
        {
            FlowId = $"pay-{Guid.NewGuid():N}",
            PaymentId = "PAY-001",
            Amount = 99.99m,
            CustomerId = "CUST-001"
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.FraudCheckPassed.Should().BeTrue();
        result.State.PaymentAuthorized.Should().BeTrue();
        result.State.PaymentCaptured.Should().BeTrue();
        result.State.ReceiptSent.Should().BeTrue();
        result.State.AuthorizationCode.Should().NotBeNullOrEmpty();
        result.State.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PaymentFlow_FraudDetected_StopsProcessing()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<PaymentState>("fraud-detection")
            .Step("fraud-check", async (state, ct) =>
            {
                // High amount triggers fraud detection
                state.FraudCheckPassed = state.Amount < 10000;
                if (!state.FraudCheckPassed)
                {
                    state.FailureReason = "Fraud detected: amount exceeds threshold";
                }
                return state.FraudCheckPassed;
            })
            .Step("authorize-payment", async (state, ct) =>
            {
                state.PaymentAuthorized = true;
                return true;
            })
            .Build();

        var initialState = new PaymentState
        {
            FlowId = "high-value-payment",
            Amount = 50000m
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.FraudCheckPassed.Should().BeFalse();
        result.State.PaymentAuthorized.Should().BeFalse("payment should not proceed after fraud detection");
        result.State.FailureReason.Should().Contain("Fraud detected");
    }

    [Fact]
    public async Task PaymentFlow_WithRetry_RetriesOnTransientFailure()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptCount = 0;

        var flow = FlowBuilder.Create<PaymentState>("payment-with-retry")
            .Step("authorize-with-retry", async (state, ct) =>
            {
                attemptCount++;
                state.RetryCount = attemptCount;

                // Fail first 2 attempts, succeed on 3rd
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException("Gateway timeout");
                }

                state.PaymentAuthorized = true;
                state.AuthorizationCode = "AUTH-SUCCESS";
                return true;
            })
            .WithRetry(3, TimeSpan.FromMilliseconds(10))
            .Build();

        var initialState = new PaymentState
        {
            FlowId = "retry-payment",
            Amount = 50m
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.PaymentAuthorized.Should().BeTrue();
        result.State.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task PaymentFlow_SwitchByPaymentMethod_SelectsCorrectProcessor()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var processorUsed = "";

        var state1 = new PaymentStateWithMethod { FlowId = "credit", PaymentMethod = "CREDIT_CARD", Amount = 100m };
        var state2 = new PaymentStateWithMethod { FlowId = "paypal", PaymentMethod = "PAYPAL", Amount = 100m };
        var state3 = new PaymentStateWithMethod { FlowId = "crypto", PaymentMethod = "CRYPTO", Amount = 100m };

        var flow = FlowBuilder.Create<PaymentStateWithMethod>("payment-method-switch")
            .Switch(s => s.PaymentMethod)
                .Case("CREDIT_CARD", f => f.Step("credit-card-processor", async (state, ct) =>
                {
                    processorUsed = "CreditCard";
                    state.ProcessorUsed = processorUsed;
                    return true;
                }))
                .Case("PAYPAL", f => f.Step("paypal-processor", async (state, ct) =>
                {
                    processorUsed = "PayPal";
                    state.ProcessorUsed = processorUsed;
                    return true;
                }))
                .Default(f => f.Step("default-processor", async (state, ct) =>
                {
                    processorUsed = "Default";
                    state.ProcessorUsed = processorUsed;
                    return true;
                }))
            .EndSwitch()
            .Build();

        // Act & Assert - Credit Card
        processorUsed = "";
        var result1 = await executor.ExecuteAsync(flow, state1);
        result1.IsSuccess.Should().BeTrue();
        result1.State.ProcessorUsed.Should().Be("CreditCard");

        // Act & Assert - PayPal
        processorUsed = "";
        var result2 = await executor.ExecuteAsync(flow, state2);
        result2.IsSuccess.Should().BeTrue();
        result2.State.ProcessorUsed.Should().Be("PayPal");

        // Act & Assert - Crypto (default)
        processorUsed = "";
        var result3 = await executor.ExecuteAsync(flow, state3);
        result3.IsSuccess.Should().BeTrue();
        result3.State.ProcessorUsed.Should().Be("Default");
    }

    public class PaymentStateWithMethod : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public decimal Amount { get; set; }
        public string? ProcessorUsed { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
