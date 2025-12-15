using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder concurrent access and thread safety
/// </summary>
public class FlowBuilderConcurrencyTests
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

    #region Concurrent Builder Creation Tests

    [Fact]
    public void MultipleBuilders_CanBeCreatedConcurrently()
    {
        var builders = new System.Collections.Concurrent.ConcurrentBag<FlowBuilder<TestState>>();

        Parallel.For(0, 100, _ =>
        {
            builders.Add(new FlowBuilder<TestState>());
        });

        builders.Count.Should().Be(100);
    }

    [Fact]
    public void ConcurrentBuilders_AllIndependent()
    {
        var builders = new System.Collections.Concurrent.ConcurrentBag<FlowBuilder<TestState>>();

        Parallel.For(0, 50, i =>
        {
            var builder = new FlowBuilder<TestState>();
            builder.Name($"Flow-{i}");
            builders.Add(builder);
        });

        builders.Select(b => b.FlowName).Distinct().Count().Should().Be(50);
    }

    #endregion

    #region Concurrent Step Addition Tests

    [Fact]
    public void SingleBuilder_ConcurrentStepAddition_Works()
    {
        var builder = new FlowBuilder<TestState>();

        Parallel.For(0, 10, i =>
        {
            builder.Send(s => new TestCommand($"cmd-{i}"));
        });

        builder.Steps.Count.Should().Be(10);
    }

    #endregion

    #region Concurrent Configuration Tests

    [Fact]
    public void ConcurrentBuilders_WithConfiguration_AllConfigured()
    {
        var builders = new System.Collections.Concurrent.ConcurrentBag<FlowBuilder<TestState>>();

        Parallel.For(0, 20, i =>
        {
            var builder = new FlowBuilder<TestState>();
            builder.Name($"Flow-{i}");
            builder.Timeout(TimeSpan.FromSeconds(i + 1)).ForTag("timeout");
            builder.Retry(i).ForTag("retry");
            builders.Add(builder);
        });

        builders.All(b => !string.IsNullOrEmpty(b.FlowName)).Should().BeTrue();
        builders.All(b => b.TaggedTimeouts.ContainsKey("timeout")).Should().BeTrue();
        builders.All(b => b.TaggedRetries.ContainsKey("retry")).Should().BeTrue();
    }

    #endregion
}
