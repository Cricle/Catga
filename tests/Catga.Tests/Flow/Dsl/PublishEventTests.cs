using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for Publish event step
/// </summary>
public class PublishEventTests
{
    private class TestState : BaseFlowState
    {
        public string Id { get; set; } = "";
        public string Status { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record OrderCreatedEvent(string OrderId) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record StatusChangedEvent(string Id, string OldStatus, string NewStatus) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Basic Publish Tests

    [Fact]
    public void Publish_CreatesPublishStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new OrderCreatedEvent(s.Id));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Publish);
    }

    [Fact]
    public void Publish_ComplexEvent_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new StatusChangedEvent(s.Id, "old", s.Status));

        builder.Steps[0].Type.Should().Be(StepType.Publish);
    }

    #endregion

    #region Chained Publish Tests

    [Fact]
    public void MultiplePublish_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Publish(s => new OrderCreatedEvent(s.Id))
            .Publish(s => new StatusChangedEvent(s.Id, "a", "b"));

        builder.Steps.Should().HaveCount(2);
        builder.Steps.All(s => s.Type == StepType.Publish).Should().BeTrue();
    }

    [Fact]
    public void Publish_ChainedWithTag_TagApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new OrderCreatedEvent(s.Id)).Tag("notification");

        builder.Steps[0].Tag.Should().Be("notification");
    }

    #endregion

    #region Publish in Branches Tests

    [Fact]
    public void Publish_InIfBranch_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Status == "Active")
            .Publish(s => new OrderCreatedEvent(s.Id))
        .EndIf();

        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.Publish);
    }

    [Fact]
    public void Publish_InSwitchCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case("Created", c => c.Publish(s => new OrderCreatedEvent(s.Id)))
        .EndSwitch();

        builder.Steps[0].Cases["Created"][0].Type.Should().Be(StepType.Publish);
    }

    #endregion

    #region Mixed Steps Tests

    [Fact]
    public void SendThenPublish_BothCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand(s.Id))
            .Publish(s => new OrderCreatedEvent(s.Id));

        builder.Steps.Should().HaveCount(2);
        builder.Steps[0].Type.Should().Be(StepType.Send);
        builder.Steps[1].Type.Should().Be(StepType.Publish);
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #endregion
}
