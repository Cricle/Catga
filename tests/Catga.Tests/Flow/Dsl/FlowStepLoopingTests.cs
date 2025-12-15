using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep looping configuration
/// </summary>
public class FlowStepLoopingTests
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

    #region ForEach Configuration Tests

    [Fact]
    public void ForEachStep_HasLoopingConfiguration()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b" })
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        var forEachStep = builder.Steps[0];
        forEachStep.Type.Should().Be(StepType.ForEach);
        forEachStep.ForEachSteps.Should().NotBeNull();
    }

    [Fact]
    public void ForEachStep_CanHaveParallelism()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b" })
            .Parallel(2)
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        var forEachStep = builder.Steps[0];
        forEachStep.ForEachParallelism.Should().Be(2);
    }

    [Fact]
    public void ForEachStep_CanHaveBatchSize()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b", "c", "d" })
            .BatchSize(2)
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        var forEachStep = builder.Steps[0];
        forEachStep.ForEachBatchSize.Should().Be(2);
    }

    #endregion

    #region WhenAll/WhenAny Tests

    [Fact]
    public void WhenAllStep_HasParallelSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        );

        var whenAllStep = builder.Steps[0];
        whenAllStep.Type.Should().Be(StepType.WhenAll);
        whenAllStep.ParallelBranches.Should().HaveCount(2);
    }

    [Fact]
    public void WhenAnyStep_HasParallelSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAny(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        );

        var whenAnyStep = builder.Steps[0];
        whenAnyStep.Type.Should().Be(StepType.WhenAny);
        whenAnyStep.ParallelBranches.Should().HaveCount(2);
    }

    #endregion
}
