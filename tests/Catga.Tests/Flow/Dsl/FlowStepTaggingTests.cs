using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep tagging and tag-based configuration
/// </summary>
public class FlowStepTaggingTests
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

    #region Tag Assignment Tests

    [Fact]
    public void Step_CanBeTagged()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag("api");

        builder.Steps[0].Tag.Should().Be("api");
    }

    [Fact]
    public void MultipleSteps_CanHaveSameTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2")).Tag("api")
            .Send(s => new TestCommand("3")).Tag("api");

        builder.Steps.All(s => s.Tag == "api").Should().BeTrue();
    }

    [Fact]
    public void MultipleSteps_CanHaveDifferentTags()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2")).Tag("database")
            .Send(s => new TestCommand("3")).Tag("cache");

        builder.Steps[0].Tag.Should().Be("api");
        builder.Steps[1].Tag.Should().Be("database");
        builder.Steps[2].Tag.Should().Be("cache");
    }

    #endregion

    #region Tag-Based Configuration Tests

    [Fact]
    public void TaggedTimeout_AppliesOnlyToTaggedSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2"));

        builder.TaggedTimeouts["api"].Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TaggedRetry_AppliesOnlyToTaggedSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Retry(3).ForTag("api")
            .Send(s => new TestCommand("1")).Tag("api")
            .Send(s => new TestCommand("2"));

        builder.TaggedRetries["api"].Should().Be(3);
    }

    [Fact]
    public void TaggedPersist_AppliesOnlyToTaggedSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Persist().ForTag("checkpoint")
            .Send(s => new TestCommand("1")).Tag("checkpoint")
            .Send(s => new TestCommand("2"));

        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    #endregion

    #region Tag Combination Tests

    [Fact]
    public void Step_CanHaveMultipleConfigurations()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Persist().ForTag("critical")
            .Send(s => new TestCommand("cmd")).Tag("critical");

        builder.TaggedTimeouts.Should().ContainKey("critical");
        builder.TaggedRetries.Should().ContainKey("critical");
        builder.TaggedPersist.Should().Contain("critical");
    }

    #endregion
}
