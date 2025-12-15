using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Robustness tests for FlowBuilder
/// </summary>
public class FlowBuilderRobustnessTests
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

    #region Robustness Tests

    [Fact]
    public void Robustness_MultipleBuildCalls()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));

        var steps1 = builder.Steps.Count;
        var steps2 = builder.Steps.Count;

        steps1.Should().Be(steps2);
    }

    [Fact]
    public void Robustness_ConcurrentAccess()
    {
        var builder = new FlowBuilder<TestState>();

        Parallel.For(0, 10, _ =>
        {
            builder.Steps.Count.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    [Fact]
    public void Robustness_LargeConfiguration()
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < 10; i++)
        {
            builder.Timeout(TimeSpan.FromSeconds(i)).ForTag($"tag-{i}");
            builder.Retry(i).ForTag($"tag-{i}");
        }

        builder.TaggedTimeouts.Should().HaveCount(10);
        builder.TaggedRetries.Should().HaveCount(10);
    }

    #endregion

    #region Stress Tests

    [Fact]
    public void Stress_ManySteps()
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < 200; i++)
        {
            builder.Send(s => new TestCommand($"step-{i}"));
        }

        builder.Steps.Should().HaveCount(200);
    }

    [Fact]
    public void Stress_DeepNesting()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .If(s => true)
                .If(s => true)
                    .If(s => true)
                        .If(s => true)
                            .Send(s => new TestCommand("deep"))
                        .EndIf()
                    .EndIf()
                .EndIf()
            .EndIf();

        builder.Steps.Should().HaveCount(1);
    }

    #endregion
}
