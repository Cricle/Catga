using Catga.Abstractions;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// TDD tests for FlowConfig DSL parsing.
/// </summary>
public class FlowConfigDslTests
{
    #region Basic DSL

    [Fact]
    public void FlowConfig_Name_SetsFlowName()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.Name.Should().Be("create-order");
    }

    [Fact]
    public void FlowConfig_Send_AddsStep()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.Steps.Should().HaveCountGreaterThan(0);
        config.Steps[0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void FlowConfig_Query_AddsStepWithResult()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var queryStep = config.Steps.FirstOrDefault(s => s.Type == StepType.Query);
        queryStep.Should().NotBeNull();
        queryStep!.HasResult.Should().BeTrue();
    }

    [Fact]
    public void FlowConfig_Publish_AddsPublishStep()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var publishStep = config.Steps.FirstOrDefault(s => s.Type == StepType.Publish);
        publishStep.Should().NotBeNull();
    }

    [Fact]
    public void FlowConfig_StepsInOrder()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.Steps.Should().HaveCount(4);
        config.Steps[0].Type.Should().Be(StepType.Send);    // SaveOrder
        config.Steps[1].Type.Should().Be(StepType.Query);   // GetDiscount
        config.Steps[2].Type.Should().Be(StepType.Send);    // ProcessPayment
        config.Steps[3].Type.Should().Be(StepType.Publish); // OrderCreated
    }

    #endregion

    #region Step Configuration

    [Fact]
    public void Step_IfFail_SetsCompensation()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var step = config.Steps[0];
        step.HasCompensation.Should().BeTrue();
    }

    [Fact]
    public void Step_Into_SetsResultProperty()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var queryStep = config.Steps.First(s => s.Type == StepType.Query);
        queryStep.ResultPropertyName.Should().Be("Discount");
    }

    [Fact]
    public void Step_Tag_SetsTags()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var step = config.Steps[0];
        step.Tags.Should().Contain("persistence");
    }

    [Fact]
    public void Step_OnlyWhen_SetsCondition()
    {
        var config = new TestConditionalFlowConfig();
        config.Build();

        var step = config.Steps.First(s => s.HasCondition);
        step.HasCondition.Should().BeTrue();
    }

    [Fact]
    public void Step_FailIf_SetsFailCondition()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var paymentStep = config.Steps[2];
        paymentStep.HasFailCondition.Should().BeTrue();
    }

    #endregion

    #region Global Settings

    [Fact]
    public void FlowConfig_Timeout_SetsDefaultTimeout()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.DefaultTimeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void FlowConfig_Timeout_ForTags_SetsTaggedTimeout()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var paymentTimeout = config.GetTimeoutForTag("payment");
        paymentTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FlowConfig_Retry_ForTags_SetsTaggedRetry()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var criticalRetry = config.GetRetryForTag("critical");
        criticalRetry.Should().Be(3);
    }

    [Fact]
    public void FlowConfig_Persist_ForTags_SetsTaggedPersist()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var checkpointPersist = config.ShouldPersistForTag("checkpoint");
        checkpointPersist.Should().BeTrue();
    }

    #endregion

    #region Event Hooks

    [Fact]
    public void FlowConfig_OnFlowCompleted_SetsHook()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.HasOnFlowCompletedHook.Should().BeTrue();
    }

    [Fact]
    public void FlowConfig_OnFlowFailed_SetsHook()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.HasOnFlowFailedHook.Should().BeTrue();
    }

    [Fact]
    public void FlowConfig_OnStepCompleted_SetsHook()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.HasOnStepCompletedHook.Should().BeTrue();
    }

    [Fact]
    public void Step_OnCompleted_SetsStepHook()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        var paymentStep = config.Steps[2];
        paymentStep.HasOnCompletedHook.Should().BeTrue();
    }

    #endregion

    #region WhenAll/WhenAny

    [Fact]
    public void FlowConfig_WhenAll_AddsWhenAllStep()
    {
        var config = new TestParallelFlowConfig();
        config.Build();

        var whenAllStep = config.Steps.FirstOrDefault(s => s.Type == StepType.WhenAll);
        whenAllStep.Should().NotBeNull();
        whenAllStep!.ChildRequestCount.Should().Be(2);
    }

    [Fact]
    public void FlowConfig_WhenAny_AddsWhenAnyStep()
    {
        var config = new TestParallelFlowConfig();
        config.Build();

        var whenAnyStep = config.Steps.FirstOrDefault(s => s.Type == StepType.WhenAny);
        whenAnyStep.Should().NotBeNull();
    }

    [Fact]
    public void WhenAll_Timeout_SetsTimeout()
    {
        var config = new TestParallelFlowConfig();
        config.Build();

        var whenAllStep = config.Steps.First(s => s.Type == StepType.WhenAll);
        whenAllStep.Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void WhenAll_IfAnyFail_SetsCompensation()
    {
        var config = new TestParallelFlowConfig();
        config.Build();

        var whenAllStep = config.Steps.First(s => s.Type == StepType.WhenAll);
        whenAllStep.HasCompensation.Should().BeTrue();
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void FlowConfig_NoSteps_ReturnsEmptyList()
    {
        var config = new EmptyFlowConfig();
        config.Build();

        config.Steps.Should().BeEmpty();
    }

    [Fact]
    public void FlowConfig_NoName_UsesTypeName()
    {
        var config = new NoNameFlowConfig();
        config.Build();

        config.Name.Should().Be("NoNameFlowConfig");
    }

    [Fact]
    public void FlowConfig_DuplicateTags_Allowed()
    {
        var config = new DuplicateTagsFlowConfig();
        config.Build();

        var step = config.Steps[0];
        step.Tags.Should().HaveCount(3);
    }

    #endregion
}

#region Test Flow Configs

public class TestOrderFlowState : IFlowState
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

// Test commands/queries/events
public record SaveOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record DeleteOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record GetDiscountQuery(string CustomerId) : IRequest<decimal> { public long MessageId => 0; }
public record ProcessPaymentCommand(string OrderId, decimal Amount) : IRequest<string> { public long MessageId => 0; }
public record RefundPaymentCommand(string PaymentId) : IRequest { public long MessageId => 0; }
public record CancelOrderCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record OrderCreatedEvent(string OrderId) : IEvent { public long MessageId => 0; }
public record OrderFlowCompletedEvent(string FlowId, string OrderId) : IEvent { public long MessageId => 0; }
public record OrderFlowFailedEvent(string FlowId, string OrderId, string? Error) : IEvent { public long MessageId => 0; }
public record StepCompletedEvent(string FlowId, int Step) : IEvent { public long MessageId => 0; }
public record PaymentStepCompletedEvent(string OrderId, string PaymentId) : IEvent { public long MessageId => 0; }

// Parallel flow commands
public record StartPaymentFlowCommand(string OrderId, string ParentFlowId) : IRequest { public long MessageId => 0; }
public record StartInventoryFlowCommand(string OrderId, string ParentFlowId) : IRequest { public long MessageId => 0; }
public record PrimaryPaymentCommand(string OrderId) : IRequest<string> { public long MessageId => 0; }
public record BackupPaymentCommand(string OrderId) : IRequest<string> { public long MessageId => 0; }

public class TestOrderFlowConfig : FlowConfig<TestOrderFlowState>
{
    protected override void Configure(IFlowBuilder<TestOrderFlowState> flow)
    {
        flow.Name("create-order");

        // Global settings with tag filtering
        flow.Timeout(TimeSpan.FromMinutes(10));
        flow.Timeout(TimeSpan.FromSeconds(30)).ForTags("payment");
        flow.Retry(3).ForTags("critical");
        flow.Persist().ForTags("checkpoint");

        // Event hooks
        flow.OnStepCompleted<StepCompletedEvent>((s, step) => new StepCompletedEvent(s.FlowId!, step));
        flow.OnFlowCompleted<OrderFlowCompletedEvent>(s => new OrderFlowCompletedEvent(s.FlowId!, s.OrderId!));
        flow.OnFlowFailed<OrderFlowFailedEvent>((s, error) => new OrderFlowFailedEvent(s.FlowId!, s.OrderId!, error));

        // Steps
        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .IfFail(s => new DeleteOrderCommand(s.OrderId!))
            .Tag("persistence", "checkpoint");

        flow.Query(s => new GetDiscountQuery(s.CustomerId!))
            .Into(s => s.Discount)
            .Tag("pricing");

        flow.Send<string>(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount - s.Discount))
            .FailIf(p => string.IsNullOrEmpty(p), "Payment failed")
            .Into(s => s.PaymentId)
            .IfFail(s => new RefundPaymentCommand(s.PaymentId!))
            .OnCompleted<PaymentStepCompletedEvent>(s => new PaymentStepCompletedEvent(s.OrderId!, s.PaymentId!))
            .Tag("payment", "critical", "checkpoint");

        flow.Publish(s => new OrderCreatedEvent(s.OrderId!))
            .Tag("notification");
    }
}

public class TestConditionalFlowConfig : FlowConfig<TestOrderFlowState>
{
    protected override void Configure(IFlowBuilder<TestOrderFlowState> flow)
    {
        flow.Name("conditional-flow");

        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .OnlyWhen(s => s.TotalAmount > 0);
    }
}

public class TestParallelFlowConfig : FlowConfig<TestOrderFlowState>
{
    protected override void Configure(IFlowBuilder<TestOrderFlowState> flow)
    {
        flow.Name("parallel-flow");

        flow.WhenAll(
            s => new StartPaymentFlowCommand(s.OrderId!, s.FlowId!),
            s => new StartInventoryFlowCommand(s.OrderId!, s.FlowId!)
        )
        .Timeout(TimeSpan.FromMinutes(5))
        .IfAnyFail(s => new CancelOrderCommand(s.OrderId!));

        flow.WhenAny(
            s => new PrimaryPaymentCommand(s.OrderId!),
            s => new BackupPaymentCommand(s.OrderId!)
        )
        .Into(s => s.PaymentId);
    }
}

public class EmptyFlowConfig : FlowConfig<TestOrderFlowState>
{
    protected override void Configure(IFlowBuilder<TestOrderFlowState> flow)
    {
        // No steps
    }
}

public class NoNameFlowConfig : FlowConfig<TestOrderFlowState>
{
    protected override void Configure(IFlowBuilder<TestOrderFlowState> flow)
    {
        flow.Send(s => new SaveOrderCommand(s.OrderId!));
    }
}

public class DuplicateTagsFlowConfig : FlowConfig<TestOrderFlowState>
{
    protected override void Configure(IFlowBuilder<TestOrderFlowState> flow)
    {
        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .Tag("tag1", "tag2", "tag1"); // Duplicate tag1
    }
}

#endregion
