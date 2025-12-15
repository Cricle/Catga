using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Scenario tests for FlowBuilder covering real-world use cases
/// </summary>
public class FlowBuilderScenarioTests
{
    private class OrderState : BaseFlowState
    {
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";
        public List<string> Items { get; set; } = new();
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

    private record NotifyCustomerCommand(string OrderId) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region E-Commerce Scenarios

    [Fact]
    public void Scenario_SimpleOrderProcessing()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("SimpleOrderProcessing")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .Send(s => new NotifyCustomerCommand(s.OrderId));

        builder.FlowName.Should().Be("SimpleOrderProcessing");
        builder.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void Scenario_ConditionalPaymentProcessing()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("ConditionalPayment")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .If(s => s.Amount > 1000)
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("high-value")
                .Wait(new WaitCondition("order", "Approved"))
            .Else()
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .EndIf()
            .Send(s => new NotifyCustomerCommand(s.OrderId));

        builder.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void Scenario_BatchItemProcessing()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("BatchProcessing")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .ForEach(s => s.Items)
                .Send((s, item) => new CreateOrderCommand(item))
            .EndForEach()
            .Send(s => new NotifyCustomerCommand(s.OrderId));

        builder.Steps.Should().HaveCount(3);
    }

    #endregion

    #region Status-Based Scenarios

    [Fact]
    public void Scenario_StatusBasedRouting()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("StatusRouting")
            .Switch(s => s.Status)
                .Case("pending", c => c.Send(s => new CreateOrderCommand(s.OrderId)))
                .Case("confirmed", c => c.Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)))
                .Case("shipped", c => c.Send(s => new NotifyCustomerCommand(s.OrderId)))
                .Default(c => c.Send(s => new CreateOrderCommand(s.OrderId)))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
    }

    #endregion

    #region Error Handling Scenarios

    [Fact]
    public void Scenario_RetryablePayment()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("RetryablePayment")
            .Retry(3).ForTag("payment")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("payment")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("payment")
            .Send(s => new NotifyCustomerCommand(s.OrderId));

        builder.TaggedRetries["payment"].Should().Be(3);
        builder.TaggedTimeouts["payment"].Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion
}
