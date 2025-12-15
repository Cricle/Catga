using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Advanced scenario tests for FlowBuilder covering real-world use cases
/// </summary>
public class FlowBuilderAdvancedScenarioTests
{
    private class OrderProcessingState : BaseFlowState
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

    private record NotifyCustomerCommand(string OrderId) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region E-Commerce Order Processing Scenario

    [Fact]
    public void OrderProcessing_HighValueOrder_WithApproval()
    {
        var builder = new FlowBuilder<OrderProcessingState>();
        builder
            .Name("OrderProcessing")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("payment")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("notification")
            .Retry(3).ForTag("payment")
            .Send(s => new CreateOrderCommand(s.OrderId)).Tag("payment")
            .If(s => s.Amount > 1000)
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("payment")
                .Wait(new WaitCondition($"order-{s.OrderId}", "ApprovalReceived"))
            .Else()
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("payment")
            .EndIf()
            .Send(s => new NotifyCustomerCommand(s.OrderId)).Tag("notification");

        builder.Steps.Should().HaveCount(4);
        builder.TaggedTimeouts.Should().HaveCount(2);
        builder.TaggedRetries.Should().ContainKey("payment");
    }

    #endregion

    #region Batch Processing Scenario

    [Fact]
    public void BatchProcessing_WithParallelization()
    {
        var builder = new FlowBuilder<OrderProcessingState>();
        builder
            .Name("BatchProcessing")
            .ForEach(s => new[] { "order1", "order2", "order3" })
                .Send((s, orderId) => new CreateOrderCommand(orderId))
                .Send((s, orderId) => new ProcessPaymentCommand(orderId, s.Amount))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.ForEach);
    }

    #endregion

    #region Resilient Processing Scenario

    [Fact]
    public void ResilientProcessing_WithFallback()
    {
        var builder = new FlowBuilder<OrderProcessingState>();
        builder
            .Name("ResilientProcessing")
            .Retry(5).ForTag("critical")
            .Send(s => new CreateOrderCommand(s.OrderId)).Tag("critical")
            .IfFail().ContinueFlow()
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("critical")
            .IfFail().ContinueFlow()
            .Delay(TimeSpan.FromSeconds(5))
            .Send(s => new NotifyCustomerCommand(s.OrderId));

        builder.Steps.Should().HaveCount(5);
        builder.TaggedRetries.Should().ContainKey("critical");
    }

    #endregion

    #region Multi-Step Workflow Scenario

    [Fact]
    public void MultiStepWorkflow_ComplexBranching()
    {
        var builder = new FlowBuilder<OrderProcessingState>();
        builder
            .Name("ComplexWorkflow")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .Switch(s => s.Status)
                .Case("pending", c => c
                    .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
                    .Send(s => new NotifyCustomerCommand(s.OrderId)))
                .Case("approved", c => c
                    .Send(s => new NotifyCustomerCommand(s.OrderId)))
            .EndSwitch()
            .Delay(TimeSpan.FromSeconds(1));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[1].Type.Should().Be(StepType.Switch);
    }

    #endregion
}
