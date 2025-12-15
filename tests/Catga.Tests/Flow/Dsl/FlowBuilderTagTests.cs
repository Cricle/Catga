using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder tag configuration
/// </summary>
public class FlowBuilderTagTests
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

    #region Tag Configuration Tests

    [Fact]
    public void Tag_CanBeAppliedToStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag("api");

        builder.Steps[0].Tag.Should().Be("api");
    }

    [Fact]
    public void Tag_MultipleStepsWithSameTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2")).Tag("api")
            .Send(s => new TestCommand("3")).Tag("api");

        builder.Steps.All(s => s.Tag == "api").Should().BeTrue();
    }

    [Fact]
    public void Tag_MultipleStepsWithDifferentTags()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2")).Tag("database")
            .Send(s => new TestCommand("3")).Tag("notification");

        builder.Steps[0].Tag.Should().Be("api");
        builder.Steps[1].Tag.Should().Be("database");
        builder.Steps[2].Tag.Should().Be("notification");
    }

    #endregion

    #region Tagged Settings Tests

    [Fact]
    public void TaggedTimeout_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        builder.TaggedTimeouts["api"].Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TaggedRetry_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(3).ForTag("api");

        builder.TaggedRetries["api"].Should().Be(3);
    }

    [Fact]
    public void TaggedPersist_CanBeConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    [Fact]
    public void TaggedSettings_MultipleTagsConfigured()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("database")
            .Retry(3).ForTag("api")
            .Retry(5).ForTag("database")
            .Persist().ForTag("checkpoint");

        builder.TaggedTimeouts.Should().HaveCount(2);
        builder.TaggedRetries.Should().HaveCount(2);
        builder.TaggedPersist.Should().HaveCount(1);
    }

    #endregion
}
