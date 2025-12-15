using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Additional edge case tests for FlowBuilder
/// </summary>
public class FlowBuilderEdgeCaseTests2
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

    #region Edge Case Tests

    [Fact]
    public void EdgeCase_EmptyIfBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true).EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ThenBranch.Should().BeEmpty();
    }

    [Fact]
    public void EdgeCase_EmptySwitchCase()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => 1).EndSwitch();

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void EdgeCase_EmptyForEach()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => Array.Empty<string>()).EndForEach();

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void EdgeCase_ZeroTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.Zero).ForTag("zero");

        builder.TaggedTimeouts["zero"].Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void EdgeCase_ZeroRetry()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(0).ForTag("zero");

        builder.TaggedRetries["zero"].Should().Be(0);
    }

    [Fact]
    public void EdgeCase_LargeTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromHours(24)).ForTag("long");

        builder.TaggedTimeouts["long"].Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void EdgeCase_LargeRetry()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(100).ForTag("many");

        builder.TaggedRetries["many"].Should().Be(100);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void Boundary_SingleStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("only"));

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void Boundary_ManySteps()
    {
        var builder = new FlowBuilder<TestState>();
        for (int i = 0; i < 100; i++)
        {
            builder.Send(s => new TestCommand($"step-{i}"));
        }

        builder.Steps.Should().HaveCount(100);
    }

    #endregion
}
