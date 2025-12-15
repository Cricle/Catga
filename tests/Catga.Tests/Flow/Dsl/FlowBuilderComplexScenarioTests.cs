using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Complex end-to-end scenario tests for FlowBuilder
/// </summary>
public class FlowBuilderComplexScenarioTests
{
    private class OrderState : BaseFlowState
    {
        public string OrderId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public decimal Amount { get; set; }
        public List<string> Items { get; set; } = new();
        public string Status { get; set; } = "Pending";
        public bool IsApproved { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record CreateOrderCommand(string OrderId) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record ProcessPaymentCommand(string OrderId, decimal Amount) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record NotifyCustomerCommand(string CustomerId) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record ProcessItemCommand(string Item) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record OrderCreatedEvent(string OrderId) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region E-Commerce Order Flow Scenarios

    [Fact]
    public void OrderFlow_BasicProcessing_CreatesCorrectSteps()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("OrderProcessingFlow")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .Publish(s => new OrderCreatedEvent(s.OrderId))
            .Send(s => new NotifyCustomerCommand(s.CustomerId));

        builder.FlowName.Should().Be("OrderProcessingFlow");
        builder.Steps.Should().HaveCount(4);
    }

    [Fact]
    public void OrderFlow_WithConditionalApproval_CreatesIfBranch()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Send(s => new CreateOrderCommand(s.OrderId))
            .If(s => s.Amount > 1000)
                .Send(s => new NotifyCustomerCommand(s.CustomerId))
            .EndIf()
            .Publish(s => new OrderCreatedEvent(s.OrderId));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[1].Type.Should().Be(StepType.If);
    }

    [Fact]
    public void OrderFlow_WithStatusSwitch_CreatesSwitchBranch()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Switch(s => s.Status)
                .Case("Pending", c => c.Send(s => new CreateOrderCommand(s.OrderId)))
                .Case("Approved", c => c.Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)))
                .Default(c => c.Send(s => new NotifyCustomerCommand(s.CustomerId)))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
        builder.Steps[0].Cases.Should().HaveCount(2);
    }

    [Fact]
    public void OrderFlow_WithItemProcessing_CreatesForEach()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .ForEach(s => s.Items)
                .WithParallelism(4)
                .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach()
            .Publish(s => new OrderCreatedEvent(s.OrderId));

        builder.Steps.Should().HaveCount(2);
        builder.Steps[0].Type.Should().Be(StepType.ForEach);
        builder.Steps[0].MaxParallelism.Should().Be(4);
    }

    [Fact]
    public void OrderFlow_WithParallelBranches_CreatesWhenAll()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .WhenAll(
                b => b.Send(s => new CreateOrderCommand(s.OrderId)),
                b => b.Send(s => new NotifyCustomerCommand(s.CustomerId))
            )
            .Timeout(TimeSpan.FromSeconds(30))
            .Publish(s => new OrderCreatedEvent(s.OrderId));

        builder.Steps.Should().HaveCount(2);
        builder.Steps[0].Type.Should().Be(StepType.WhenAll);
    }

    #endregion

    #region Complex Nested Scenarios

    [Fact]
    public void ComplexFlow_NestedIfSwitchForEach_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .If(s => s.IsApproved)
                .Switch(s => s.Status)
                    .Case("Ready", c => c
                        .ForEach(s => s.Items)
                            .Send((s, item) => new ProcessItemCommand(item))
                        .EndForEach())
                .EndSwitch()
            .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
        builder.Steps[0].ThenBranch.Should().HaveCount(1);
        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void ComplexFlow_MultipleConditions_CreatesIfElseIfElse()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .If(s => s.Amount > 10000)
                .Send(s => new NotifyCustomerCommand("VIP"))
            .ElseIf(s => s.Amount > 1000)
                .Send(s => new NotifyCustomerCommand("Premium"))
            .ElseIf(s => s.Amount > 100)
                .Send(s => new NotifyCustomerCommand("Standard"))
            .Else()
                .Send(s => new NotifyCustomerCommand("Basic"))
            .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
    }

    #endregion

    #region Configuration Scenarios

    [Fact]
    public void Flow_WithTaggedTimeoutsAndRetries_ConfiguresCorrectly()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api-call")
            .Retry(3).ForTag("api-call")
            .Persist().ForTag("checkpoint")
            .Send(s => new CreateOrderCommand(s.OrderId)).Tag("api-call")
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("checkpoint");

        builder.TaggedTimeouts["api-call"].Should().Be(TimeSpan.FromSeconds(30));
        builder.TaggedRetries["api-call"].Should().Be(3);
        builder.TaggedPersist.Should().Contain("checkpoint");
        builder.Steps[0].Tag.Should().Be("api-call");
        builder.Steps[1].Tag.Should().Be("checkpoint");
    }

    [Fact]
    public void Flow_WithAllEventCallbacks_ConfiguresAll()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .OnStepCompleted((s, i) => new OrderCreatedEvent(s.OrderId))
            .OnStepFailed((s, i, e) => new OrderCreatedEvent(s.OrderId))
            .OnFlowCompleted(s => new OrderCreatedEvent(s.OrderId))
            .OnFlowFailed((s, e) => new OrderCreatedEvent(s.OrderId));

        builder.OnStepCompletedFactory.Should().NotBeNull();
        builder.OnStepFailedFactory.Should().NotBeNull();
        builder.OnFlowCompletedFactory.Should().NotBeNull();
        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    #endregion

    #region Step Modifier Scenarios

    [Fact]
    public void Steps_WithAllModifiers_AppliesAll()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Send(s => new CreateOrderCommand(s.OrderId))
                .Tag("important")
                .Optional()
                .OnlyWhen(s => s.Amount > 0)
                .IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.Tag.Should().Be("important");
        step.IsOptional.Should().BeTrue();
        step.Condition.Should().NotBeNull();
        step.FailureAction.Should().NotBeNull();
    }

    #endregion
}
