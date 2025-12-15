using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Regression tests for Flow DSL to ensure stability
/// </summary>
public class FlowDslRegressionTests
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

    #region Regression Tests

    [Fact]
    public void FlowBuilder_StepsAreImmutable()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));
        var count1 = builder.Steps.Count;

        builder.Send(s => new TestCommand("2"));
        var count2 = builder.Steps.Count;

        count2.Should().Be(count1 + 1);
    }

    [Fact]
    public void FlowBuilder_ConfigurationPersists()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("Test");
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        var name1 = builder.FlowName;
        var timeout1 = builder.TaggedTimeouts["api"];

        builder.Send(s => new TestCommand("1"));

        var name2 = builder.FlowName;
        var timeout2 = builder.TaggedTimeouts["api"];

        name1.Should().Equal(name2);
        timeout1.Should().Equal(timeout2);
    }

    [Fact]
    public void FlowBuilder_MultipleBuilders_Independent()
    {
        var builder1 = new FlowBuilder<TestState>();
        builder1.Name("Flow1");
        builder1.Send(s => new TestCommand("1"));

        var builder2 = new FlowBuilder<TestState>();
        builder2.Name("Flow2");
        builder2.Send(s => new TestCommand("2"));

        builder1.FlowName.Should().Be("Flow1");
        builder2.FlowName.Should().Be("Flow2");
        builder1.Steps.Should().HaveCount(1);
        builder2.Steps.Should().HaveCount(1);
    }

    #endregion

    #region Stability Tests

    [Fact]
    public void FlowBuilder_HandlesEmptyFlow()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("EmptyFlow");

        builder.FlowName.Should().Be("EmptyFlow");
        builder.Steps.Should().BeEmpty();
    }

    [Fact]
    public void FlowBuilder_HandlesLargeFlow()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("LargeFlow");

        for (int i = 0; i < 50; i++)
        {
            builder.Send(s => new TestCommand($"cmd-{i}"));
        }

        builder.Steps.Should().HaveCount(50);
    }

    [Fact]
    public void FlowBuilder_HandlesDeepNesting()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .If(s => true)
                .If(s => true)
                    .Send(s => new TestCommand("deep"))
                .EndIf()
            .EndIf()
        .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ThenBranch.Should().HaveCount(1);
    }

    #endregion
}
