using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests demonstrating FlowBuilder usage patterns and best practices
/// </summary>
public class FlowBuilderDocumentationTests
{
    private class OrderState : BaseFlowState
    {
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
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

    #region Basic Flow Pattern Tests

    [Fact]
    public void BasicFlow_CreateAndProcess_Works()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("OrderProcessing")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount));

        builder.FlowName.Should().Be("OrderProcessing");
        builder.Steps.Should().HaveCount(2);
    }

    #endregion

    #region Conditional Flow Pattern Tests

    [Fact]
    public void ConditionalFlow_HighValueOrder_Works()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("SmartOrderProcessing")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .If(s => s.Amount > 1000)
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .EndIf();

        builder.Steps.Should().HaveCount(2);
        builder.Steps[1].Type.Should().Be(StepType.If);
    }

    #endregion

    #region Tagged Configuration Pattern Tests

    [Fact]
    public void TaggedConfiguration_WithTimeouts_Works()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("ConfiguredFlow")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("payment")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("notification")
            .Send(s => new CreateOrderCommand(s.OrderId)).Tag("payment")
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("notification");

        builder.TaggedTimeouts.Should().HaveCount(2);
        builder.Steps.Should().HaveCount(2);
    }

    #endregion

    #region Error Handling Pattern Tests

    [Fact]
    public void ErrorHandling_WithRetry_Works()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("ResilientFlow")
            .Retry(3).ForTag("external")
            .Send(s => new CreateOrderCommand(s.OrderId)).Tag("external")
            .IfFail().ContinueFlow();

        builder.TaggedRetries.Should().ContainKey("external");
        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    #endregion
}
