using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for ForEach configuration options
/// </summary>
public class ForEachConfigurationTests
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

    #region Parallelism Tests

    [Fact]
    public void WithParallelism_SetsMaxParallelism()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithParallelism(8)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps[0].MaxParallelism.Should().Be(8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(100)]
    public void WithParallelism_VariousValues_Work(int parallelism)
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithParallelism(parallelism)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps[0].MaxParallelism.Should().Be(parallelism);
    }

    #endregion

    #region Batch Size Tests

    [Fact]
    public void WithBatchSize_SetsBatchSize()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithBatchSize(50)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps[0].BatchSize.Should().Be(50);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void WithBatchSize_VariousValues_Work(int batchSize)
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithBatchSize(batchSize)
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps[0].BatchSize.Should().Be(batchSize);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void ContinueOnFailure_SetsContinueOnFailure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .ContinueOnFailure()
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps[0].ContinueOnFailure.Should().BeTrue();
    }

    [Fact]
    public void StopOnFirstFailure_SetsStopOnFirstFailure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .StopOnFirstFailure()
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        builder.Steps[0].ContinueOnFailure.Should().BeFalse();
    }

    #endregion

    #region Combined Configuration Tests

    [Fact]
    public void AllOptions_CanBeCombined()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => s.Items)
            .WithParallelism(4)
            .WithBatchSize(10)
            .ContinueOnFailure()
            .Send((s, item) => new ProcessItemCommand(item))
            .EndForEach();

        var step = builder.Steps[0];
        step.MaxParallelism.Should().Be(4);
        step.BatchSize.Should().Be(10);
        step.ContinueOnFailure.Should().BeTrue();
    }

    #endregion
}
