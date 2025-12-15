using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder event callbacks (OnStepCompleted, OnFlowCompleted, etc.)
/// </summary>
public class FlowBuilderEventCallbackTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public int StepCount { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record StepCompletedEvent(string FlowId, int StepIndex) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record StepFailedEvent(string FlowId, int StepIndex, string? Error) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record FlowCompletedEvent(string FlowId) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record FlowFailedEvent(string FlowId, string? Error) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region OnStepCompleted Tests

    [Fact]
    public void OnStepCompleted_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnStepCompleted((state, stepIndex) => new StepCompletedEvent(state.FlowId, stepIndex));

        builder.OnStepCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void OnStepCompleted_FactoryReturnsCorrectEvent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnStepCompleted((state, stepIndex) => new StepCompletedEvent(state.FlowId, stepIndex));

        var state = new TestState { FlowId = "test-flow" };
        var evt = builder.OnStepCompletedFactory!(state, 5);

        evt.Should().BeOfType<StepCompletedEvent>();
        ((StepCompletedEvent)evt).FlowId.Should().Be("test-flow");
        ((StepCompletedEvent)evt).StepIndex.Should().Be(5);
    }

    #endregion

    #region OnStepFailed Tests

    [Fact]
    public void OnStepFailed_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnStepFailed((state, stepIndex, error) => new StepFailedEvent(state.FlowId, stepIndex, error));

        builder.OnStepFailedFactory.Should().NotBeNull();
    }

    [Fact]
    public void OnStepFailed_FactoryReturnsCorrectEvent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnStepFailed((state, stepIndex, error) => new StepFailedEvent(state.FlowId, stepIndex, error));

        var state = new TestState { FlowId = "failed-flow" };
        var evt = builder.OnStepFailedFactory!(state, 3, "Test error");

        evt.Should().BeOfType<StepFailedEvent>();
        ((StepFailedEvent)evt).Error.Should().Be("Test error");
    }

    #endregion

    #region OnFlowCompleted Tests

    [Fact]
    public void OnFlowCompleted_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowCompleted(state => new FlowCompletedEvent(state.FlowId));

        builder.OnFlowCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void OnFlowCompleted_FactoryReturnsCorrectEvent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowCompleted(state => new FlowCompletedEvent(state.FlowId));

        var state = new TestState { FlowId = "completed-flow" };
        var evt = builder.OnFlowCompletedFactory!(state);

        evt.Should().BeOfType<FlowCompletedEvent>();
        ((FlowCompletedEvent)evt).FlowId.Should().Be("completed-flow");
    }

    #endregion

    #region OnFlowFailed Tests

    [Fact]
    public void OnFlowFailed_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowFailed((state, error) => new FlowFailedEvent(state.FlowId, error));

        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    [Fact]
    public void OnFlowFailed_FactoryReturnsCorrectEvent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowFailed((state, error) => new FlowFailedEvent(state.FlowId, error));

        var state = new TestState { FlowId = "failed-flow" };
        var evt = builder.OnFlowFailedFactory!(state, "Flow failed");

        evt.Should().BeOfType<FlowFailedEvent>();
        ((FlowFailedEvent)evt).Error.Should().Be("Flow failed");
    }

    #endregion

    #region Combined Callbacks Tests

    [Fact]
    public void AllCallbacks_CanBeConfiguredTogether()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .OnStepCompleted((state, stepIndex) => new StepCompletedEvent(state.FlowId, stepIndex))
            .OnStepFailed((state, stepIndex, error) => new StepFailedEvent(state.FlowId, stepIndex, error))
            .OnFlowCompleted(state => new FlowCompletedEvent(state.FlowId))
            .OnFlowFailed((state, error) => new FlowFailedEvent(state.FlowId, error));

        builder.OnStepCompletedFactory.Should().NotBeNull();
        builder.OnStepFailedFactory.Should().NotBeNull();
        builder.OnFlowCompletedFactory.Should().NotBeNull();
        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    #endregion
}
