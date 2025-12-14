using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class FlowConfigTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : ICommand
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private class TestFlowConfig : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Send(s => new TestCommand(s.Data));
        }
    }

    [Fact]
    public void FlowConfig_CanBeCreated()
    {
        var config = new TestFlowConfig();
        config.Should().NotBeNull();
    }

    [Fact]
    public void FlowConfig_Build_CreatesBuilder()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Builder.Should().NotBeNull();
    }

    [Fact]
    public void FlowConfig_Build_ConfiguresSteps()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void FlowConfig_Build_CalledTwice_OnlyConfiguresOnce()
    {
        var config = new TestFlowConfig();
        config.Build();
        config.Build();

        config.Builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void FlowConfig_Steps_ReturnsBuilderSteps()
    {
        var config = new TestFlowConfig();
        config.Build();

        config.Steps.Should().BeSameAs(config.Builder.Steps);
    }
}
