using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Executor;

/// <summary>
/// TDD tests for DslFlowExecutor.
/// </summary>
public class DslFlowExecutorTests
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;

    public DslFlowExecutorTests()
    {
        _mediator = Substitute.For<ICatgaMediator>();
        _store = Substitute.For<IDslFlowStore>();
    }

    #region Basic Execution

    [Fact]
    public async Task RunAsync_NewFlow_ExecutesAllSteps()
    {
        // Arrange
        var config = new SimpleFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorSuccess<SaveOrderCommand>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(DslFlowStatus.Completed);
        await _mediator.Received(1).SendAsync(Arg.Any<SaveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WithQuery_StoresResultInState()
    {
        // Arrange
        var config = new QueryFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1", CustomerId = "cust-1" };
        var executor = CreateExecutor(config);

        SetupMediatorQueryResult<GetDiscountQuery, decimal>(10.5m);
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State!.Discount.Should().Be(10.5m);
    }

    [Fact]
    public async Task RunAsync_WithPublish_PublishesEvent()
    {
        // Arrange
        var config = new PublishFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorPublish<OrderCreatedEvent>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mediator.Received(1).PublishAsync(Arg.Any<OrderCreatedEvent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Failure and Compensation

    [Fact]
    public async Task RunAsync_StepFails_ExecutesCompensation()
    {
        // Arrange
        var config = new CompensationFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorFailure<SaveOrderCommand>("Save failed");
        SetupMediatorSuccess<DeleteOrderCommand>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(DslFlowStatus.Failed);
        await _mediator.Received(1).SendAsync(Arg.Any<DeleteOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MultipleStepsFail_ExecutesCompensationsInReverseOrder()
    {
        // Arrange
        var config = new MultiStepCompensationFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var compensationOrder = new List<string>();

        SetupMediatorSuccess<SaveOrderCommand>();
        SetupMediatorSuccess<ProcessPaymentCommand, string>("pay-1");
        SetupMediatorFailure<ShipOrderCommand>("Ship failed");

        _mediator.SendAsync(Arg.Any<RefundPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult.Success())
            .AndDoes(_ => compensationOrder.Add("refund"));

        _mediator.SendAsync(Arg.Any<DeleteOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult.Success())
            .AndDoes(_ => compensationOrder.Add("delete"));

        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        compensationOrder.Should().Equal("refund", "delete"); // Reverse order
    }

    [Fact]
    public async Task RunAsync_FailIfConditionMet_FailsStep()
    {
        // Arrange
        var config = new FailIfFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorSuccess<ProcessPaymentCommand, string>(string.Empty); // Empty = fail condition
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Payment failed");
    }

    #endregion

    #region Conditional Execution

    [Fact]
    public async Task RunAsync_OnlyWhenFalse_SkipsStep()
    {
        // Arrange
        var config = new ConditionalFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1", TotalAmount = 0 }; // Condition false
        var executor = CreateExecutor(config);

        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mediator.DidNotReceive().SendAsync(Arg.Any<SaveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OnlyWhenTrue_ExecutesStep()
    {
        // Arrange
        var config = new ConditionalFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1", TotalAmount = 100 }; // Condition true
        var executor = CreateExecutor(config);

        SetupMediatorSuccess<SaveOrderCommand>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mediator.Received(1).SendAsync(Arg.Any<SaveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OptionalStepFails_ContinuesExecution()
    {
        // Arrange
        var config = new OptionalStepFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorFailure<SendNotificationCommand>("Notification failed");
        SetupMediatorSuccess<SaveOrderCommand>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue(); // Optional step failure doesn't fail flow
    }

    #endregion

    #region Resume and Persistence

    [Fact]
    public async Task ResumeAsync_FromStep_ContinuesExecution()
    {
        // Arrange
        var config = new MultiStepFlowConfig();
        config.Build();

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var snapshot = new FlowSnapshot<SimpleFlowState>(
            "flow-1", state, 1, DslFlowStatus.Running, null, null, DateTime.UtcNow, DateTime.UtcNow, 1);

        _store.GetAsync<SimpleFlowState>("flow-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);

        SetupMediatorSuccess<ProcessPaymentCommand, string>("pay-1");
        SetupMediatorSuccess<ShipOrderCommand>();
        SetupStoreUpdate();

        // Act
        var result = await executor.ResumeAsync("flow-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Should skip step 0 and execute steps 1, 2
        await _mediator.DidNotReceive().SendAsync(Arg.Any<SaveOrderCommand>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).SendAsync<ProcessPaymentCommand, string>(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_CompletedFlow_ReturnsImmediately()
    {
        // Arrange
        var config = new SimpleFlowConfig();
        config.Build();

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var snapshot = new FlowSnapshot<SimpleFlowState>(
            "flow-1", state, 1, DslFlowStatus.Completed, null, null, DateTime.UtcNow, DateTime.UtcNow, 1);

        _store.GetAsync<SimpleFlowState>("flow-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);

        // Act
        var result = await executor.ResumeAsync("flow-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(DslFlowStatus.Completed);
        await _mediator.DidNotReceive().SendAsync(Arg.Any<SaveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_FailedFlow_ReturnsFailure()
    {
        // Arrange
        var config = new SimpleFlowConfig();
        config.Build();

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var snapshot = new FlowSnapshot<SimpleFlowState>(
            "flow-1", state, 0, DslFlowStatus.Failed, "Previous error", null, DateTime.UtcNow, DateTime.UtcNow, 1);

        _store.GetAsync<SimpleFlowState>("flow-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);

        // Act
        var result = await executor.ResumeAsync("flow-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Previous error");
    }

    [Fact]
    public async Task ResumeAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        var config = new SimpleFlowConfig();
        config.Build();
        var executor = CreateExecutor(config);

        _store.GetAsync<SimpleFlowState>("flow-1", Arg.Any<CancellationToken>())
            .Returns((FlowSnapshot<SimpleFlowState>?)null);

        // Act
        var result = await executor.ResumeAsync("flow-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    #endregion

    #region Event Hooks

    [Fact]
    public async Task RunAsync_OnFlowCompleted_PublishesEvent()
    {
        // Arrange
        var config = new HooksFlowConfig();
        config.Build();

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorSuccess<SaveOrderCommand>();
        SetupMediatorPublish<FlowCompletedEvent>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mediator.Received(1).PublishAsync(
            Arg.Is<FlowCompletedEvent>(e => e.FlowId == "flow-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OnFlowFailed_PublishesEvent()
    {
        // Arrange
        var config = new HooksFlowConfig();
        config.Build();

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorFailure<SaveOrderCommand>("Test error");
        SetupMediatorPublish<FlowFailedEvent>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        await _mediator.Received(1).PublishAsync(
            Arg.Is<FlowFailedEvent>(e => e.FlowId == "flow-1" && e.Error == "Test error"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OnStepCompleted_PublishesEventForEachStep()
    {
        // Arrange
        var config = new StepHooksFlowConfig();
        config.Build();

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorSuccess<SaveOrderCommand>();
        SetupMediatorSuccess<ProcessPaymentCommand, string>("pay-1");
        SetupMediatorPublish<StepCompletedEvent>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mediator.Received(2).PublishAsync(Arg.Any<StepCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task RunAsync_Cancelled_StopsExecution()
    {
        // Arrange
        var config = new MultiStepFlowConfig();
        config.Build();

        var state = new SimpleFlowState { OrderId = "order-1" };
        var executor = CreateExecutor(config);
        var cts = new CancellationTokenSource();

        _mediator.SendAsync(Arg.Any<SaveOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult.Success())
            .AndDoes(_ => cts.Cancel());

        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        var result = await executor.RunAsync(state, cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(DslFlowStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAsync_RunningFlow_CancelsFlow()
    {
        // Arrange
        var config = new SimpleFlowConfig();
        config.Build();
        var executor = CreateExecutor(config);

        var state = new SimpleFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = new FlowSnapshot<SimpleFlowState>(
            "flow-1", state, 0, DslFlowStatus.Running, null, null, DateTime.UtcNow, DateTime.UtcNow, 1);

        _store.GetAsync<SimpleFlowState>("flow-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        SetupStoreUpdate();

        // Act
        var result = await executor.CancelAsync("flow-1");

        // Assert
        result.Should().BeTrue();
        await _store.Received(1).UpdateAsync(
            Arg.Is<FlowSnapshot<SimpleFlowState>>(s => s.Status == DslFlowStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private DslFlowExecutor<SimpleFlowState, TConfig> CreateExecutor<TConfig>(TConfig config)
        where TConfig : FlowConfig<SimpleFlowState>
    {
        return new DslFlowExecutor<SimpleFlowState, TConfig>(_mediator, _store, config);
    }

    private void SetupMediatorSuccess<TRequest>() where TRequest : IRequest
    {
        _mediator.SendAsync(Arg.Any<TRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
    }

    private void SetupMediatorSuccess<TRequest, TResult>(TResult result) where TRequest : IRequest<TResult>
    {
        _mediator.SendAsync<TRequest, TResult>(Arg.Any<TRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TResult>>(CatgaResult<TResult>.Success(result)));
    }

    private void SetupMediatorQueryResult<TRequest, TResult>(TResult result) where TRequest : IRequest<TResult>
    {
        _mediator.SendAsync<TRequest, TResult>(Arg.Any<TRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TResult>>(CatgaResult<TResult>.Success(result)));
    }

    private void SetupMediatorFailure<TRequest>(string error) where TRequest : IRequest
    {
        _mediator.SendAsync(Arg.Any<TRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Failure(error)));
    }

    private void SetupMediatorPublish<TEvent>() where TEvent : IEvent
    {
        _mediator.PublishAsync(Arg.Any<TEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private void SetupStoreCreate()
    {
        _store.CreateAsync(Arg.Any<FlowSnapshot<SimpleFlowState>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
    }

    private void SetupStoreUpdate()
    {
        _store.UpdateAsync(Arg.Any<FlowSnapshot<SimpleFlowState>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
    }

    #endregion
}

#region Test Flow State

public class SimpleFlowState : IFlowState
{
    public const int Field_OrderId = 0;
    public const int Field_Discount = 1;
    public const int Field_PaymentId = 2;
    public const int FieldCount = 3;

    private int _changedMask;
    public string? FlowId { get; set; }

    private string? _orderId;
    public string? OrderId { get => _orderId; set { _orderId = value; MarkChanged(Field_OrderId); } }

    private decimal _discount;
    public decimal Discount { get => _discount; set { _discount = value; MarkChanged(Field_Discount); } }

    private string? _paymentId;
    public string? PaymentId { get => _paymentId; set { _paymentId = value; MarkChanged(Field_PaymentId); } }

    public decimal TotalAmount { get; set; }
    public string? CustomerId { get; set; }

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_OrderId)) yield return nameof(OrderId);
        if (IsFieldChanged(Field_Discount)) yield return nameof(Discount);
        if (IsFieldChanged(Field_PaymentId)) yield return nameof(PaymentId);
    }
}

#endregion

#region Test Commands/Events

public record SaveOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record DeleteOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record ProcessPaymentCommand(string OrderId, decimal Amount) : IRequest<string> { public long MessageId => 0; }
public record RefundPaymentCommand(string PaymentId) : IRequest { public long MessageId => 0; }
public record ShipOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record CancelShipmentCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record SendNotificationCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record GetDiscountQuery(string CustomerId) : IRequest<decimal> { public long MessageId => 0; }
public record OrderCreatedEvent(string OrderId) : IEvent { public long MessageId => 0; }
public record FlowCompletedEvent(string FlowId) : IEvent { public long MessageId => 0; }
public record FlowFailedEvent(string FlowId, string? Error) : IEvent { public long MessageId => 0; }
public record StepCompletedEvent(string FlowId, int Step) : IEvent { public long MessageId => 0; }

#endregion

#region Test Flow Configs

public class SimpleFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("simple-flow");
        flow.Send(s => new SaveOrderCommand(s.OrderId!));
    }
}

public class QueryFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("query-flow");
        flow.Query(s => new GetDiscountQuery(s.CustomerId!))
            .Into(s => s.Discount);
    }
}

public class PublishFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("publish-flow");
        flow.Publish(s => new OrderCreatedEvent(s.OrderId!));
    }
}

public class CompensationFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("compensation-flow");
        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .IfFail(s => new DeleteOrderCommand(s.OrderId!));
    }
}

public class MultiStepCompensationFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("multi-compensation-flow");

        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .IfFail(s => new DeleteOrderCommand(s.OrderId!));

        flow.Send<string>(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount))
            .Into(s => s.PaymentId)
            .IfFail(s => new RefundPaymentCommand(s.PaymentId!));

        flow.Send(s => new ShipOrderCommand(s.OrderId!))
            .IfFail(s => new CancelShipmentCommand(s.OrderId!));
    }
}

public class FailIfFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("failif-flow");
        flow.Send<string>(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount))
            .FailIf(p => string.IsNullOrEmpty(p), "Payment failed")
            .Into(s => s.PaymentId);
    }
}

public class ConditionalFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("conditional-flow");
        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .OnlyWhen(s => s.TotalAmount > 0);
    }
}

public class OptionalStepFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("optional-flow");

        flow.Send(s => new SendNotificationCommand(s.OrderId!))
            .Optional();

        flow.Send(s => new SaveOrderCommand(s.OrderId!));
    }
}

public class MultiStepFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("multi-step-flow");

        flow.Send(s => new SaveOrderCommand(s.OrderId!));

        flow.Send<string>(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount))
            .Into(s => s.PaymentId);

        flow.Send(s => new ShipOrderCommand(s.OrderId!));
    }
}

public class HooksFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("hooks-flow");

        flow.OnFlowCompleted<FlowCompletedEvent>(s => new FlowCompletedEvent(s.FlowId!));
        flow.OnFlowFailed<FlowFailedEvent>((s, error) => new FlowFailedEvent(s.FlowId!, error));

        flow.Send(s => new SaveOrderCommand(s.OrderId!));
    }
}

public class StepHooksFlowConfig : FlowConfig<SimpleFlowState>
{
    protected override void Configure(IFlowBuilder<SimpleFlowState> flow)
    {
        flow.Name("step-hooks-flow");

        flow.OnStepCompleted<StepCompletedEvent>((s, step) => new StepCompletedEvent(s.FlowId!, step));

        flow.Send(s => new SaveOrderCommand(s.OrderId!));

        flow.Send<string>(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount))
            .Into(s => s.PaymentId);
    }
}

#endregion
