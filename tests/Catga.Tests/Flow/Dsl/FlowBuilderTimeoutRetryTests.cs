using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder timeout and retry configuration
/// </summary>
public class FlowBuilderTimeoutRetryTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Default Timeout Tests

    [Fact]
    public void DefaultTimeout_HasDefaultValue()
    {
        var builder = new FlowBuilder<TestState>();
        builder.DefaultTimeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Timeout_ForTag_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("critical");

        builder.TaggedTimeouts.Should().ContainKey("critical");
        builder.TaggedTimeouts["critical"].Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Timeout_MultipleTagsCanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(10)).ForTag("fast");
        builder.Timeout(TimeSpan.FromMinutes(5)).ForTag("slow");

        builder.TaggedTimeouts.Should().HaveCount(2);
    }

    #endregion

    #region Retry Tests

    [Fact]
    public void Retry_ForTag_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(3).ForTag("retryable");

        builder.TaggedRetries.Should().ContainKey("retryable");
        builder.TaggedRetries["retryable"].Should().Be(3);
    }

    [Fact]
    public void Retry_ZeroRetries_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(0).ForTag("no-retry");

        builder.TaggedRetries["no-retry"].Should().Be(0);
    }

    [Fact]
    public void Retry_MultipleTagsCanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(1).ForTag("once");
        builder.Retry(5).ForTag("many");
        builder.Retry(10).ForTag("persistent");

        builder.TaggedRetries.Should().HaveCount(3);
    }

    #endregion

    #region Persist Tests

    [Fact]
    public void Persist_ForTag_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("important");

        builder.TaggedPersist.Should().Contain("important");
    }

    [Fact]
    public void Persist_MultipleTagsCanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint1");
        builder.Persist().ForTag("checkpoint2");

        builder.TaggedPersist.Should().HaveCount(2);
    }

    #endregion

    #region Combined Configuration Tests

    [Fact]
    public void CombinedConfiguration_AllSettingsApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api-call");
        builder.Retry(3).ForTag("api-call");
        builder.Persist().ForTag("api-call");

        builder.TaggedTimeouts.Should().ContainKey("api-call");
        builder.TaggedRetries.Should().ContainKey("api-call");
        builder.TaggedPersist.Should().Contain("api-call");
    }

    [Fact]
    public void StepWithTag_UsesTaggedConfiguration()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(10)).ForTag("fast");
        builder.Send(s => new TestCommand("cmd")).Tag("fast");

        builder.Steps[0].Tag.Should().Be("fast");
        builder.TaggedTimeouts["fast"].Should().Be(TimeSpan.FromSeconds(10));
    }

    #endregion
}
