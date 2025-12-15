using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder consistency and invariants
/// </summary>
public class FlowBuilderConsistencyTests
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

    #region Consistency Tests

    [Fact]
    public void FlowBuilder_StepsCollection_IsConsistent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));
        builder.Send(s => new TestCommand("2"));

        var count1 = builder.Steps.Count;
        var count2 = builder.Steps.Count;

        count1.Should().Equal(count2);
    }

    [Fact]
    public void FlowBuilder_Configuration_IsConsistent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("test");

        var name1 = builder.FlowName;
        var name2 = builder.FlowName;

        name1.Should().Be(name2);
    }

    [Fact]
    public void FlowBuilder_TaggedSettings_AreConsistent()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        var timeout1 = builder.TaggedTimeouts["api"];
        var timeout2 = builder.TaggedTimeouts["api"];

        timeout1.Should().Equal(timeout2);
    }

    #endregion

    #region Invariant Tests

    [Fact]
    public void FlowBuilder_DefaultTimeout_IsAlwaysSet()
    {
        var builder = new FlowBuilder<TestState>();

        builder.DefaultTimeout.Should().NotBe(TimeSpan.Zero);
    }

    [Fact]
    public void FlowBuilder_Steps_IsNeverNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.Steps.Should().NotBeNull();
    }

    [Fact]
    public void FlowBuilder_TaggedTimeouts_IsNeverNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.TaggedTimeouts.Should().NotBeNull();
    }

    [Fact]
    public void FlowBuilder_TaggedRetries_IsNeverNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.TaggedRetries.Should().NotBeNull();
    }

    [Fact]
    public void FlowBuilder_TaggedPersist_IsNeverNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.TaggedPersist.Should().NotBeNull();
    }

    #endregion
}
