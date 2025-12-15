using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder name and configuration
/// </summary>
public class FlowBuilderNameConfigurationTests
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

    #region Name Tests

    [Fact]
    public void Name_SetsFlowName()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("MyFlow");

        builder.FlowName.Should().Be("MyFlow");
    }

    [Fact]
    public void Name_EmptyString_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("");

        builder.FlowName.Should().BeEmpty();
    }

    [Fact]
    public void Name_SpecialCharacters_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("Flow-Name_v1.0 (test)");

        builder.FlowName.Should().Be("Flow-Name_v1.0 (test)");
    }

    [Fact]
    public void Name_Unicode_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("フロー名_日本語");

        builder.FlowName.Should().Contain("日本語");
    }

    [Fact]
    public void Name_CalledMultipleTimes_LastValueWins()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("First");
        builder.Name("Second");
        builder.Name("Third");

        builder.FlowName.Should().Be("Third");
    }

    #endregion

    #region Default Timeout Tests

    [Fact]
    public void DefaultTimeout_HasValue()
    {
        var builder = new FlowBuilder<TestState>();

        builder.DefaultTimeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region Default Retries Tests

    [Fact]
    public void DefaultRetries_IsZero()
    {
        var builder = new FlowBuilder<TestState>();

        builder.DefaultRetries.Should().Be(0);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void FluentConfiguration_AllSettings()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("ConfiguredFlow")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("api")
            .Retry(5).ForTag("api")
            .Persist().ForTag("checkpoint");

        builder.FlowName.Should().Be("ConfiguredFlow");
        builder.TaggedTimeouts["api"].Should().Be(TimeSpan.FromSeconds(60));
        builder.TaggedRetries["api"].Should().Be(5);
        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    [Fact]
    public void FluentConfiguration_WithSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("StepsFlow")
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"));

        builder.FlowName.Should().Be("StepsFlow");
        builder.Steps.Should().HaveCount(2);
    }

    #endregion

    #region Concurrent Tests

    [Fact]
    public void FlowBuilder_ConcurrentCreation_AllIndependent()
    {
        var builders = new System.Collections.Concurrent.ConcurrentBag<FlowBuilder<TestState>>();

        Parallel.For(0, 100, i =>
        {
            var builder = new FlowBuilder<TestState>();
            builder.Name($"Flow-{i}");
            builders.Add(builder);
        });

        builders.Count.Should().Be(100);
        builders.Select(b => b.FlowName).Distinct().Count().Should().Be(100);
    }

    #endregion
}
