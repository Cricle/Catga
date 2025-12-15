using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Complete configuration tests for Flow DSL
/// </summary>
public class FlowConfigurationCompleteTests
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

    #region Complete Configuration Tests

    [Fact]
    public void FlowConfiguration_Complete_AllSettingsApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("CompleteFlow")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("database")
            .Retry(3).ForTag("api")
            .Retry(5).ForTag("database")
            .Persist().ForTag("checkpoint")
            .OnFlowCompleted(s => new TestCommand("completed"))
            .OnFlowFailed((s, error) => new TestCommand($"failed: {error}"))
            .OnStepCompleted((s, idx) => new TestCommand($"step {idx} completed"))
            .OnStepFailed((s, idx, error) => new TestCommand($"step {idx} failed: {error}"))
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2")).Tag("database")
            .Send(s => new TestCommand("3")).Tag("checkpoint");

        builder.FlowName.Should().Be("CompleteFlow");
        builder.Steps.Should().HaveCount(3);
        builder.TaggedTimeouts.Should().HaveCount(2);
        builder.TaggedRetries.Should().HaveCount(2);
        builder.TaggedPersist.Should().HaveCount(1);
        builder.OnFlowCompletedFactory.Should().NotBeNull();
        builder.OnFlowFailedFactory.Should().NotBeNull();
        builder.OnStepCompletedFactory.Should().NotBeNull();
        builder.OnStepFailedFactory.Should().NotBeNull();
    }

    #endregion

    #region Configuration Consistency Tests

    [Fact]
    public void FlowConfiguration_IsConsistent()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("Test")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Send(s => new TestCommand("1")).Tag("api");

        var name1 = builder.FlowName;
        var name2 = builder.FlowName;
        var timeout1 = builder.TaggedTimeouts["api"];
        var timeout2 = builder.TaggedTimeouts["api"];

        name1.Should().Be(name2);
        timeout1.Should().Equal(timeout2);
    }

    #endregion
}
