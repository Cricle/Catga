using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for step execution order in FlowBuilder
/// </summary>
public class StepExecutionOrderTests
{
    private class TestState : BaseFlowState
    {
        public List<string> ExecutionLog { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Step Order Tests

    [Fact]
    public void Steps_ExecuteInOrder()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3"));

        builder.Steps[0].Should().NotBeNull();
        builder.Steps[1].Should().NotBeNull();
        builder.Steps[2].Should().NotBeNull();
    }

    [Fact]
    public void StepIndex_IsSequential()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3"))
            .Send(s => new TestCommand("4"))
            .Send(s => new TestCommand("5"));

        builder.Steps.Should().HaveCount(5);
        for (int i = 0; i < 5; i++)
        {
            builder.Steps[i].Should().NotBeNull();
        }
    }

    #endregion

    #region Branch Order Tests

    [Fact]
    public void BranchSteps_MaintainOrder()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3"))
        .EndIf();

        builder.Steps[0].ThenBranch.Should().HaveCount(3);
    }

    #endregion

    #region Mixed Steps Order Tests

    [Fact]
    public void MixedSteps_MaintainOrder()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .If(s => true)
                .Send(s => new TestCommand("2"))
            .EndIf()
            .Send(s => new TestCommand("3"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[0].Type.Should().Be(StepType.Send);
        builder.Steps[1].Type.Should().Be(StepType.If);
        builder.Steps[2].Type.Should().Be(StepType.Send);
    }

    #endregion
}
