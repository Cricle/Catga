using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder message type handling (Send, Query, Publish)
/// </summary>
public class FlowBuilderMessageTypeTests
{
    private class TestState : BaseFlowState
    {
        public string Result { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record TestQuery(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record TestEvent(string Message) : IEvent
    {
    }

    #region Send Tests

    [Fact]
    public void Send_CreatesSendStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Send_MultipleSends()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps.All(s => s.Type == StepType.Send).Should().BeTrue();
    }

    #endregion

    #region Query Tests

    [Fact]
    public void Query_CreatesQueryStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery("q1"));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Query);
    }

    [Fact]
    public void Query_WithInto()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery("q1")).Into(s => s.Result);

        builder.Steps[0].ResultSetter.Should().NotBeNull();
    }

    #endregion

    #region Publish Tests

    [Fact]
    public void Publish_CreatesPublishStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new TestEvent("event"));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Publish);
    }

    [Fact]
    public void Publish_MultiplePublishes()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Publish(s => new TestEvent("1"))
            .Publish(s => new TestEvent("2"))
            .Publish(s => new TestEvent("3"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps.All(s => s.Type == StepType.Publish).Should().BeTrue();
    }

    #endregion

    #region Mixed Message Types Tests

    [Fact]
    public void MixedTypes_AllTypesSupported()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .Query(s => new TestQuery("query"))
            .Publish(s => new TestEvent("event"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[0].Type.Should().Be(StepType.Send);
        builder.Steps[1].Type.Should().Be(StepType.Query);
        builder.Steps[2].Type.Should().Be(StepType.Publish);
    }

    #endregion
}
