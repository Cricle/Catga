using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Integration tests for FlowConfig and FlowBuilder
/// </summary>
public class FlowConfigurationIntegrationTests
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

    #region FlowConfig Integration Tests

    [Fact]
    public void FlowConfig_Build_CreatesBuilder()
    {
        var config = new SimpleFlowConfig();
        config.Build();

        config.Builder.Should().NotBeNull();
    }

    [Fact]
    public void FlowConfig_Build_PopulatesSteps()
    {
        var config = new SimpleFlowConfig();
        config.Build();

        config.Steps.Should().NotBeEmpty();
    }

    [Fact]
    public void FlowConfig_Steps_MatchBuilderSteps()
    {
        var config = new SimpleFlowConfig();
        config.Build();

        config.Steps.Should().BeSameAs(config.Builder.Steps);
    }

    #endregion

    #region FlowConfig with Configuration Tests

    [Fact]
    public void FlowConfig_WithName_Works()
    {
        var config = new NamedFlowConfig();
        config.Build();

        config.Builder.FlowName.Should().Be("NamedFlow");
    }

    [Fact]
    public void FlowConfig_WithTimeout_Works()
    {
        var config = new TimeoutFlowConfig();
        config.Build();

        config.Builder.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    private class SimpleFlowConfig : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Send(s => new TestCommand("test"));
        }
    }

    private class NamedFlowConfig : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Name("NamedFlow");
        }
    }

    private class TimeoutFlowConfig : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Timeout(TimeSpan.FromSeconds(30)).ForTag("default");
        }
    }
}
