using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder state mutation and immutability patterns
/// </summary>
public class FlowBuilderMutationTests
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

    #region Builder Mutation Tests

    [Fact]
    public void FlowBuilder_Name_ReturnsBuilder()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Name("test");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void FlowBuilder_Send_ReturnsBuilder()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Send(s => new TestCommand("cmd"));

        result.Should().NotBeNull();
    }

    [Fact]
    public void FlowBuilder_StepsCollection_IsMutable()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));
        var count1 = builder.Steps.Count;

        builder.Send(s => new TestCommand("2"));
        var count2 = builder.Steps.Count;

        count2.Should().Be(count1 + 1);
    }

    #endregion

    #region Configuration Mutation Tests

    [Fact]
    public void FlowBuilder_TimeoutConfiguration_IsMutable()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        builder.TaggedTimeouts.Should().ContainKey("api");

        builder.Timeout(TimeSpan.FromSeconds(60)).ForTag("api");
        builder.TaggedTimeouts["api"].Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void FlowBuilder_RetryConfiguration_IsMutable()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(3).ForTag("retry");

        builder.TaggedRetries["retry"].Should().Be(3);

        builder.Retry(5).ForTag("retry");
        builder.TaggedRetries["retry"].Should().Be(5);
    }

    #endregion

    #region Chaining Mutation Tests

    [Fact]
    public void FlowBuilder_ChainedMutations_AllApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("test")
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("default");

        builder.FlowName.Should().Be("test");
        builder.Steps.Should().HaveCount(2);
        builder.TaggedTimeouts.Should().ContainKey("default");
    }

    #endregion
}
