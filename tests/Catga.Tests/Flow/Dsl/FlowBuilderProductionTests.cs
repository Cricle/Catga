using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Production readiness tests for FlowBuilder
/// </summary>
public class FlowBuilderProductionTests
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

    #region Production Ready Tests

    [Fact]
    public void Production_FullConfiguration()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("ProductionFlow")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Persist().ForTag("checkpoint")
            .OnFlowCompleted(s => new CreateOrderCommand(s.OrderId))
            .OnFlowFailed((s, error) => new CreateOrderCommand(s.OrderId));

        builder.FlowName.Should().Be("ProductionFlow");
        builder.TaggedTimeouts.Should().ContainKey("critical");
        builder.TaggedRetries.Should().ContainKey("critical");
        builder.TaggedPersist.Should().Contain("checkpoint");
        builder.OnFlowCompletedFactory.Should().NotBeNull();
        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    [Fact]
    public void Production_CompleteWorkflow()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Name("OrderProcessing")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("payment")
            .Retry(3).ForTag("payment")
            .Send(s => new CreateOrderCommand(s.OrderId))
            .If(s => s.Amount > 1000)
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount)).Tag("payment")
            .Else()
                .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .EndIf();

        builder.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void Production_ErrorHandling()
    {
        var builder = new FlowBuilder<OrderState>();
        builder
            .Send(s => new CreateOrderCommand(s.OrderId))
            .IfFail().ContinueFlow()
            .Send(s => new ProcessPaymentCommand(s.OrderId, s.Amount))
            .IfFail().StopFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
        builder.Steps[1].FailureAction.Should().NotBeNull();
    }

    #endregion

    #region Deployment Ready Tests

    [Fact]
    public void Deployment_AllFeaturesAvailable()
    {
        var builder = new FlowBuilder<OrderState>();

        builder.Should().NotBeNull();
        builder.Steps.Should().NotBeNull();
        builder.TaggedTimeouts.Should().NotBeNull();
        builder.TaggedRetries.Should().NotBeNull();
        builder.TaggedPersist.Should().NotBeNull();
    }

    [Fact]
    public void Deployment_DefaultsAreReasonable()
    {
        var builder = new FlowBuilder<OrderState>();

        builder.DefaultTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        builder.DefaultRetries.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion
}
