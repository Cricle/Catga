using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Integration tests for complete Flow DSL workflows
/// </summary>
public class FlowDslIntegrationTests
{
    private class OrderState : BaseFlowState
    {
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";
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

    #region End-to-End Flow Tests

    [Fact]
    public void E2E_OrderProcessing_Flow()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("OrderProcessing")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("payment")
            .Retry(3).ForTag("payment")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .If(s => s.Amount > 1000)
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("payment")
            .Else()
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .EndIf()
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition($"order-{builder.FlowName}", "Confirmation"));

        builder.FlowName.Should().Be("OrderProcessing");
        builder.Steps.Should().HaveCount(4);
    }

    [Fact]
    public void E2E_BatchProcessing_Flow()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("BatchProcessing")
            .ForEach(s => new[] { "order1", "order2", "order3" })
                .Send((s, orderId) => new CreateOrderCommand(orderId))
                .Send((s, orderId) => new ProcessPaymentCommand(orderId, s.Amount))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void E2E_ConditionalBranching_Flow()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("ConditionalFlow")
            .Switch(s => s.Status)
                .Case("pending", c => c.Send(s => new CreateOrderCommand(s.OrderId)))
                .Case("confirmed", c => c.Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)))
                .Default(c => c.Send(s => new CreateOrderCommand(s.OrderId)))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
    }

    #endregion

    #region Complex Workflow Tests

    [Fact]
    public void ComplexWorkflow_MultipleFeatures()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("ComplexWorkflow")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Retry(3).ForTag("api")
            .Persist().ForTag("checkpoint")
            .Send(s => new CreateOrderCommand(s.OrderId)).Tag("api")
            .If(s => s.Amount > 1000)
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("api")
                .Wait(new WaitCondition($"order-{s.OrderId}", "Approved"))
            .Else()
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .EndIf()
            .ForEach(s => new[] { "notification", "confirmation" })
                .Send((s, item) => new CreateOrderCommand(item))
            .EndForEach()
            .Delay(TimeSpan.FromSeconds(1));

        builder.FlowName.Should().Be("ComplexWorkflow");
        builder.Steps.Should().HaveCount(4);
        builder.TaggedTimeouts.Should().ContainKey("api");
        builder.TaggedRetries.Should().ContainKey("api");
        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    #endregion
}
