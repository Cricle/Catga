using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for OnlyWhen condition in FlowBuilder
/// </summary>
public class OnlyWhenConditionTests
{
    private class TestState : BaseFlowState
    {
        public bool IsEnabled { get; set; }
        public int Value { get; set; }
        public string Status { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Basic OnlyWhen Tests

    [Fact]
    public void OnlyWhen_SetsCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .OnlyWhen(s => s.IsEnabled);

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    [Fact]
    public void OnlyWhen_WithBoolProperty_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .OnlyWhen(s => s.IsEnabled);

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    [Fact]
    public void OnlyWhen_WithComparison_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .OnlyWhen(s => s.Value > 100);

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    [Fact]
    public void OnlyWhen_WithStringComparison_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .OnlyWhen(s => s.Status == "Active");

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    [Fact]
    public void OnlyWhen_WithComplexCondition_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .OnlyWhen(s => s.IsEnabled && s.Value > 0 && s.Status != "Disabled");

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    #endregion

    #region Multiple Steps Tests

    [Fact]
    public void MultipleSteps_EachWithCondition_AllSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).OnlyWhen(s => s.Value > 0)
            .Send(s => new TestCommand("2")).OnlyWhen(s => s.Value > 10)
            .Send(s => new TestCommand("3")).OnlyWhen(s => s.Value > 100);

        builder.Steps.Should().HaveCount(3);
        builder.Steps.All(s => s.Condition != null).Should().BeTrue();
    }

    #endregion

    #region Combined with Other Modifiers

    [Fact]
    public void OnlyWhen_WithTag_BothApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .Tag("tagged")
            .OnlyWhen(s => s.IsEnabled);

        var step = builder.Steps[0];
        step.Tag.Should().Be("tagged");
        step.Condition.Should().NotBeNull();
    }

    [Fact]
    public void OnlyWhen_WithOptional_BothApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .Optional()
            .OnlyWhen(s => s.IsEnabled);

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.Condition.Should().NotBeNull();
    }

    #endregion
}
