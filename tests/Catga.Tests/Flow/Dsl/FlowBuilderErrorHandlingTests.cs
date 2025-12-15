using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder error handling and edge cases
/// </summary>
public class FlowBuilderErrorHandlingTests
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

    #region Null Handling Tests

    [Fact]
    public void FlowBuilder_Name_WithNull_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name(null);

        builder.FlowName.Should().BeNull();
    }

    [Fact]
    public void FlowBuilder_Tag_WithNull_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag(null);

        builder.Steps[0].Tag.Should().BeNull();
    }

    #endregion

    #region Boundary Value Tests

    [Fact]
    public void FlowBuilder_Timeout_WithZero_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.Zero).ForTag("test");

        builder.TaggedTimeouts["test"].Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void FlowBuilder_Retry_WithZero_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(0).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(0);
    }

    [Fact]
    public void FlowBuilder_Retry_WithNegative_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(-1).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(-1);
    }

    #endregion

    #region Large Value Tests

    [Fact]
    public void FlowBuilder_Retry_WithLargeValue_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(int.MaxValue).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(int.MaxValue);
    }

    [Fact]
    public void FlowBuilder_Timeout_WithLargeValue_Accepted()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.MaxValue).ForTag("test");

        builder.TaggedTimeouts["test"].Should().Be(TimeSpan.MaxValue);
    }

    #endregion
}
