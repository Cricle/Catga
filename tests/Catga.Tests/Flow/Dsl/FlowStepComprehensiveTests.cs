using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Comprehensive tests covering all FlowStep aspects
/// </summary>
public class FlowStepComprehensiveTests
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

    #region Complete Step Configuration Tests

    [Fact]
    public void Step_CanHaveAllProperties()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .Tag("api")
            .Optional()
            .OnlyWhen(s => true)
            .IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.Type.Should().Be(StepType.Send);
        step.Tag.Should().Be("api");
        step.IsOptional.Should().BeTrue();
        step.Condition.Should().NotBeNull();
        step.FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void Step_CanHaveTimeoutAndRetry()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Send(s => new TestCommand("cmd")).Tag("critical");

        var step = builder.Steps[0];
        step.Tag.Should().Be("critical");
        builder.TaggedTimeouts["critical"].Should().Be(TimeSpan.FromSeconds(30));
        builder.TaggedRetries["critical"].Should().Be(5);
    }

    #endregion

    #region Step Type Variety Tests

    [Fact]
    public void AllStepTypes_CanBeCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Query(s => new TestCommand("2"))
            .Publish(s => new TestCommand("3"))
            .If(s => true)
                .Send(s => new TestCommand("4"))
            .EndIf()
            .Switch(s => 1)
                .Case(1, c => c.Send(s => new TestCommand("5")))
            .EndSwitch()
            .ForEach(s => new[] { "a" })
                .Send((s, item) => new TestCommand(item))
            .EndForEach()
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition("corr", "Event"));

        builder.Steps.Should().HaveCount(8);
    }

    #endregion

    #region Step Combination Tests

    [Fact]
    public void ComplexFlow_WithAllFeatures()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("ComplexFlow")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("critical")
            .Retry(3).ForTag("critical")
            .Persist().ForTag("checkpoint")
            .Send(s => new TestCommand("1")).Tag("critical").Optional()
            .If(s => true)
                .Send(s => new TestCommand("2")).Tag("critical")
                .OnCompleted(s => new TestCommand("completed"))
            .Else()
                .Send(s => new TestCommand("3"))
                .OnFailed((s, error) => new TestCommand($"failed: {error}"))
            .EndIf()
            .Switch(s => 1)
                .Case(1, c => c
                    .Send(s => new TestCommand("4")).Tag("checkpoint")
                    .Delay(TimeSpan.FromSeconds(1)))
                .Default(c => c.Send(s => new TestCommand("5")))
            .EndSwitch()
            .ForEach(s => new[] { "x", "y" })
                .Send((s, item) => new TestCommand(item))
            .EndForEach()
            .Wait(new WaitCondition("corr", "Event"));

        builder.FlowName.Should().Be("ComplexFlow");
        builder.Steps.Should().HaveCount(5);
        builder.TaggedTimeouts.Should().ContainKey("critical");
        builder.TaggedRetries.Should().ContainKey("critical");
        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    #endregion
}
