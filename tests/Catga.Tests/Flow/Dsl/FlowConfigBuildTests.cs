using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowConfig.Build() method
/// </summary>
public class FlowConfigBuildTests
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

    #region Build Tests

    [Fact]
    public void Build_CreatesBuilder()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Builder.Should().NotBeNull();
    }

    [Fact]
    public void Build_PopulatesSteps()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Builder.Steps.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_MultipleTimes_OnlyConfiguresOnce()
    {
        var config = new TestFlowConfig();
        config.Build();
        var stepCount = config.Builder.Steps.Count;

        config.Build();
        config.Build();

        config.Builder.Steps.Count.Should().Be(stepCount);
    }

    #endregion

    #region Steps Property Tests

    [Fact]
    public void Steps_ReturnsBuilderSteps()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Steps.Should().BeSameAs(config.Builder.Steps);
    }

    [Fact]
    public void Steps_NotEmpty()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Steps.Should().NotBeEmpty();
    }

    #endregion

    private class TestFlowConfig : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow
                .Name("TestFlow")
                .Send(s => new TestCommand("1"))
                .Send(s => new TestCommand("2"));
        }
    }
}
