using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class WhenAllWhenAnyBuilderTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void WhenAll_CreatesWhenAllStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("cmd1")),
            b => b.Send(s => new TestCommand("cmd2"))
        );

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.WhenAll);
    }

    [Fact]
    public void WhenAll_WithTimeout_SetsTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("cmd1")),
            b => b.Send(s => new TestCommand("cmd2"))
        ).Timeout(TimeSpan.FromSeconds(30));

        builder.Steps[0].Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WhenAny_CreatesWhenAnyStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAny(
            b => b.Send(s => new TestCommand("cmd1")),
            b => b.Send(s => new TestCommand("cmd2"))
        );

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.WhenAny);
    }

    [Fact]
    public void WhenAny_WithTimeout_SetsTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAny(
            b => b.Send(s => new TestCommand("cmd1")),
            b => b.Send(s => new TestCommand("cmd2"))
        ).Timeout(TimeSpan.FromSeconds(10));

        builder.Steps[0].Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void WhenAll_MultipleBranches_AllRegistered()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("cmd1")),
            b => b.Send(s => new TestCommand("cmd2")),
            b => b.Send(s => new TestCommand("cmd3")),
            b => b.Send(s => new TestCommand("cmd4"))
        );

        builder.Steps[0].ParallelBranches.Should().HaveCount(4);
    }
}
