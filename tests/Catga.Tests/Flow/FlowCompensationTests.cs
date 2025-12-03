using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Tests for Flow compensation with [Compensation] attribute
/// </summary>
public sealed partial class FlowCompensationTests
{
    [Fact]
    public async Task Flow_WithCompensatableCommand_ShouldAutoCompensateOnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new CompensationTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<CreateOrderCmd, CreateOrderResult>, CreateOrderCmdHandler>();
        services.AddScoped<IRequestHandler<ReserveStockCmd, ReserveStockResult>, ReserveStockCmdHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentCmd, ProcessPaymentResult>, ProcessPaymentCmdHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCmd>, CancelOrderCmdHandler>();
        services.AddScoped<IRequestHandler<ReleaseStockCmd>, ReleaseStockCmdHandler>();
        services.AddScoped<IRequestHandler<RefundPaymentCmd>, RefundPaymentCmdHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("OrderFlow"))
        {
            // Step 1: Create order (success)
            var orderResult = await flow.ExecuteAsync<CreateOrderCmd, CreateOrderResult>(
                new CreateOrderCmd { MessageId = MessageExtensions.NewMessageId() });
            orderResult.IsSuccess.Should().BeTrue();

            // Manually register compensation since we're not using Source Generator in tests
            flow.RegisterCompensation(new CancelOrderCmd
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = orderResult.Value!.OrderId
            });

            // Step 2: Reserve stock (success)
            var stockResult = await flow.ExecuteAsync<ReserveStockCmd, ReserveStockResult>(
                new ReserveStockCmd { MessageId = MessageExtensions.NewMessageId(), OrderId = orderResult.Value.OrderId });
            stockResult.IsSuccess.Should().BeTrue();

            flow.RegisterCompensation(new ReleaseStockCmd
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = stockResult.Value!.ReservationId
            });

            // Step 3: Process payment (FAIL)
            var paymentResult = await flow.ExecuteAsync<ProcessPaymentCmd, ProcessPaymentResult>(
                new ProcessPaymentCmd
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    OrderId = orderResult.Value.OrderId,
                    ShouldFail = true
                });
            paymentResult.IsSuccess.Should().BeFalse();

            // Don't commit - let dispose handle compensation
        }

        // Assert - compensation should be executed in reverse order
        tracker.ExecutionOrder.Should().ContainInOrder(
            "CreateOrder",
            "ReserveStock",
            "ProcessPayment",
            "ReleaseStock",  // Compensation for ReserveStock
            "CancelOrder"    // Compensation for CreateOrder
        );
    }

    [Fact]
    public async Task Flow_WithAllSuccess_ShouldNotCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new CompensationTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<CreateOrderCmd, CreateOrderResult>, CreateOrderCmdHandler>();
        services.AddScoped<IRequestHandler<ReserveStockCmd, ReserveStockResult>, ReserveStockCmdHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCmd>, CancelOrderCmdHandler>();
        services.AddScoped<IRequestHandler<ReleaseStockCmd>, ReleaseStockCmdHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("OrderFlow"))
        {
            var orderResult = await flow.ExecuteAsync<CreateOrderCmd, CreateOrderResult>(
                new CreateOrderCmd { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(new CancelOrderCmd
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = orderResult.Value!.OrderId
            });

            var stockResult = await flow.ExecuteAsync<ReserveStockCmd, ReserveStockResult>(
                new ReserveStockCmd { MessageId = MessageExtensions.NewMessageId(), OrderId = orderResult.Value.OrderId });
            flow.RegisterCompensation(new ReleaseStockCmd
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = stockResult.Value!.ReservationId
            });

            flow.Commit(); // Success - no compensation
        }

        // Assert - no compensation should be executed
        tracker.ExecutionOrder.Should().ContainInOrder("CreateOrder", "ReserveStock");
        tracker.ExecutionOrder.Should().NotContain("CancelOrder");
        tracker.ExecutionOrder.Should().NotContain("ReleaseStock");
    }

    [Fact]
    public async Task FlowExtensions_ExecuteAsync_ShouldWorkWithoutFlowContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new CompensationTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<CreateOrderCmd, CreateOrderResult>, CreateOrderCmdHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Execute without flow context
        var result = await mediator.ExecuteAsync<CreateOrderCmd, CreateOrderResult>(
            new CreateOrderCmd { MessageId = MessageExtensions.NewMessageId() });

        // Assert
        result.IsSuccess.Should().BeTrue();
        tracker.ExecutionOrder.Should().Contain("CreateOrder");
    }

    #region Test Types

    private sealed class CompensationTracker
    {
        public List<string> ExecutionOrder { get; } = new();
    }

    // Commands
    [MemoryPackable]
    private partial record CreateOrderCmd : IRequest<CreateOrderResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record CreateOrderResult(string OrderId);

    [MemoryPackable]
    private partial record CancelOrderCmd : IRequest
    {
        public required long MessageId { get; init; }
        public string OrderId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record ReserveStockCmd : IRequest<ReserveStockResult>
    {
        public required long MessageId { get; init; }
        public string OrderId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record ReserveStockResult(string ReservationId);

    [MemoryPackable]
    private partial record ReleaseStockCmd : IRequest
    {
        public required long MessageId { get; init; }
        public string ReservationId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record ProcessPaymentCmd : IRequest<ProcessPaymentResult>
    {
        public required long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record ProcessPaymentResult(string PaymentId);

    [MemoryPackable]
    private partial record RefundPaymentCmd : IRequest
    {
        public required long MessageId { get; init; }
        public string PaymentId { get; init; } = "";
    }

    // Handlers
    private sealed class CreateOrderCmdHandler : IRequestHandler<CreateOrderCmd, CreateOrderResult>
    {
        private readonly CompensationTracker _tracker;
        public CreateOrderCmdHandler(CompensationTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<CreateOrderResult>> HandleAsync(CreateOrderCmd request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("CreateOrder");
            return Task.FromResult(CatgaResult<CreateOrderResult>.Success(
                new CreateOrderResult($"ORD-{Guid.NewGuid():N}")));
        }
    }

    private sealed class CancelOrderCmdHandler : IRequestHandler<CancelOrderCmd>
    {
        private readonly CompensationTracker _tracker;
        public CancelOrderCmdHandler(CompensationTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(CancelOrderCmd request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("CancelOrder");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class ReserveStockCmdHandler : IRequestHandler<ReserveStockCmd, ReserveStockResult>
    {
        private readonly CompensationTracker _tracker;
        public ReserveStockCmdHandler(CompensationTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<ReserveStockResult>> HandleAsync(ReserveStockCmd request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("ReserveStock");
            return Task.FromResult(CatgaResult<ReserveStockResult>.Success(
                new ReserveStockResult($"RES-{Guid.NewGuid():N}")));
        }
    }

    private sealed class ReleaseStockCmdHandler : IRequestHandler<ReleaseStockCmd>
    {
        private readonly CompensationTracker _tracker;
        public ReleaseStockCmdHandler(CompensationTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(ReleaseStockCmd request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("ReleaseStock");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class ProcessPaymentCmdHandler : IRequestHandler<ProcessPaymentCmd, ProcessPaymentResult>
    {
        private readonly CompensationTracker _tracker;
        public ProcessPaymentCmdHandler(CompensationTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<ProcessPaymentResult>> HandleAsync(ProcessPaymentCmd request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("ProcessPayment");
            if (request.ShouldFail)
                return Task.FromResult(CatgaResult<ProcessPaymentResult>.Failure("Payment failed"));
            return Task.FromResult(CatgaResult<ProcessPaymentResult>.Success(
                new ProcessPaymentResult($"PAY-{Guid.NewGuid():N}")));
        }
    }

    private sealed class RefundPaymentCmdHandler : IRequestHandler<RefundPaymentCmd>
    {
        private readonly CompensationTracker _tracker;
        public RefundPaymentCmdHandler(CompensationTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(RefundPaymentCmd request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("RefundPayment");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    #endregion
}
