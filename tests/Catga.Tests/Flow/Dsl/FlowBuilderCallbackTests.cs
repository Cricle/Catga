using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder callback configuration
/// </summary>
public class FlowBuilderCallbackTests
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

    #region Flow Callback Tests

    [Fact]
    public void Callback_OnFlowCompleted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowCompleted(s => new TestEvent("completed"));

        builder.OnFlowCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void Callback_OnFlowFailed()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowFailed((s, error) => new TestEvent($"failed: {error}"));

        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    [Fact]
    public void Callback_OnStepCompleted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnStepCompleted((s, idx) => new TestEvent($"step {idx} completed"));

        builder.OnStepCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void Callback_OnStepFailed()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnStepFailed((s, idx, error) => new TestEvent($"step {idx} failed: {error}"));

        builder.OnStepFailedFactory.Should().NotBeNull();
    }

    #endregion

    #region Step Callback Tests

    [Fact]
    public void StepCallback_OnCompleted()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .OnCompleted(s => new TestEvent("step completed"));

        builder.Steps[0].OnCompletedFactory.Should().NotBeNull();
    }

    [Fact]
    public void StepCallback_OnFailed()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .OnFailed((s, error) => new TestEvent($"step failed: {error}"));

        builder.Steps[0].OnFailedFactory.Should().NotBeNull();
    }

    [Fact]
    public void StepCallback_Both()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .OnCompleted(s => new TestEvent("completed"))
            .OnFailed((s, error) => new TestEvent($"failed: {error}"));

        builder.Steps[0].OnCompletedFactory.Should().NotBeNull();
        builder.Steps[0].OnFailedFactory.Should().NotBeNull();
    }

    #endregion

    #region Combined Callback Tests

    [Fact]
    public void Callbacks_AllConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .OnFlowCompleted(s => new TestEvent("flow completed"))
            .OnFlowFailed((s, error) => new TestEvent($"flow failed: {error}"))
            .OnStepCompleted((s, idx) => new TestEvent($"step {idx} completed"))
            .OnStepFailed((s, idx, error) => new TestEvent($"step {idx} failed: {error}"))
            .Send(s => new TestCommand("cmd"))
            .OnCompleted(s => new TestEvent("step completed"))
            .OnFailed((s, error) => new TestEvent($"step failed: {error}"));

        builder.OnFlowCompletedFactory.Should().NotBeNull();
        builder.OnFlowFailedFactory.Should().NotBeNull();
        builder.OnStepCompletedFactory.Should().NotBeNull();
        builder.OnStepFailedFactory.Should().NotBeNull();
        builder.Steps[0].OnCompletedFactory.Should().NotBeNull();
        builder.Steps[0].OnFailedFactory.Should().NotBeNull();
    }

    #endregion
}
