using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class ForEachBuilderTests
{
    private class TestState : BaseFlowState
    {
        public List<string> Items { get; set; } = new();
        public Dictionary<string, int> Results { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record ProcessItemCommand(string Item) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void ForEach_CreatesForEachStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void ForEach_WithParallelism_SetsMaxParallelism()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithParallelism(4)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].MaxParallelism.Should().Be(4);
    }

    [Fact]
    public void ForEach_WithBatchSize_SetsBatchSize()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithBatchSize(10)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].BatchSize.Should().Be(10);
    }

    [Fact]
    public void ForEach_WithContinueOnFailure_SetsErrorStrategy()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .ContinueOnFailure()
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ContinueOnFailure.Should().BeTrue();
    }

    [Fact]
    public void ForEach_WithStopOnFirstFailure_SetsErrorStrategy()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .StopOnFirstFailure()
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ContinueOnFailure.Should().BeFalse();
    }

    [Fact]
    public void ForEach_ChainedConfiguration_AllSettingsApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithParallelism(8)
            .WithBatchSize(20)
            .ContinueOnFailure()
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].MaxParallelism.Should().Be(8);
        builder.Steps[0].BatchSize.Should().Be(20);
        builder.Steps[0].ContinueOnFailure.Should().BeTrue();
    }
}
