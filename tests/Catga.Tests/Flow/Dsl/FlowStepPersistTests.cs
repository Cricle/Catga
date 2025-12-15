using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep persistence configuration
/// </summary>
public class FlowStepPersistTests
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

    #region Persist Configuration Tests

    [Fact]
    public void Step_CanBePersisted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint");
        builder.Send(s => new TestCommand("cmd")).Tag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    [Fact]
    public void MultipleSteps_CanSharePersistTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint");
        builder
            .Send(s => new TestCommand("1")).Tag("checkpoint")
            .Send(s => new TestCommand("2")).Tag("checkpoint")
            .Send(s => new TestCommand("3")).Tag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
        builder.Steps.All(s => s.Tag == "checkpoint").Should().BeTrue();
    }

    [Fact]
    public void MultiplePersistTags_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Persist().ForTag("checkpoint1")
            .Persist().ForTag("checkpoint2")
            .Persist().ForTag("checkpoint3");

        builder.TaggedPersist.Should().HaveCount(3);
        builder.TaggedPersist.Should().Contain("checkpoint1");
        builder.TaggedPersist.Should().Contain("checkpoint2");
        builder.TaggedPersist.Should().Contain("checkpoint3");
    }

    #endregion

    #region Persist with Other Configuration Tests

    [Fact]
    public void PersistStep_CanHaveTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Persist().ForTag("checkpoint")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("checkpoint")
            .Send(s => new TestCommand("cmd")).Tag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
        builder.TaggedTimeouts.Should().ContainKey("checkpoint");
    }

    [Fact]
    public void PersistStep_CanHaveRetry()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Persist().ForTag("checkpoint")
            .Retry(3).ForTag("checkpoint")
            .Send(s => new TestCommand("cmd")).Tag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
        builder.TaggedRetries.Should().ContainKey("checkpoint");
    }

    #endregion
}
