using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Saga pattern tests with comprehensive compensation scenarios.
/// Tests distributed transaction-like workflows with rollback.
/// </summary>
public class SagaCompensationFlowTests
{
    #region Test State

    public class BookingState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string BookingId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }

        // Booking stages
        public bool HotelBooked { get; set; }
        public bool FlightBooked { get; set; }
        public bool CarRented { get; set; }
        public bool PaymentProcessed { get; set; }

        // Compensation tracking
        public List<string> CompensatedSteps { get; set; } = new();
        public string? FailureReason { get; set; }

        // Confirmation codes
        public string? HotelConfirmation { get; set; }
        public string? FlightConfirmation { get; set; }
        public string? CarConfirmation { get; set; }
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
    public async Task Saga_AllServicesSucceed_CompletesWithoutCompensation()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = CreateBookingSagaFlow();
        var initialState = new BookingState
        {
            FlowId = $"booking-{Guid.NewGuid():N}",
            BookingId = "BK-001",
            CustomerId = "CUST-001",
            CheckIn = DateTime.UtcNow.AddDays(7),
            CheckOut = DateTime.UtcNow.AddDays(10)
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.HotelBooked.Should().BeTrue();
        result.State.FlightBooked.Should().BeTrue();
        result.State.CarRented.Should().BeTrue();
        result.State.PaymentProcessed.Should().BeTrue();
        result.State.CompensatedSteps.Should().BeEmpty();
        result.State.HotelConfirmation.Should().NotBeNullOrEmpty();
        result.State.FlightConfirmation.Should().NotBeNullOrEmpty();
        result.State.CarConfirmation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Saga_FlightFails_CompensatesHotel()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<BookingState>("saga-flight-fail")
            .Step("book-hotel", async (state, ct) =>
            {
                state.HotelBooked = true;
                state.HotelConfirmation = "HTL-001";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.HotelBooked = false;
                state.HotelConfirmation = null;
                state.CompensatedSteps.Add("hotel");
            })
            .Step("book-flight", async (state, ct) =>
            {
                // Simulate flight booking failure
                throw new InvalidOperationException("No flights available");
            })
            .WithCompensation(async (state, ct) =>
            {
                state.FlightBooked = false;
                state.CompensatedSteps.Add("flight");
            })
            .Build();

        var initialState = new BookingState { FlowId = "fail-flight" };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.HotelBooked.Should().BeFalse("hotel should be compensated");
        result.State.CompensatedSteps.Should().Contain("hotel");
    }

    [Fact]
    public async Task Saga_CarFails_CompensatesHotelAndFlight()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<BookingState>("saga-car-fail")
            .Step("book-hotel", async (state, ct) =>
            {
                state.HotelBooked = true;
                state.HotelConfirmation = "HTL-002";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.HotelBooked = false;
                state.HotelConfirmation = null;
                state.CompensatedSteps.Add("hotel");
            })
            .Step("book-flight", async (state, ct) =>
            {
                state.FlightBooked = true;
                state.FlightConfirmation = "FLT-002";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.FlightBooked = false;
                state.FlightConfirmation = null;
                state.CompensatedSteps.Add("flight");
            })
            .Step("rent-car", async (state, ct) =>
            {
                // Simulate car rental failure
                throw new InvalidOperationException("No cars available");
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CarRented = false;
                state.CompensatedSteps.Add("car");
            })
            .Build();

        var initialState = new BookingState { FlowId = "fail-car" };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.HotelBooked.Should().BeFalse();
        result.State.FlightBooked.Should().BeFalse();
        result.State.CompensatedSteps.Should().HaveCount(2);
        result.State.CompensatedSteps.Should().Contain("hotel");
        result.State.CompensatedSteps.Should().Contain("flight");
    }

    [Fact]
    public async Task Saga_PaymentFails_CompensatesAllBookings()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<BookingState>("saga-payment-fail")
            .Step("book-hotel", async (state, ct) =>
            {
                state.HotelBooked = true;
                state.HotelConfirmation = "HTL-003";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.HotelBooked = false;
                state.CompensatedSteps.Add("hotel");
            })
            .Step("book-flight", async (state, ct) =>
            {
                state.FlightBooked = true;
                state.FlightConfirmation = "FLT-003";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.FlightBooked = false;
                state.CompensatedSteps.Add("flight");
            })
            .Step("rent-car", async (state, ct) =>
            {
                state.CarRented = true;
                state.CarConfirmation = "CAR-003";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CarRented = false;
                state.CompensatedSteps.Add("car");
            })
            .Step("process-payment", async (state, ct) =>
            {
                // Simulate payment failure
                state.FailureReason = "Payment declined";
                throw new InvalidOperationException("Payment declined");
            })
            .Build();

        var initialState = new BookingState { FlowId = "fail-payment" };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.HotelBooked.Should().BeFalse();
        result.State.FlightBooked.Should().BeFalse();
        result.State.CarRented.Should().BeFalse();
        result.State.CompensatedSteps.Should().HaveCount(3);
    }

    [Fact]
    public async Task Saga_WithNestedTransactions_CompensatesInReverseOrder()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var executionOrder = new List<string>();

        var flow = FlowBuilder.Create<BookingState>("nested-saga")
            .Step("step-1", async (state, ct) =>
            {
                executionOrder.Add("exec-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                executionOrder.Add("comp-1");
            })
            .Step("step-2", async (state, ct) =>
            {
                executionOrder.Add("exec-2");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                executionOrder.Add("comp-2");
            })
            .Step("step-3", async (state, ct) =>
            {
                executionOrder.Add("exec-3");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                executionOrder.Add("comp-3");
            })
            .Step("step-4-fail", async (state, ct) =>
            {
                executionOrder.Add("exec-4");
                throw new InvalidOperationException("Intentional failure");
            })
            .Build();

        var initialState = new BookingState { FlowId = "nested" };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();

        // Verify execution order
        executionOrder.Should().StartWith(new[] { "exec-1", "exec-2", "exec-3", "exec-4" });

        // Verify compensation in reverse order (3, 2, 1)
        var compIndex3 = executionOrder.IndexOf("comp-3");
        var compIndex2 = executionOrder.IndexOf("comp-2");
        var compIndex1 = executionOrder.IndexOf("comp-1");

        compIndex3.Should().BeLessThan(compIndex2, "step-3 should compensate before step-2");
        compIndex2.Should().BeLessThan(compIndex1, "step-2 should compensate before step-1");
    }

    private IFlow<BookingState> CreateBookingSagaFlow()
    {
        return FlowBuilder.Create<BookingState>("travel-booking-saga")
            .Step("book-hotel", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                state.HotelBooked = true;
                state.HotelConfirmation = $"HTL-{Guid.NewGuid():N}"[..12];
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.HotelBooked = false;
                state.HotelConfirmation = null;
                state.CompensatedSteps.Add("hotel");
            })
            .Step("book-flight", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                state.FlightBooked = true;
                state.FlightConfirmation = $"FLT-{Guid.NewGuid():N}"[..12];
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.FlightBooked = false;
                state.FlightConfirmation = null;
                state.CompensatedSteps.Add("flight");
            })
            .Step("rent-car", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                state.CarRented = true;
                state.CarConfirmation = $"CAR-{Guid.NewGuid():N}"[..12];
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CarRented = false;
                state.CarConfirmation = null;
                state.CompensatedSteps.Add("car");
            })
            .Step("process-payment", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                state.PaymentProcessed = true;
                return true;
            })
            .Build();
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
