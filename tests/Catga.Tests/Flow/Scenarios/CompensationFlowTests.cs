using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Compensation flow scenario tests.
/// Tests rollback, compensation chains, and error recovery patterns.
/// </summary>
public class CompensationFlowTests
{
    #region Test State

    public class CompensationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> ExecutedSteps { get; set; } = new();
        public List<string> CompensatedSteps { get; set; } = new();
        public bool ShouldFail { get; set; }
        public int FailAtStep { get; set; } = -1;
        public string? ErrorMessage { get; set; }
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
    public async Task Compensation_SingleStep_ExecutesOnFailure()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CompensationState>("single-compensation")
            .Step("action", async (state, ct) =>
            {
                state.ExecutedSteps.Add("action");
                throw new InvalidOperationException("Action failed");
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("action-rollback");
            })
            .Build();

        var state = new CompensationState { FlowId = "single-comp-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.ExecutedSteps.Should().Contain("action");
        result.State.CompensatedSteps.Should().Contain("action-rollback");
    }

    [Fact]
    public async Task Compensation_MultipleSteps_ExecutesInReverseOrder()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CompensationState>("reverse-compensation")
            .Step("step-1", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-1");
            })
            .Step("step-2", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-2");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-2");
            })
            .Step("step-3", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-3");
                throw new InvalidOperationException("Step 3 failed");
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-3");
            })
            .Build();

        var state = new CompensationState { FlowId = "reverse-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.ExecutedSteps.Should().ContainInOrder("step-1", "step-2", "step-3");
        // Compensation should be in reverse order
        result.State.CompensatedSteps.Should().ContainInOrder("rollback-3", "rollback-2", "rollback-1");
    }

    [Fact]
    public async Task Compensation_NoFailure_NoCompensationExecuted()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CompensationState>("success-no-compensation")
            .Step("step-1", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-1");
            })
            .Step("step-2", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-2");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-2");
            })
            .Build();

        var state = new CompensationState { FlowId = "success-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedSteps.Should().HaveCount(2);
        result.State.CompensatedSteps.Should().BeEmpty();
    }

    [Fact]
    public async Task Compensation_PartialExecution_OnlyCompensatesExecutedSteps()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CompensationState>("partial-compensation")
            .Step("step-1", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-1");
            })
            .Step("step-2-fail", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-2");
                throw new InvalidOperationException("Failed at step 2");
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-2");
            })
            .Step("step-3-never", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-3");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-3");
            })
            .Build();

        var state = new CompensationState { FlowId = "partial-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.ExecutedSteps.Should().NotContain("step-3");
        // Only steps 1 and 2 should be compensated
        result.State.CompensatedSteps.Should().Contain("rollback-1");
        result.State.CompensatedSteps.Should().Contain("rollback-2");
        result.State.CompensatedSteps.Should().NotContain("rollback-3");
    }

    [Fact]
    public async Task Compensation_SagaPattern_RollsBackDistributedTransaction()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SagaState>("saga-rollback")
            // Service 1: Reserve Inventory
            .Step("reserve-inventory", async (state, ct) =>
            {
                state.ExecutedSteps.Add("reserve-inventory");
                state.InventoryReserved = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("release-inventory");
                state.InventoryReserved = false;
            })
            // Service 2: Process Payment
            .Step("process-payment", async (state, ct) =>
            {
                state.ExecutedSteps.Add("process-payment");
                state.PaymentProcessed = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("refund-payment");
                state.PaymentProcessed = false;
            })
            // Service 3: Create Shipment (fails)
            .Step("create-shipment", async (state, ct) =>
            {
                state.ExecutedSteps.Add("create-shipment");
                if (state.ShouldFailShipment)
                {
                    throw new InvalidOperationException("Shipment service unavailable");
                }
                state.ShipmentCreated = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("cancel-shipment");
                state.ShipmentCreated = false;
            })
            .Build();

        var state = new SagaState { FlowId = "saga-test", ShouldFailShipment = true };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        // All previous services should be rolled back
        result.State.InventoryReserved.Should().BeFalse();
        result.State.PaymentProcessed.Should().BeFalse();
        result.State.ShipmentCreated.Should().BeFalse();
        result.State.CompensatedSteps.Should().Contain("refund-payment");
        result.State.CompensatedSteps.Should().Contain("release-inventory");
    }

    [Fact]
    public async Task Compensation_WithCleanup_PerformsResourceCleanup()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ResourceState>("cleanup-compensation")
            .Step("acquire-resource", async (state, ct) =>
            {
                state.ExecutedSteps.Add("acquire");
                state.ResourceAcquired = true;
                state.ResourceId = "RES-001";
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("release-resource");
                state.ResourceAcquired = false;
                state.ResourceId = null;
            })
            .Step("use-resource", async (state, ct) =>
            {
                state.ExecutedSteps.Add("use");
                throw new InvalidOperationException("Error using resource");
            })
            .Build();

        var state = new ResourceState { FlowId = "cleanup-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.ResourceAcquired.Should().BeFalse();
        result.State.ResourceId.Should().BeNull();
    }

    [Fact]
    public async Task Compensation_NestedInBranch_ExecutesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CompensationState>("branch-compensation")
            .Step("init", async (state, ct) =>
            {
                state.ExecutedSteps.Add("init");
                return true;
            })
            .If(s => true)
                .Then(f => f
                    .Step("branch-step-1", async (state, ct) =>
                    {
                        state.ExecutedSteps.Add("branch-1");
                        return true;
                    })
                    .WithCompensation(async (state, ct) =>
                    {
                        state.CompensatedSteps.Add("rollback-branch-1");
                    })
                    .Step("branch-step-2-fail", async (state, ct) =>
                    {
                        state.ExecutedSteps.Add("branch-2");
                        throw new InvalidOperationException("Branch failed");
                    })
                    .WithCompensation(async (state, ct) =>
                    {
                        state.CompensatedSteps.Add("rollback-branch-2");
                    }))
            .EndIf()
            .Build();

        var state = new CompensationState { FlowId = "branch-comp-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.CompensatedSteps.Should().Contain("rollback-branch-2");
        result.State.CompensatedSteps.Should().Contain("rollback-branch-1");
    }

    [Fact]
    public async Task Compensation_ErrorInCompensation_ContinuesOtherCompensations()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CompensationState>("error-in-compensation")
            .Step("step-1", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-1");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-1");
            })
            .Step("step-2", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-2");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("rollback-2-error");
                throw new InvalidOperationException("Compensation error");
            })
            .Step("step-3-fail", async (state, ct) =>
            {
                throw new InvalidOperationException("Main error");
            })
            .Build();

        var state = new CompensationState { FlowId = "comp-error-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.CompensatedSteps.Should().Contain("rollback-2-error");
        // Rollback-1 should still execute despite rollback-2 throwing
        result.State.CompensatedSteps.Should().Contain("rollback-1");
    }

    [Fact]
    public async Task Compensation_WithStateRestore_RestoresPreviousState()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<AccountState>("state-restore")
            .Step("debit-account", async (state, ct) =>
            {
                state.ExecutedSteps.Add("debit");
                state.PreviousBalance = state.Balance;
                state.Balance -= state.TransferAmount;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.CompensatedSteps.Add("restore-balance");
                state.Balance = state.PreviousBalance;
            })
            .Step("credit-account", async (state, ct) =>
            {
                state.ExecutedSteps.Add("credit");
                throw new InvalidOperationException("Credit failed");
            })
            .Build();

        var state = new AccountState
        {
            FlowId = "restore-test",
            Balance = 1000m,
            TransferAmount = 500m
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.Balance.Should().Be(1000m); // Original balance restored
    }

    public class SagaState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> ExecutedSteps { get; set; } = new();
        public List<string> CompensatedSteps { get; set; } = new();
        public bool InventoryReserved { get; set; }
        public bool PaymentProcessed { get; set; }
        public bool ShipmentCreated { get; set; }
        public bool ShouldFailShipment { get; set; }
    }

    public class ResourceState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> ExecutedSteps { get; set; } = new();
        public List<string> CompensatedSteps { get; set; } = new();
        public bool ResourceAcquired { get; set; }
        public string? ResourceId { get; set; }
    }

    public class AccountState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> ExecutedSteps { get; set; } = new();
        public List<string> CompensatedSteps { get; set; } = new();
        public decimal Balance { get; set; }
        public decimal PreviousBalance { get; set; }
        public decimal TransferAmount { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
