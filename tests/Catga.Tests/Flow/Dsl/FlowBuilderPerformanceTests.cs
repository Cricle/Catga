using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using System.Diagnostics;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Performance tests for FlowBuilder
/// </summary>
public class FlowBuilderPerformanceTests
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

    #region Builder Creation Performance Tests

    [Fact]
    public void FlowBuilder_Creation_IsEfficient()
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 1000; i++)
        {
            _ = new FlowBuilder<TestState>();
        }

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public void FlowBuilder_StepAddition_IsEfficient()
    {
        var builder = new FlowBuilder<TestState>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            builder.Send(s => new TestCommand($"cmd-{i}"));
        }

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    #endregion

    #region Configuration Performance Tests

    [Fact]
    public void FlowBuilder_Configuration_IsEfficient()
    {
        var builder = new FlowBuilder<TestState>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            builder.Timeout(TimeSpan.FromSeconds(i)).ForTag($"tag-{i}");
            builder.Retry(i).ForTag($"tag-{i}");
        }

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    #endregion

    #region Memory Efficiency Tests

    [Fact]
    public void FlowBuilder_MemoryUsage_IsReasonable()
    {
        var initialMemory = GC.GetTotalMemory(true);

        var builders = new List<FlowBuilder<TestState>>();
        for (int i = 0; i < 100; i++)
        {
            var builder = new FlowBuilder<TestState>();
            for (int j = 0; j < 10; j++)
            {
                builder.Send(s => new TestCommand($"cmd-{j}"));
            }
            builders.Add(builder);
        }

        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = finalMemory - initialMemory;

        memoryUsed.Should().BeLessThan(50 * 1024 * 1024); // 50 MB
    }

    #endregion
}
