using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.WhenAllAny;

/// <summary>
/// TDD tests for WhenAll/WhenAny distributed coordination.
/// </summary>
public class WhenAllWhenAnyTests
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;

    public WhenAllWhenAnyTests()
    {
        _mediator = Substitute.For<ICatgaMediator>();
        _store = Substitute.For<IDslFlowStore>();
    }

    #region WhenAll Basic

    [Fact]
    public async Task WhenAll_StartsChildFlows_SuspendsParentFlow()
    {
        // Arrange
        var config = new WhenAllFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        SetupMediatorSuccess<StartPaymentFlowCommand>();
        SetupMediatorSuccess<StartInventoryFlowCommand>();
        SetupStoreCreate();
        SetupStoreUpdate();
        _store.SetWaitConditionAsync(Arg.Any<string>(), Arg.Any<WaitCondition>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        await _mediator.Received(1).SendAsync(Arg.Any<StartPaymentFlowCommand>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).SendAsync(Arg.Any<StartInventoryFlowCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenAll_AllChildrenComplete_ResumesParentFlow()
    {
        // Arrange
        var config = new WhenAllFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        // Simulate suspended state with completed wait condition
        var waitCondition = new WaitCondition
        {
            CorrelationId = "parent-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 2,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            FlowId = "parent-1",
            FlowType = "WhenAllFlowConfig",
            Step = 0,
            ChildFlowIds = ["child-1", "child-2"],
            Results = [
                new FlowCompletedEventData { FlowId = "child-1", Success = true },
                new FlowCompletedEventData { FlowId = "child-2", Success = true }
            ]
        };

        var snapshot = new FlowSnapshot<OrderFlowState>(
            "parent-1", state, 0, DslFlowStatus.Suspended, null, waitCondition,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 1);

        _store.GetAsync<OrderFlowState>("parent-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _store.GetWaitConditionAsync("parent-1-step-0", Arg.Any<CancellationToken>())
            .Returns(waitCondition);
        SetupStoreUpdate();
        SetupMediatorSuccess<CompleteOrderCommand>();

        // Act
        var result = await executor.ResumeAsync("parent-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task WhenAll_ChildFails_ExecutesCompensation()
    {
        // Arrange
        var config = new WhenAllWithCompensationFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var waitCondition = new WaitCondition
        {
            CorrelationId = "parent-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 2,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            FlowId = "parent-1",
            FlowType = "WhenAllWithCompensationFlowConfig",
            Step = 0,
            ChildFlowIds = ["child-1", "child-2"],
            Results = [
                new FlowCompletedEventData { FlowId = "child-1", Success = true },
                new FlowCompletedEventData { FlowId = "child-2", Success = false, Error = "Inventory failed" }
            ]
        };

        var snapshot = new FlowSnapshot<OrderFlowState>(
            "parent-1", state, 0, DslFlowStatus.Suspended, null, waitCondition,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 1);

        _store.GetAsync<OrderFlowState>("parent-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _store.GetWaitConditionAsync("parent-1-step-0", Arg.Any<CancellationToken>())
            .Returns(waitCondition);
        SetupStoreUpdate();
        SetupMediatorSuccess<CancelOrderCommand>();

        // Act
        var result = await executor.ResumeAsync("parent-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(DslFlowStatus.Failed);
        await _mediator.Received(1).SendAsync(Arg.Any<CancelOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenAll_Timeout_FailsFlow()
    {
        // Arrange
        var config = new WhenAllFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var waitCondition = new WaitCondition
        {
            CorrelationId = "parent-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 1, // Only 1 completed
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10), // Timed out
            FlowId = "parent-1",
            FlowType = "WhenAllFlowConfig",
            Step = 0,
            ChildFlowIds = ["child-1", "child-2"],
            Results = [new FlowCompletedEventData { FlowId = "child-1", Success = true }]
        };

        var snapshot = new FlowSnapshot<OrderFlowState>(
            "parent-1", state, 0, DslFlowStatus.Suspended, null, waitCondition,
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, 1);

        _store.GetAsync<OrderFlowState>("parent-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _store.GetWaitConditionAsync("parent-1-step-0", Arg.Any<CancellationToken>())
            .Returns(waitCondition);
        SetupStoreUpdate();

        // Act
        var result = await executor.ResumeAsync("parent-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("timeout");
    }

    #endregion

    #region WhenAny Basic

    [Fact]
    public async Task WhenAny_FirstChildCompletes_ResumesParentFlow()
    {
        // Arrange
        var config = new WhenAnyFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var waitCondition = new WaitCondition
        {
            CorrelationId = "parent-1-step-0",
            Type = WaitType.Any,
            ExpectedCount = 2,
            CompletedCount = 1, // Only 1 needed for WhenAny
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            FlowId = "parent-1",
            FlowType = "WhenAnyFlowConfig",
            Step = 0,
            CancelOthers = true,
            ChildFlowIds = ["child-1", "child-2"],
            Results = [new FlowCompletedEventData { FlowId = "child-1", Success = true, Result = "pay-123" }]
        };

        var snapshot = new FlowSnapshot<OrderFlowState>(
            "parent-1", state, 0, DslFlowStatus.Suspended, null, waitCondition,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 1);

        _store.GetAsync<OrderFlowState>("parent-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _store.GetWaitConditionAsync("parent-1-step-0", Arg.Any<CancellationToken>())
            .Returns(waitCondition);
        SetupStoreUpdate();

        // Act
        var result = await executor.ResumeAsync("parent-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task WhenAny_WithResult_StoresFirstResult()
    {
        // Arrange
        var config = new WhenAnyWithResultFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var waitCondition = new WaitCondition
        {
            CorrelationId = "parent-1-step-0",
            Type = WaitType.Any,
            ExpectedCount = 2,
            CompletedCount = 1,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            FlowId = "parent-1",
            FlowType = "WhenAnyWithResultFlowConfig",
            Step = 0,
            ChildFlowIds = ["child-1", "child-2"],
            Results = [new FlowCompletedEventData { FlowId = "child-1", Success = true, Result = "pay-primary-123" }]
        };

        var snapshot = new FlowSnapshot<OrderFlowState>(
            "parent-1", state, 0, DslFlowStatus.Suspended, null, waitCondition,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 1);

        _store.GetAsync<OrderFlowState>("parent-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _store.GetWaitConditionAsync("parent-1-step-0", Arg.Any<CancellationToken>())
            .Returns(waitCondition);
        SetupStoreUpdate();

        // Act
        var result = await executor.ResumeAsync("parent-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State!.PaymentId.Should().Be("pay-primary-123");
    }

    [Fact]
    public async Task WhenAny_AllFail_FailsFlow()
    {
        // Arrange
        var config = new WhenAnyFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        var waitCondition = new WaitCondition
        {
            CorrelationId = "parent-1-step-0",
            Type = WaitType.Any,
            ExpectedCount = 2,
            CompletedCount = 2, // Both completed but both failed
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            FlowId = "parent-1",
            FlowType = "WhenAnyFlowConfig",
            Step = 0,
            ChildFlowIds = ["child-1", "child-2"],
            Results = [
                new FlowCompletedEventData { FlowId = "child-1", Success = false, Error = "Primary failed" },
                new FlowCompletedEventData { FlowId = "child-2", Success = false, Error = "Backup failed" }
            ]
        };

        var snapshot = new FlowSnapshot<OrderFlowState>(
            "parent-1", state, 0, DslFlowStatus.Suspended, null, waitCondition,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 1);

        _store.GetAsync<OrderFlowState>("parent-1", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        _store.GetWaitConditionAsync("parent-1-step-0", Arg.Any<CancellationToken>())
            .Returns(waitCondition);
        SetupStoreUpdate();

        // Act
        var result = await executor.ResumeAsync("parent-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(DslFlowStatus.Failed);
    }

    #endregion

    #region WaitCondition Management

    [Fact]
    public async Task WhenAll_CreatesWaitCondition_WithCorrectData()
    {
        // Arrange
        var config = new WhenAllFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        WaitCondition? capturedCondition = null;
        _store.SetWaitConditionAsync(Arg.Any<string>(), Arg.Do<WaitCondition>(c => capturedCondition = c), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        SetupMediatorSuccess<StartPaymentFlowCommand>();
        SetupMediatorSuccess<StartInventoryFlowCommand>();
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        await executor.RunAsync(state);

        // Assert
        capturedCondition.Should().NotBeNull();
        capturedCondition!.Type.Should().Be(WaitType.All);
        capturedCondition.ExpectedCount.Should().Be(2);
        capturedCondition.FlowId.Should().Be("parent-1");
        capturedCondition.Step.Should().Be(0);
    }

    [Fact]
    public async Task WhenAny_CreatesWaitCondition_WithCancelOthers()
    {
        // Arrange
        var config = new WhenAnyFlowConfig();
        config.Build();

        var state = new OrderFlowState { FlowId = "parent-1", OrderId = "order-1" };
        var executor = CreateExecutor(config);

        WaitCondition? capturedCondition = null;
        _store.SetWaitConditionAsync(Arg.Any<string>(), Arg.Do<WaitCondition>(c => capturedCondition = c), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        SetupMediatorSuccess<PrimaryPaymentCommand, string>("pay-1");
        SetupMediatorSuccess<BackupPaymentCommand, string>("pay-2");
        SetupStoreCreate();
        SetupStoreUpdate();

        // Act
        await executor.RunAsync(state);

        // Assert
        capturedCondition.Should().NotBeNull();
        capturedCondition!.Type.Should().Be(WaitType.Any);
        capturedCondition.CancelOthers.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private DslFlowExecutor<OrderFlowState, TConfig> CreateExecutor<TConfig>(TConfig config)
        where TConfig : FlowConfig<OrderFlowState>
    {
        return new DslFlowExecutor<OrderFlowState, TConfig>(_mediator, _store, config);
    }

    private void SetupMediatorSuccess<TRequest>() where TRequest : IRequest
    {
        _mediator.SendAsync(Arg.Any<TRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult.Success()));
    }

    private void SetupMediatorSuccess<TRequest, TResult>(TResult result) where TRequest : IRequest<TResult>
    {
        _mediator.SendAsync<TRequest, TResult>(Arg.Any<TRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult<TResult>.Success(result)));
    }

    private void SetupStoreCreate()
    {
        _store.CreateAsync(Arg.Any<FlowSnapshot<OrderFlowState>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
    }

    private void SetupStoreUpdate()
    {
        _store.UpdateAsync(Arg.Any<FlowSnapshot<OrderFlowState>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
    }

    #endregion
}

#region Test Flow State

public class OrderFlowState : IFlowState
{
    public const int Field_OrderId = 0;
    public const int Field_PaymentId = 1;
    public const int FieldCount = 2;

    private int _changedMask;
    public string? FlowId { get; set; }

    private string? _orderId;
    public string? OrderId { get => _orderId; set { _orderId = value; MarkChanged(Field_OrderId); } }

    private string? _paymentId;
    public string? PaymentId { get => _paymentId; set { _paymentId = value; MarkChanged(Field_PaymentId); } }

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_OrderId)) yield return nameof(OrderId);
        if (IsFieldChanged(Field_PaymentId)) yield return nameof(PaymentId);
    }
}

#endregion

#region Test Commands

public record StartPaymentFlowCommand(string OrderId, string ParentFlowId) : IRequest { public long MessageId => 0; }
public record StartInventoryFlowCommand(string OrderId, string ParentFlowId) : IRequest { public long MessageId => 0; }
public record PrimaryPaymentCommand(string OrderId) : IRequest<string> { public long MessageId => 0; }
public record BackupPaymentCommand(string OrderId) : IRequest<string> { public long MessageId => 0; }
public record CompleteOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record CancelOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }

#endregion

#region Test Flow Configs

public class WhenAllFlowConfig : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("whenall-flow");

        flow.WhenAll(
            s => new StartPaymentFlowCommand(s.OrderId!, s.FlowId!),
            s => new StartInventoryFlowCommand(s.OrderId!, s.FlowId!)
        ).Timeout(TimeSpan.FromMinutes(5));

        flow.Send(s => new CompleteOrderCommand(s.OrderId!));
    }
}

public class WhenAllWithCompensationFlowConfig : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("whenall-compensation-flow");

        flow.WhenAll(
            s => new StartPaymentFlowCommand(s.OrderId!, s.FlowId!),
            s => new StartInventoryFlowCommand(s.OrderId!, s.FlowId!)
        )
        .Timeout(TimeSpan.FromMinutes(5))
        .IfAnyFail(s => new CancelOrderCommand(s.OrderId!));
    }
}

public class WhenAnyFlowConfig : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("whenany-flow");

        flow.WhenAny(
            s => new PrimaryPaymentCommand(s.OrderId!),
            s => new BackupPaymentCommand(s.OrderId!)
        ).Timeout(TimeSpan.FromMinutes(5));
    }
}

public class WhenAnyWithResultFlowConfig : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("whenany-result-flow");

        flow.WhenAny<string>(
            s => new PrimaryPaymentCommand(s.OrderId!),
            s => new BackupPaymentCommand(s.OrderId!)
        )
        .Timeout(TimeSpan.FromMinutes(5))
        .Into(s => s.PaymentId);
    }
}

#endregion
