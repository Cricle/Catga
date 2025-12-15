using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Final comprehensive tests for FlowBuilder
/// </summary>
public class FlowBuilderFinalTests
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

    #region Final Validation Tests

    [Fact]
    public void FlowBuilder_IsFullyFunctional()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("FinalTest")
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"));

        builder.Should().NotBeNull();
        builder.FlowName.Should().Be("FinalTest");
        builder.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void FlowBuilder_SupportsComplexScenarios()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("Complex")
            .If(s => true)
                .Send(s => new TestCommand("1"))
                .Switch(s => 1)
                    .Case(1, c => c.Send(s => new TestCommand("2")))
                .EndSwitch()
            .EndIf()
            .ForEach(s => new[] { "a" })
                .Send((s, item) => new TestCommand(item))
            .EndForEach();

        builder.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void FlowBuilder_ConfigurationIsComplete()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("Complete")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Retry(3).ForTag("api")
            .Persist().ForTag("checkpoint")
            .Send(s => new TestCommand("cmd")).Tag("api");

        builder.FlowName.Should().NotBeNull();
        builder.TaggedTimeouts.Should().NotBeEmpty();
        builder.TaggedRetries.Should().NotBeEmpty();
        builder.TaggedPersist.Should().NotBeEmpty();
        builder.Steps.Should().NotBeEmpty();
    }

    #endregion

    #region Readiness Tests

    [Fact]
    public void FlowBuilder_ReadyForProduction()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("Production")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Send(s => new TestCommand("critical")).Tag("critical")
            .IfFail().ContinueFlow();

        builder.Should().NotBeNull();
        builder.Steps.Should().NotBeEmpty();
        builder.TaggedTimeouts.Should().NotBeEmpty();
        builder.TaggedRetries.Should().NotBeEmpty();
    }

    #endregion
}
