using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E tests for saga-like distributed transaction patterns
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class SagaE2ETests
{
    [Fact]
    public async Task OrderSaga_HappyPath_AllStepsComplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var sagaState = new SagaState();
        services.AddSingleton(sagaState);
        services.AddScoped<IRequestHandler<CreateOrderSagaCommand, SagaStepResult>, CreateOrderSagaHandler>();
        services.AddScoped<IRequestHandler<ReserveInventorySagaCommand, SagaStepResult>, ReserveInventorySagaHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentSagaCommand, SagaStepResult>, ProcessPaymentSagaHandler>();
        services.AddScoped<IRequestHandler<ShipOrderSagaCommand, SagaStepResult>, ShipOrderSagaHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var sagaId = Guid.NewGuid().ToString();

        // Act - Execute saga steps
        var createResult = await mediator.SendAsync<CreateOrderSagaCommand, SagaStepResult>(
            new CreateOrderSagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, CustomerId = "CUST-001" });
        createResult.IsSuccess.Should().BeTrue();

        var reserveResult = await mediator.SendAsync<ReserveInventorySagaCommand, SagaStepResult>(
            new ReserveInventorySagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, ProductId = "PROD-001", Quantity = 5 });
        reserveResult.IsSuccess.Should().BeTrue();

        var paymentResult = await mediator.SendAsync<ProcessPaymentSagaCommand, SagaStepResult>(
            new ProcessPaymentSagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, Amount = 99.99m });
        paymentResult.IsSuccess.Should().BeTrue();

        var shipResult = await mediator.SendAsync<ShipOrderSagaCommand, SagaStepResult>(
            new ShipOrderSagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, Address = "123 Main St" });
        shipResult.IsSuccess.Should().BeTrue();

        // Assert
        sagaState.CompletedSteps.Should().HaveCount(4);
        sagaState.CompletedSteps.Should().ContainInOrder("CreateOrder", "ReserveInventory", "ProcessPayment", "ShipOrder");
    }

    [Fact]
    public async Task OrderSaga_PaymentFails_CompensationExecuted()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var sagaState = new SagaState { ShouldFailPayment = true };
        services.AddSingleton(sagaState);
        services.AddScoped<IRequestHandler<CreateOrderSagaCommand, SagaStepResult>, CreateOrderSagaHandler>();
        services.AddScoped<IRequestHandler<ReserveInventorySagaCommand, SagaStepResult>, ReserveInventorySagaHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentSagaCommand, SagaStepResult>, ProcessPaymentSagaHandler>();
        services.AddScoped<IRequestHandler<CompensateInventoryCommand, SagaStepResult>, CompensateInventoryHandler>();
        services.AddScoped<IRequestHandler<CompensateOrderCommand, SagaStepResult>, CompensateOrderHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var sagaId = Guid.NewGuid().ToString();

        // Act - Execute saga steps
        var createResult = await mediator.SendAsync<CreateOrderSagaCommand, SagaStepResult>(
            new CreateOrderSagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, CustomerId = "CUST-001" });
        createResult.IsSuccess.Should().BeTrue();

        var reserveResult = await mediator.SendAsync<ReserveInventorySagaCommand, SagaStepResult>(
            new ReserveInventorySagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, ProductId = "PROD-001", Quantity = 5 });
        reserveResult.IsSuccess.Should().BeTrue();

        var paymentResult = await mediator.SendAsync<ProcessPaymentSagaCommand, SagaStepResult>(
            new ProcessPaymentSagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, Amount = 99.99m });
        paymentResult.IsSuccess.Should().BeFalse();

        // Execute compensation
        var compensateInventory = await mediator.SendAsync<CompensateInventoryCommand, SagaStepResult>(
            new CompensateInventoryCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId });
        compensateInventory.IsSuccess.Should().BeTrue();

        var compensateOrder = await mediator.SendAsync<CompensateOrderCommand, SagaStepResult>(
            new CompensateOrderCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId });
        compensateOrder.IsSuccess.Should().BeTrue();

        // Assert
        sagaState.CompletedSteps.Should().Contain("CreateOrder");
        sagaState.CompletedSteps.Should().Contain("ReserveInventory");
        sagaState.CompensatedSteps.Should().Contain("CompensateInventory");
        sagaState.CompensatedSteps.Should().Contain("CompensateOrder");
    }

    [Fact]
    public async Task ParallelSagas_IndependentExecution_NoInterference()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var sagaState = new SagaState();
        services.AddSingleton(sagaState);
        services.AddScoped<IRequestHandler<CreateOrderSagaCommand, SagaStepResult>, CreateOrderSagaHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        const int sagaCount = 10;

        // Act - Execute multiple sagas in parallel
        var tasks = Enumerable.Range(0, sagaCount).Select(async i =>
        {
            var sagaId = $"saga-{i}";
            return await mediator.SendAsync<CreateOrderSagaCommand, SagaStepResult>(
                new CreateOrderSagaCommand { MessageId = MessageExtensions.NewMessageId(), SagaId = sagaId, CustomerId = $"CUST-{i}" });
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        sagaState.CompletedSteps.Count.Should().Be(sagaCount);
    }

    #region Saga Types

    private class SagaState
    {
        public List<string> CompletedSteps { get; } = new();
        public List<string> CompensatedSteps { get; } = new();
        public bool ShouldFailPayment { get; set; }
    }

    [MemoryPackable]
    private partial record SagaStepResult
    {
        public required string StepName { get; init; }
        public required bool Success { get; init; }
    }

    [MemoryPackable]
    private partial record CreateOrderSagaCommand : IRequest<SagaStepResult>
    {
        public required long MessageId { get; init; }
        public required string SagaId { get; init; }
        public required string CustomerId { get; init; }
    }

    private sealed class CreateOrderSagaHandler(SagaState state) : IRequestHandler<CreateOrderSagaCommand, SagaStepResult>
    {
        public ValueTask<CatgaResult<SagaStepResult>> HandleAsync(CreateOrderSagaCommand request, CancellationToken ct = default)
        {
            lock (state.CompletedSteps)
            {
                state.CompletedSteps.Add("CreateOrder");
            }
            return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Success(new SagaStepResult { StepName = "CreateOrder", Success = true }));
        }
    }

    [MemoryPackable]
    private partial record ReserveInventorySagaCommand : IRequest<SagaStepResult>
    {
        public required long MessageId { get; init; }
        public required string SagaId { get; init; }
        public required string ProductId { get; init; }
        public required int Quantity { get; init; }
    }

    private sealed class ReserveInventorySagaHandler(SagaState state) : IRequestHandler<ReserveInventorySagaCommand, SagaStepResult>
    {
        public ValueTask<CatgaResult<SagaStepResult>> HandleAsync(ReserveInventorySagaCommand request, CancellationToken ct = default)
        {
            lock (state.CompletedSteps)
            {
                state.CompletedSteps.Add("ReserveInventory");
            }
            return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Success(new SagaStepResult { StepName = "ReserveInventory", Success = true }));
        }
    }

    [MemoryPackable]
    private partial record ProcessPaymentSagaCommand : IRequest<SagaStepResult>
    {
        public required long MessageId { get; init; }
        public required string SagaId { get; init; }
        public required decimal Amount { get; init; }
    }

    private sealed class ProcessPaymentSagaHandler(SagaState state) : IRequestHandler<ProcessPaymentSagaCommand, SagaStepResult>
    {
        public ValueTask<CatgaResult<SagaStepResult>> HandleAsync(ProcessPaymentSagaCommand request, CancellationToken ct = default)
        {
            if (state.ShouldFailPayment)
            {
                return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Failure("Payment declined"));
            }
            lock (state.CompletedSteps)
            {
                state.CompletedSteps.Add("ProcessPayment");
            }
            return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Success(new SagaStepResult { StepName = "ProcessPayment", Success = true }));
        }
    }

    [MemoryPackable]
    private partial record ShipOrderSagaCommand : IRequest<SagaStepResult>
    {
        public required long MessageId { get; init; }
        public required string SagaId { get; init; }
        public required string Address { get; init; }
    }

    private sealed class ShipOrderSagaHandler(SagaState state) : IRequestHandler<ShipOrderSagaCommand, SagaStepResult>
    {
        public ValueTask<CatgaResult<SagaStepResult>> HandleAsync(ShipOrderSagaCommand request, CancellationToken ct = default)
        {
            lock (state.CompletedSteps)
            {
                state.CompletedSteps.Add("ShipOrder");
            }
            return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Success(new SagaStepResult { StepName = "ShipOrder", Success = true }));
        }
    }

    [MemoryPackable]
    private partial record CompensateInventoryCommand : IRequest<SagaStepResult>
    {
        public required long MessageId { get; init; }
        public required string SagaId { get; init; }
    }

    private sealed class CompensateInventoryHandler(SagaState state) : IRequestHandler<CompensateInventoryCommand, SagaStepResult>
    {
        public ValueTask<CatgaResult<SagaStepResult>> HandleAsync(CompensateInventoryCommand request, CancellationToken ct = default)
        {
            lock (state.CompensatedSteps)
            {
                state.CompensatedSteps.Add("CompensateInventory");
            }
            return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Success(new SagaStepResult { StepName = "CompensateInventory", Success = true }));
        }
    }

    [MemoryPackable]
    private partial record CompensateOrderCommand : IRequest<SagaStepResult>
    {
        public required long MessageId { get; init; }
        public required string SagaId { get; init; }
    }

    private sealed class CompensateOrderHandler(SagaState state) : IRequestHandler<CompensateOrderCommand, SagaStepResult>
    {
        public ValueTask<CatgaResult<SagaStepResult>> HandleAsync(CompensateOrderCommand request, CancellationToken ct = default)
        {
            lock (state.CompensatedSteps)
            {
                state.CompensatedSteps.Add("CompensateOrder");
            }
            return new ValueTask<CatgaResult<SagaStepResult>>(CatgaResult<SagaStepResult>.Success(new SagaStepResult { StepName = "CompensateOrder", Success = true }));
        }
    }

    #endregion
}



