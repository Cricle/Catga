using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class PublishBuilderTests
{
    private class TestState : BaseFlowState
    {
        public string EventType { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestEvent(string Type) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void Publish_CreatesPublishStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new TestEvent(s.EventType));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Publish);
    }

    [Fact]
    public void Publish_WithTag_SetsTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new TestEvent(s.EventType)).Tag("event-tag");

        builder.Steps[0].Tag.Should().Be("event-tag");
    }

    [Fact]
    public void Publish_ChainedWithOtherSteps_AddsMultipleSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Publish(s => new TestEvent("event1"))
            .Publish(s => new TestEvent("event2"))
            .Publish(s => new TestEvent("event3"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps.All(s => s.Type == StepType.Publish).Should().BeTrue();
    }

    [Fact]
    public void Publish_WithOptional_SetsOptionalFlag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new TestEvent(s.EventType)).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
    }
}
