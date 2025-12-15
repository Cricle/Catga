using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder looping constructs
/// </summary>
public class FlowBuilderLoopTests
{
    private class TestState : BaseFlowState
    {
        public List<string> Items { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region ForEach Tests

    [Fact]
    public void ForEach_CreatesForEachStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items).EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void ForEach_WithSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        builder.Steps[0].ForEachSteps.Should().HaveCount(1);
    }

    [Fact]
    public void ForEach_WithParallelism()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .Parallel(4)
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        builder.Steps[0].ForEachParallelism.Should().Be(4);
    }

    [Fact]
    public void ForEach_WithBatchSize()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .BatchSize(10)
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        builder.Steps[0].ForEachBatchSize.Should().Be(10);
    }

    #endregion

    #region WhenAll Tests

    [Fact]
    public void WhenAll_CreatesWhenAllStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        );

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.WhenAll);
    }

    [Fact]
    public void WhenAll_WithMultipleBranches()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2")),
            b => b.Send(s => new TestCommand("3"))
        );

        builder.Steps[0].ParallelBranches.Should().HaveCount(3);
    }

    #endregion

    #region WhenAny Tests

    [Fact]
    public void WhenAny_CreatesWhenAnyStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAny(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        );

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.WhenAny);
    }

    [Fact]
    public void WhenAny_WithMultipleBranches()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAny(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2")),
            b => b.Send(s => new TestCommand("3"))
        );

        builder.Steps[0].ParallelBranches.Should().HaveCount(3);
    }

    #endregion
}
