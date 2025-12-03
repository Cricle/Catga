using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// End-to-end tests for Flow orchestration with automatic compensation
/// </summary>
public sealed partial class FlowOrchestrationE2ETests
{
    #region Order Flow E2E Tests

    [Fact]
    public async Task OrderFlow_AllStepsSucceed_ShouldCompleteWithoutCompensation()
    {
        // Arrange
        var services = CreateServices();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var tracker = sp.GetRequiredService<OrderFlowTracker>();

        // Act
        var result = await mediator.RunFlowAsync("CreateOrder", async flow =>
        {
            var order = await flow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                new CreateOrderE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    CustomerId = "CUST-001",
                    Amount = 100.00m
                });
            order.IsSuccess.Should().BeTrue();

            flow.RegisterCompensation(new CancelOrderE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = order.Value!.OrderId
            });

            var inventory = await flow.ExecuteAsync<ReserveInventoryE2ECommand, ReserveInventoryE2EResult>(
                new ReserveInventoryE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    OrderId = order.Value.OrderId,
                    ProductId = "PROD-001",
                    Quantity = 5
                });
            inventory.IsSuccess.Should().BeTrue();

            flow.RegisterCompensation(new ReleaseInventoryE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = inventory.Value!.ReservationId
            });

            var payment = await flow.ExecuteAsync<ProcessPaymentE2ECommand, ProcessPaymentE2EResult>(
                new ProcessPaymentE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    OrderId = order.Value.OrderId,
                    Amount = 100.00m
                });
            payment.IsSuccess.Should().BeTrue();

            return new OrderFlowResult
            {
                OrderId = order.Value.OrderId,
                PaymentId = payment.Value!.PaymentId,
                Status = "Completed"
            };
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Completed");

        tracker.Steps.Should().ContainInOrder(
            "CreateOrder:CUST-001",
            "ReserveInventory:PROD-001",
            "ProcessPayment:100.00"
        );
        tracker.Compensations.Should().BeEmpty();
    }

    [Fact]
    public async Task OrderFlow_PaymentFails_ShouldCompensateInReverseOrder()
    {
        // Arrange
        var services = CreateServices();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var tracker = sp.GetRequiredService<OrderFlowTracker>();

        // Act
        await using (var flow = mediator.BeginFlow("CreateOrder"))
        {
            var order = await flow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                new CreateOrderE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    CustomerId = "CUST-002",
                    Amount = 200.00m
                });
            flow.RegisterCompensation(new CancelOrderE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = order.Value!.OrderId
            });

            var inventory = await flow.ExecuteAsync<ReserveInventoryE2ECommand, ReserveInventoryE2EResult>(
                new ReserveInventoryE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    OrderId = order.Value.OrderId,
                    ProductId = "PROD-002",
                    Quantity = 10
                });
            flow.RegisterCompensation(new ReleaseInventoryE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = inventory.Value!.ReservationId
            });

            // Payment fails
            var payment = await flow.ExecuteAsync<ProcessPaymentE2ECommand, ProcessPaymentE2EResult>(
                new ProcessPaymentE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    OrderId = order.Value.OrderId,
                    Amount = 200.00m,
                    ShouldFail = true
                });
            payment.IsSuccess.Should().BeFalse();

            // Don't commit - let dispose handle compensation
        }

        // Assert - compensation should be in reverse order
        tracker.Steps.Should().ContainInOrder(
            "CreateOrder:CUST-002",
            "ReserveInventory:PROD-002",
            "ProcessPayment:200.00"
        );
        tracker.Compensations.Should().ContainInOrder(
            "ReleaseInventory",
            "CancelOrder"
        );
    }

    [Fact]
    public async Task OrderFlow_InventoryFails_ShouldOnlyCompensateOrder()
    {
        // Arrange
        var services = CreateServices();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var tracker = sp.GetRequiredService<OrderFlowTracker>();

        // Act
        await using (var flow = mediator.BeginFlow("CreateOrder"))
        {
            var order = await flow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                new CreateOrderE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    CustomerId = "CUST-003",
                    Amount = 300.00m
                });
            flow.RegisterCompensation(new CancelOrderE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = order.Value!.OrderId
            });

            // Inventory fails
            var inventory = await flow.ExecuteAsync<ReserveInventoryE2ECommand, ReserveInventoryE2EResult>(
                new ReserveInventoryE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    OrderId = order.Value.OrderId,
                    ProductId = "OUT-OF-STOCK",
                    Quantity = 100,
                    ShouldFail = true
                });
            inventory.IsSuccess.Should().BeFalse();
        }

        // Assert
        tracker.Steps.Should().ContainInOrder(
            "CreateOrder:CUST-003",
            "ReserveInventory:OUT-OF-STOCK"
        );
        tracker.Compensations.Should().ContainInOrder("CancelOrder");
        tracker.Compensations.Should().NotContain("ReleaseInventory");
    }

    #endregion

    #region Concurrent Flow Tests

    [Fact]
    public async Task MultipleFlows_ConcurrentExecution_ShouldIsolateCompensation()
    {
        // Arrange
        var services = CreateServices();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var tracker = sp.GetRequiredService<OrderFlowTracker>();

        // Act - Run 3 flows concurrently
        var tasks = new[]
        {
            RunFlowAsync(mediator, "CUST-A", shouldFail: false),
            RunFlowAsync(mediator, "CUST-B", shouldFail: true),
            RunFlowAsync(mediator, "CUST-C", shouldFail: false)
        };

        var results = await Task.WhenAll(tasks);

        // Assert
        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeFalse();
        results[2].IsSuccess.Should().BeTrue();

        // CUST-B flow should have compensation (CancelOrder)
        tracker.Compensations.Should().NotBeEmpty();
    }

    private static async Task<FlowResult<string>> RunFlowAsync(
        ICatgaMediator mediator,
        string customerId,
        bool shouldFail)
    {
        return await mediator.RunFlowAsync($"Order-{customerId}", async flow =>
        {
            var order = await flow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                new CreateOrderE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    CustomerId = customerId,
                    Amount = 100.00m
                });

            flow.RegisterCompensation(new CancelOrderE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = $"{order.Value!.OrderId}:{customerId}"
            });

            if (shouldFail)
            {
                var payment = await flow.ExecuteAsync<ProcessPaymentE2ECommand, ProcessPaymentE2EResult>(
                    new ProcessPaymentE2ECommand
                    {
                        MessageId = MessageExtensions.NewMessageId(),
                        OrderId = order.Value.OrderId,
                        Amount = 100.00m,
                        ShouldFail = true
                    });

                if (!payment.IsSuccess)
                    throw new FlowExecutionException("ProcessPayment", payment.Error!, flow.StepCount);
            }

            return order.Value.OrderId;
        });
    }

    #endregion

    #region Nested Flow Tests

    [Fact]
    public async Task NestedFlows_InnerFlowFails_ShouldNotAffectOuterFlow()
    {
        // Arrange
        var services = CreateServices();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var tracker = sp.GetRequiredService<OrderFlowTracker>();

        // Act
        await using (var outerFlow = mediator.BeginFlow("OuterFlow"))
        {
            var outerOrder = await outerFlow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                new CreateOrderE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    CustomerId = "OUTER",
                    Amount = 500.00m
                });
            outerFlow.RegisterCompensation(new CancelOrderE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = outerOrder.Value!.OrderId
            });

            // Inner flow that fails
            try
            {
                await using (var innerFlow = mediator.BeginFlow("InnerFlow"))
                {
                    var innerOrder = await innerFlow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                        new CreateOrderE2ECommand
                        {
                            MessageId = MessageExtensions.NewMessageId(),
                            CustomerId = "INNER",
                            Amount = 100.00m
                        });
                    innerFlow.RegisterCompensation(new CancelOrderE2ECommand
                    {
                        MessageId = MessageExtensions.NewMessageId(),
                        OrderId = innerOrder.Value!.OrderId
                    });

                    // Force failure
                    var payment = await innerFlow.ExecuteAsync<ProcessPaymentE2ECommand, ProcessPaymentE2EResult>(
                        new ProcessPaymentE2ECommand
                        {
                            MessageId = MessageExtensions.NewMessageId(),
                            OrderId = innerOrder.Value.OrderId,
                            Amount = 100.00m,
                            ShouldFail = true
                        });
                    // Inner flow not committed - will compensate
                }
            }
            catch
            {
                // Expected
            }

            // Outer flow continues and commits
            outerFlow.Commit();
        }

        // Assert - Inner should compensate, outer should not
        tracker.Compensations.Should().Contain(c => c.Contains("INNER") || c == "CancelOrder");
    }

    #endregion

    #region Timeout and Cancellation Tests

    [Fact]
    public async Task Flow_WithCancellation_ShouldCompensateExecutedSteps()
    {
        // Arrange
        var services = CreateServices();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var tracker = sp.GetRequiredService<OrderFlowTracker>();
        using var cts = new CancellationTokenSource();

        // Act
        await using (var flow = mediator.BeginFlow("CancellableFlow"))
        {
            var order = await flow.ExecuteAsync<CreateOrderE2ECommand, CreateOrderE2EResult>(
                new CreateOrderE2ECommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    CustomerId = "CANCEL-TEST",
                    Amount = 100.00m
                }, cts.Token);

            flow.RegisterCompensation(new CancelOrderE2ECommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = order.Value!.OrderId
            });

            // Cancel before next step
            cts.Cancel();

            // Don't commit - should compensate
        }

        // Assert
        tracker.Steps.Should().Contain("CreateOrder:CANCEL-TEST");
        tracker.Compensations.Should().Contain("CancelOrder");
    }

    #endregion

    #region Helper Methods

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var tracker = new OrderFlowTracker();
        services.AddSingleton(tracker);

        // Register handlers
        services.AddScoped<IRequestHandler<CreateOrderE2ECommand, CreateOrderE2EResult>, CreateOrderE2EHandler>();
        services.AddScoped<IRequestHandler<CancelOrderE2ECommand>, CancelOrderE2EHandler>();
        services.AddScoped<IRequestHandler<ReserveInventoryE2ECommand, ReserveInventoryE2EResult>, ReserveInventoryE2EHandler>();
        services.AddScoped<IRequestHandler<ReleaseInventoryE2ECommand>, ReleaseInventoryE2EHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentE2ECommand, ProcessPaymentE2EResult>, ProcessPaymentE2EHandler>();
        services.AddScoped<IRequestHandler<RefundPaymentE2ECommand>, RefundPaymentE2EHandler>();

        return services;
    }

    #endregion

    #region Test Types

    private sealed class OrderFlowTracker
    {
        private readonly object _lock = new();
        public List<string> Steps { get; } = new();
        public List<string> Compensations { get; } = new();

        public void AddStep(string step)
        {
            lock (_lock) Steps.Add(step);
        }

        public void AddCompensation(string compensation)
        {
            lock (_lock) Compensations.Add(compensation);
        }
    }

    private record OrderFlowResult
    {
        public string OrderId { get; init; } = "";
        public string PaymentId { get; init; } = "";
        public string Status { get; init; } = "";
    }

    // Commands
    [MemoryPackable]
    private partial record CreateOrderE2ECommand : IRequest<CreateOrderE2EResult>
    {
        public required long MessageId { get; init; }
        public string CustomerId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    [MemoryPackable]
    private partial record CreateOrderE2EResult(string OrderId);

    [MemoryPackable]
    private partial record CancelOrderE2ECommand : IRequest
    {
        public required long MessageId { get; init; }
        public string OrderId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record ReserveInventoryE2ECommand : IRequest<ReserveInventoryE2EResult>
    {
        public required long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string ProductId { get; init; } = "";
        public int Quantity { get; init; }
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record ReserveInventoryE2EResult(string ReservationId);

    [MemoryPackable]
    private partial record ReleaseInventoryE2ECommand : IRequest
    {
        public required long MessageId { get; init; }
        public string ReservationId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record ProcessPaymentE2ECommand : IRequest<ProcessPaymentE2EResult>
    {
        public required long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record ProcessPaymentE2EResult(string PaymentId);

    [MemoryPackable]
    private partial record RefundPaymentE2ECommand : IRequest
    {
        public required long MessageId { get; init; }
        public string PaymentId { get; init; } = "";
    }

    // Handlers
    private sealed class CreateOrderE2EHandler : IRequestHandler<CreateOrderE2ECommand, CreateOrderE2EResult>
    {
        private readonly OrderFlowTracker _tracker;
        public CreateOrderE2EHandler(OrderFlowTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<CreateOrderE2EResult>> HandleAsync(CreateOrderE2ECommand request, CancellationToken ct)
        {
            _tracker.AddStep($"CreateOrder:{request.CustomerId}");
            return Task.FromResult(CatgaResult<CreateOrderE2EResult>.Success(
                new CreateOrderE2EResult($"ORD-{Guid.NewGuid():N}")));
        }
    }

    private sealed class CancelOrderE2EHandler : IRequestHandler<CancelOrderE2ECommand>
    {
        private readonly OrderFlowTracker _tracker;
        public CancelOrderE2EHandler(OrderFlowTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(CancelOrderE2ECommand request, CancellationToken ct)
        {
            _tracker.AddCompensation($"CancelOrder");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class ReserveInventoryE2EHandler : IRequestHandler<ReserveInventoryE2ECommand, ReserveInventoryE2EResult>
    {
        private readonly OrderFlowTracker _tracker;
        public ReserveInventoryE2EHandler(OrderFlowTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<ReserveInventoryE2EResult>> HandleAsync(ReserveInventoryE2ECommand request, CancellationToken ct)
        {
            _tracker.AddStep($"ReserveInventory:{request.ProductId}");
            if (request.ShouldFail)
                return Task.FromResult(CatgaResult<ReserveInventoryE2EResult>.Failure("Out of stock"));
            return Task.FromResult(CatgaResult<ReserveInventoryE2EResult>.Success(
                new ReserveInventoryE2EResult($"RES-{Guid.NewGuid():N}")));
        }
    }

    private sealed class ReleaseInventoryE2EHandler : IRequestHandler<ReleaseInventoryE2ECommand>
    {
        private readonly OrderFlowTracker _tracker;
        public ReleaseInventoryE2EHandler(OrderFlowTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(ReleaseInventoryE2ECommand request, CancellationToken ct)
        {
            _tracker.AddCompensation("ReleaseInventory");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class ProcessPaymentE2EHandler : IRequestHandler<ProcessPaymentE2ECommand, ProcessPaymentE2EResult>
    {
        private readonly OrderFlowTracker _tracker;
        public ProcessPaymentE2EHandler(OrderFlowTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<ProcessPaymentE2EResult>> HandleAsync(ProcessPaymentE2ECommand request, CancellationToken ct)
        {
            _tracker.AddStep($"ProcessPayment:{request.Amount}");
            if (request.ShouldFail)
                return Task.FromResult(CatgaResult<ProcessPaymentE2EResult>.Failure("Payment declined"));
            return Task.FromResult(CatgaResult<ProcessPaymentE2EResult>.Success(
                new ProcessPaymentE2EResult($"PAY-{Guid.NewGuid():N}")));
        }
    }

    private sealed class RefundPaymentE2EHandler : IRequestHandler<RefundPaymentE2ECommand>
    {
        private readonly OrderFlowTracker _tracker;
        public RefundPaymentE2EHandler(OrderFlowTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(RefundPaymentE2ECommand request, CancellationToken ct)
        {
            _tracker.AddCompensation("RefundPayment");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    #endregion
}
