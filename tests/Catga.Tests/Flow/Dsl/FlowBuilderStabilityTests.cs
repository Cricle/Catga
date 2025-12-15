using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Stability tests for FlowBuilder
/// </summary>
public class FlowBuilderStabilityTests
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

    #region Stability Tests

    [Fact]
    public void Stability_RepeatedOperations()
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < 100; i++)
        {
            builder.Name($"Flow-{i}");
        }

        builder.FlowName.Should().Be("Flow-99");
    }

    [Fact]
    public void Stability_ConfigurationOrder()
    {
        var builder1 = new FlowBuilder<TestState>();
        builder1
            .Name("Test")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Send(s => new TestCommand("1"));

        var builder2 = new FlowBuilder<TestState>();
        builder2
            .Send(s => new TestCommand("1"))
            .Name("Test")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        builder1.FlowName.Should().Be(builder2.FlowName);
        builder1.Steps.Count.Should().Be(builder2.Steps.Count);
    }

    [Fact]
    public void Stability_EmptyOperations()
    {
        var builder = new FlowBuilder<TestState>();

        builder.FlowName.Should().BeNull();
        builder.Steps.Should().BeEmpty();
        builder.TaggedTimeouts.Should().BeEmpty();
        builder.TaggedRetries.Should().BeEmpty();
        builder.TaggedPersist.Should().BeEmpty();
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public void Consistency_MultipleReads()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("Test");
        builder.Send(s => new TestCommand("1"));

        var name1 = builder.FlowName;
        var name2 = builder.FlowName;
        var count1 = builder.Steps.Count;
        var count2 = builder.Steps.Count;

        name1.Should().Be(name2);
        count1.Should().Be(count2);
    }

    [Fact]
    public void Consistency_AfterModification()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("Initial");
        var name1 = builder.FlowName;

        builder.Name("Modified");
        var name2 = builder.FlowName;

        name1.Should().Be("Initial");
        name2.Should().Be("Modified");
    }

    #endregion
}
