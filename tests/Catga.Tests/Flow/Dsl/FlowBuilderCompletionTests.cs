using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Comprehensive completion tests for FlowBuilder functionality
/// </summary>
public class FlowBuilderCompletionTests
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

    #region Complete Feature Coverage Tests

    [Fact]
    public void FlowBuilder_AllFeatures_CanBeUsedTogether()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("CompleteFlow")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Retry(3).ForTag("api")
            .Persist().ForTag("checkpoint")
            .Send(s => new TestCommand("1")).Tag("api")
            .If(s => true)
                .Send(s => new TestCommand("2"))
            .EndIf()
            .Switch(s => 1)
                .Case(1, c => c.Send(s => new TestCommand("3")))
            .EndSwitch()
            .ForEach(s => new[] { "a", "b" })
                .Send((s, item) => new TestCommand(item))
            .EndForEach()
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition("corr-123", "Event"));

        builder.FlowName.Should().Be("CompleteFlow");
        builder.Steps.Should().HaveCount(6);
        builder.TaggedTimeouts.Should().ContainKey("api");
        builder.TaggedRetries.Should().ContainKey("api");
        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    #endregion

    #region Feature Combination Tests

    [Fact]
    public void FlowBuilder_BranchingAndLooping_Combined()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .If(s => true)
                .ForEach(s => new[] { "1", "2" })
                    .Send((s, item) => new TestCommand(item))
                .EndForEach()
            .EndIf();

        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void FlowBuilder_SwitchAndWaitCondition_Combined()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Switch(s => 1)
                .Case(1, c => c
                    .Send(s => new TestCommand("1"))
                    .Wait(new WaitCondition("corr-1", "Event1")))
                .Case(2, c => c
                    .Send(s => new TestCommand("2"))
                    .Wait(new WaitCondition("corr-2", "Event2")))
            .EndSwitch();

        builder.Steps[0].Cases[1].Should().HaveCount(2);
        builder.Steps[0].Cases[2].Should().HaveCount(2);
    }

    #endregion

    #region Scalability Tests

    [Fact]
    public void FlowBuilder_ManySteps_Works()
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < 100; i++)
        {
            builder.Send(s => new TestCommand($"cmd-{i}"));
        }

        builder.Steps.Should().HaveCount(100);
    }

    [Fact]
    public void FlowBuilder_DeepNesting_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .If(s => true)
                .If(s => true)
                    .Send(s => new TestCommand("deep"))
                .EndIf()
            .EndIf()
        .EndIf();

        builder.Steps[0].ThenBranch[0].ThenBranch[0].Type.Should().Be(StepType.If);
    }

    #endregion
}
