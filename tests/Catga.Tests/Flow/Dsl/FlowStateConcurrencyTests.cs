using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Concurrency tests for Flow state handling
/// </summary>
public class FlowStateConcurrencyTests
{
    private class TestState : BaseFlowState
    {
        public int Counter { get; set; }
        public List<string> Log { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    [Fact]
    public void FlowPosition_ConcurrentCreation_AllUnique()
    {
        var positions = new System.Collections.Concurrent.ConcurrentBag<FlowPosition>();

        Parallel.For(0, 100, i =>
        {
            positions.Add(new FlowPosition(new[] { i }));
        });

        positions.Select(p => p.Path[0]).Distinct().Count().Should().Be(100);
    }

    [Fact]
    public void FlowSnapshot_ConcurrentCreation_AllValid()
    {
        var snapshots = new System.Collections.Concurrent.ConcurrentBag<FlowSnapshot<TestState>>();

        Parallel.For(0, 100, i =>
        {
            snapshots.Add(new FlowSnapshot<TestState>
            {
                FlowId = $"flow-{i}",
                State = new TestState { FlowId = $"flow-{i}", Counter = i },
                Position = new FlowPosition(new[] { i }),
                Status = DslFlowStatus.Running
            });
        });

        snapshots.Count.Should().Be(100);
        snapshots.Select(s => s.FlowId).Distinct().Count().Should().Be(100);
    }

    [Fact]
    public void FlowBuilder_ConcurrentBuilding_EachHasOwnSteps()
    {
        var builders = new System.Collections.Concurrent.ConcurrentBag<FlowBuilder<TestState>>();

        Parallel.For(0, 50, i =>
        {
            var builder = new FlowBuilder<TestState>();
            builder.Name($"Flow-{i}");
            for (int j = 0; j < i + 1; j++)
            {
                builder.Delay(TimeSpan.FromSeconds(j));
            }
            builders.Add(builder);
        });

        builders.Count.Should().Be(50);
        // Each builder should have different number of steps
        builders.Select(b => b.Steps.Count).Distinct().Count().Should().Be(50);
    }

    [Fact]
    public void MessageId_ConcurrentGeneration_AllUnique()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(MessageExtensions.NewMessageId());
        });

        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void FlowResult_ConcurrentCreation_NoRaceConditions()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<FlowResult<TestState>>();

        Parallel.For(0, 100, i =>
        {
            results.Add(new FlowResult<TestState>
            {
                Status = i % 2 == 0 ? DslFlowStatus.Completed : DslFlowStatus.Failed,
                State = new TestState { FlowId = $"result-{i}" },
                Error = i % 2 == 0 ? null : $"Error-{i}"
            });
        });

        results.Count.Should().Be(100);
        results.Count(r => r.Status == DslFlowStatus.Completed).Should().Be(50);
        results.Count(r => r.Status == DslFlowStatus.Failed).Should().Be(50);
    }
}
