using System.Text.Json;
using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// End-to-end tests for distributed Flow scenarios.
/// </summary>
public class FlowE2ETests
{
    [Fact]
    public async Task E2E_OrderFlow_SuccessfulExecution()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var orderId = Guid.NewGuid().ToString();
        var steps = new List<string>();

        // Act
        var result = await executor.ExecuteAsync(
            $"order:{orderId}",
            "OrderFlow",
            JsonSerializer.SerializeToUtf8Bytes(new { OrderId = orderId }),
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("OrderFlow")
                    .Step(async c => { steps.Add("CheckInventory"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("SaveOrder"); await Task.Delay(10, c); },
                        async c => { steps.Add("CancelOrder"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ReserveStock"); await Task.Delay(10, c); },
                        async c => { steps.Add("ReleaseStock"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ProcessPayment"); await Task.Delay(10, c); },
                        async c => { steps.Add("RefundPayment"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ConfirmOrder"); await Task.Delay(10, c); });

                return await flow.ExecuteFromAsync(state.Step, ct);
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        steps.Should().BeEquivalentTo([
            "CheckInventory", "SaveOrder", "ReserveStock", "ProcessPayment", "ConfirmOrder"
        ]);
    }

    [Fact]
    public async Task E2E_OrderFlow_FailureWithCompensation()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var orderId = Guid.NewGuid().ToString();
        var steps = new List<string>();

        // Act
        var result = await executor.ExecuteAsync(
            $"order:{orderId}",
            "OrderFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("OrderFlow")
                    .Step(async c => { steps.Add("CheckInventory"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("SaveOrder"); await Task.Delay(10, c); },
                        async c => { steps.Add("CancelOrder"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ReserveStock"); await Task.Delay(10, c); },
                        async c => { steps.Add("ReleaseStock"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ProcessPayment"); throw new Exception("Payment failed"); },
                        async c => { steps.Add("RefundPayment"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("ConfirmOrder"); await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Payment failed");

        // Compensation should happen in reverse order
        steps.Should().ContainInOrder("CheckInventory", "SaveOrder", "ReserveStock", "ProcessPayment");
        steps.Should().Contain("ReleaseStock");
        steps.Should().Contain("CancelOrder");
        steps.IndexOf("ReleaseStock").Should().BeGreaterThan(steps.IndexOf("ProcessPayment"));
    }

    [Fact]
    public async Task E2E_FlowRecovery_ResumesFromLastStep()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store, new FlowOptions
        {
            NodeId = "new-node",
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });
        var flowId = "recovery-test";
        var steps = new List<string>();

        // Simulate a partially completed flow that was abandoned
        var state = new FlowState
        {
            Id = flowId,
            Type = "RecoveryFlow",
            Status = FlowStatus.Running,
            Step = 2, // Already completed 2 steps
            Version = 0,
            Owner = "crashed-node",
            HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds() // Abandoned
        };
        await store.CreateAsync(state);

        // Act - New node executes and should resume from step 2
        var result = await executor.ExecuteAsync(
            flowId,
            "RecoveryFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("RecoveryFlow")
                    .Step(async c => { steps.Add("Step1"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step2"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step3"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step4"); await Task.Delay(10, c); });

                return await flow.ExecuteFromAsync(s.Step, ct);
            });

        // Assert - Only steps 3 and 4 should execute
        result.IsSuccess.Should().BeTrue();
        steps.Should().BeEquivalentTo(["Step3", "Step4"]);
    }

    [Fact]
    public async Task E2E_ConcurrentFlowExecution_OnlyOneExecutes()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var flowId = "concurrent-test";
        var executionCount = 0;

        // Act - Multiple nodes try to execute the same flow
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var executor = new FlowExecutor(store, new FlowOptions { NodeId = $"node-{i}" });
            return await executor.ExecuteAsync(
                flowId,
                "ConcurrentFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    Interlocked.Increment(ref executionCount);
                    await Task.Delay(50, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert - Only one should actually execute
        executionCount.Should().Be(1);
        results.Count(r => r.IsSuccess).Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task E2E_FlowWithData_SerializesAndDeserializes()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var order = new TestOrder { Id = "order-123", Amount = 99.99m, Items = ["item1", "item2"] };
        var data = JsonSerializer.SerializeToUtf8Bytes(order);
        TestOrder? deserializedOrder = null;

        // Act
        var result = await executor.ExecuteAsync(
            "order:order-123",
            "OrderFlow",
            data,
            async (state, ct) =>
            {
                deserializedOrder = JsonSerializer.Deserialize<TestOrder>(state.Data);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        deserializedOrder.Should().NotBeNull();
        deserializedOrder!.Id.Should().Be("order-123");
        deserializedOrder.Amount.Should().Be(99.99m);
        deserializedOrder.Items.Should().BeEquivalentTo(["item1", "item2"]);
    }

    [Fact]
    public async Task E2E_MultipleFlowTypes_IndependentExecution()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor = new FlowExecutor(store);
        var orderSteps = new List<string>();
        var paymentSteps = new List<string>();

        // Act - Execute different flow types
        var orderTask = executor.ExecuteAsync(
            "order:1",
            "OrderFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                orderSteps.Add("OrderStep1");
                await Task.Delay(20, ct);
                orderSteps.Add("OrderStep2");
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        var paymentTask = executor.ExecuteAsync(
            "payment:1",
            "PaymentFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                paymentSteps.Add("PaymentStep1");
                await Task.Delay(20, ct);
                paymentSteps.Add("PaymentStep2");
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        await Task.WhenAll(orderTask, paymentTask);

        // Assert
        orderSteps.Should().BeEquivalentTo(["OrderStep1", "OrderStep2"]);
        paymentSteps.Should().BeEquivalentTo(["PaymentStep1", "PaymentStep2"]);
    }

    [Fact]
    public async Task E2E_HeartbeatPreventsClaimDuringExecution()
    {
        // Arrange
        var store = new InMemoryFlowStore();
        var executor1 = new FlowExecutor(store, new FlowOptions
        {
            NodeId = "node-1",
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            ClaimTimeout = TimeSpan.FromMilliseconds(200)
        });

        var flowId = "heartbeat-test";
        var claimAttempts = 0;

        // Act - Start a long-running flow
        var flowTask = executor1.ExecuteAsync(
            flowId,
            "HeartbeatFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                await Task.Delay(500, ct); // Long execution
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Try to claim while flow is running
        await Task.Delay(100);
        for (int i = 0; i < 3; i++)
        {
            var claimed = await store.TryClaimAsync("HeartbeatFlow", "node-2", 200);
            if (claimed != null) claimAttempts++;
            await Task.Delay(100);
        }

        await flowTask;

        // Assert - No claims should succeed while flow is running with heartbeat
        claimAttempts.Should().Be(0);
    }

    private record TestOrder
    {
        public string Id { get; init; } = "";
        public decimal Amount { get; init; }
        public string[] Items { get; init; } = [];
    }
}
