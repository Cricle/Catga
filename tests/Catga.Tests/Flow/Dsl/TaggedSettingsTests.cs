using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for tagged settings (Timeout, Retry, Persist) in FlowBuilder
/// </summary>
public class TaggedSettingsTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region Timeout ForTag Tests

    [Fact]
    public void Timeout_ForTag_AddsToTaggedTimeouts()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        builder.TaggedTimeouts.Should().ContainKey("api");
        builder.TaggedTimeouts["api"].Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Timeout_MultipleTags_AllAdded()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(10)).ForTag("fast");
        builder.Timeout(TimeSpan.FromSeconds(60)).ForTag("slow");
        builder.Timeout(TimeSpan.FromMinutes(5)).ForTag("background");

        builder.TaggedTimeouts.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    [InlineData(3600)]
    public void Timeout_VariousSeconds_AllWork(int seconds)
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(seconds)).ForTag("test");

        builder.TaggedTimeouts["test"].TotalSeconds.Should().Be(seconds);
    }

    #endregion

    #region Retry ForTag Tests

    [Fact]
    public void Retry_ForTag_AddsToTaggedRetries()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(3).ForTag("retryable");

        builder.TaggedRetries.Should().ContainKey("retryable");
        builder.TaggedRetries["retryable"].Should().Be(3);
    }

    [Fact]
    public void Retry_MultipleTags_AllAdded()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(1).ForTag("once");
        builder.Retry(3).ForTag("few");
        builder.Retry(10).ForTag("many");

        builder.TaggedRetries.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void Retry_VariousCounts_AllWork(int retries)
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(retries).ForTag("test");

        builder.TaggedRetries["test"].Should().Be(retries);
    }

    #endregion

    #region Persist ForTag Tests

    [Fact]
    public void Persist_ForTag_AddsToTaggedPersist()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    [Fact]
    public void Persist_MultipleTags_AllAdded()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("cp1");
        builder.Persist().ForTag("cp2");
        builder.Persist().ForTag("cp3");

        builder.TaggedPersist.Should().HaveCount(3);
    }

    [Fact]
    public void Persist_SameTagTwice_OnlyOneEntry()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint");
        builder.Persist().ForTag("checkpoint");

        builder.TaggedPersist.Should().HaveCount(1);
    }

    #endregion

    #region Combined Settings Tests

    [Fact]
    public void AllSettings_SameTag_AllApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");
        builder.Retry(3).ForTag("api");
        builder.Persist().ForTag("api");

        builder.TaggedTimeouts.Should().ContainKey("api");
        builder.TaggedRetries.Should().ContainKey("api");
        builder.TaggedPersist.Should().Contain("api");
    }

    #endregion
}
