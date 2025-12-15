using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder validation and constraints
/// </summary>
public class FlowBuilderValidationTests
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

    #region Name Validation Tests

    [Fact]
    public void Name_EmptyString_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("");

        builder.FlowName.Should().BeEmpty();
    }

    [Fact]
    public void Name_LongString_IsValid()
    {
        var longName = new string('a', 1000);
        var builder = new FlowBuilder<TestState>();
        builder.Name(longName);

        builder.FlowName.Should().HaveLength(1000);
    }

    #endregion

    #region Timeout Validation Tests

    [Fact]
    public void Timeout_Zero_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.Zero).ForTag("test");

        builder.TaggedTimeouts["test"].Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Timeout_NegativeValue_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(-1)).ForTag("test");

        builder.TaggedTimeouts["test"].TotalSeconds.Should().Be(-1);
    }

    #endregion

    #region Retry Validation Tests

    [Fact]
    public void Retry_Zero_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(0).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(0);
    }

    [Fact]
    public void Retry_NegativeValue_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(-1).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(-1);
    }

    [Fact]
    public void Retry_LargeValue_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(1000).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(1000);
    }

    #endregion

    #region Tag Validation Tests

    [Fact]
    public void Tag_EmptyString_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag("");

        builder.Steps[0].Tag.Should().BeEmpty();
    }

    [Fact]
    public void Tag_SpecialCharacters_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag("tag-with_special.chars");

        builder.Steps[0].Tag.Should().Contain("special");
    }

    #endregion
}
