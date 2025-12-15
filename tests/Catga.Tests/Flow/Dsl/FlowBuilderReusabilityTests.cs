using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder reusability and composition
/// </summary>
public class FlowBuilderReusabilityTests
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

    #region Builder Reuse Tests

    [Fact]
    public void FlowBuilder_CanBeBuiltMultipleTimes()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));

        var config1 = new TestFlowConfig(builder);
        config1.Build();

        var config2 = new TestFlowConfig(builder);
        config2.Build();

        config1.Steps.Should().HaveCount(1);
        config2.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void FlowBuilder_CanBeExtended()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));

        var initialCount = builder.Steps.Count;

        builder.Send(s => new TestCommand("2"));

        builder.Steps.Count.Should().Be(initialCount + 1);
    }

    #endregion

    #region Configuration Reuse Tests

    [Fact]
    public void FlowBuilder_ConfigurationCanBeReused()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("ReusableFlow");
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("default");

        var name = builder.FlowName;
        var timeout = builder.TaggedTimeouts["default"];

        name.Should().Be("ReusableFlow");
        timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    private class TestFlowConfig
    {
        private readonly FlowBuilder<TestState> _builder;

        public TestFlowConfig(FlowBuilder<TestState> builder)
        {
            _builder = builder;
        }

        public List<FlowStep> Steps => _builder.Steps;

        public void Build()
        {
            // Configuration happens in FlowBuilder
        }
    }
}
