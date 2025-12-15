using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep event callbacks
/// </summary>
public class FlowStepEventTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record TestEvent(string Message) : IEvent
    {
    }

    #region Step Event Tests

    [Fact]
    public void Step_CanHaveOnCompletedCallback()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .OnCompleted(s => new TestEvent("completed"));

        var step = builder.Steps[0];
        step.OnCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void Step_CanHaveOnFailedCallback()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .OnFailed((s, error) => new TestEvent($"failed: {error}"));

        var step = builder.Steps[0];
        step.OnFailedFactory.Should().NotBeNull();
    }

    #endregion

    #region Flow Event Tests

    [Fact]
    public void Flow_CanHaveOnCompletedCallback()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowCompleted(s => new TestEvent("flow completed"));

        builder.OnFlowCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void Flow_CanHaveOnFailedCallback()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowFailed((s, error) => new TestEvent($"flow failed: {error}"));

        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    #endregion

    #region Multiple Event Callbacks Tests

    [Fact]
    public void MultipleSteps_CanHaveDifferentCallbacks()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .OnCompleted(s => new TestEvent("step1 completed"))
            .Send(s => new TestCommand("2"))
            .OnFailed((s, error) => new TestEvent($"step2 failed: {error}"));

        builder.Steps[0].OnCompletedFactory.Should().NotBeNull();
        builder.Steps[1].OnFailedFactory.Should().NotBeNull();
    }

    #endregion
}
