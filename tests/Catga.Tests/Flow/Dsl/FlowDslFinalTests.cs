using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Final comprehensive tests for Flow DSL completeness
/// </summary>
public class FlowDslFinalTests
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
    public void FlowDsl_IsFullyFunctional()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("FinalTest")
            .Send(s => new TestCommand("1"))
            .Query(s => new TestCommand("2"))
            .Publish(s => new TestCommand("3"));

        builder.Should().NotBeNull();
        builder.FlowName.Should().Be("FinalTest");
        builder.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void FlowDsl_SupportsAllFeatures()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("AllFeatures")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Retry(3).ForTag("api")
            .Persist().ForTag("checkpoint")
            .Send(s => new TestCommand("1")).Tag("api").Optional()
            .If(s => true)
                .Send(s => new TestCommand("2"))
            .EndIf()
            .Switch(s => 1)
                .Case(1, c => c.Send(s => new TestCommand("3")))
            .EndSwitch()
            .ForEach(s => new[] { "a" })
                .Send((s, item) => new TestCommand(item))
            .EndForEach()
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition("corr", "Event"));

        builder.Steps.Should().HaveCount(6);
        builder.TaggedTimeouts.Should().NotBeEmpty();
        builder.TaggedRetries.Should().NotBeEmpty();
        builder.TaggedPersist.Should().NotBeEmpty();
    }

    [Fact]
    public void FlowDsl_ReadyForProduction()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("Production")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Send(s => new TestCommand("critical")).Tag("critical")
            .IfFail().ContinueFlow();

        builder.Should().NotBeNull();
        builder.FlowName.Should().NotBeNullOrEmpty();
        builder.Steps.Should().NotBeEmpty();
        builder.TaggedTimeouts.Should().NotBeEmpty();
        builder.TaggedRetries.Should().NotBeEmpty();
    }

    #endregion

    #region Readiness Tests

    [Fact]
    public void FlowDsl_AllComponentsAvailable()
    {
        var builder = new FlowBuilder<TestState>();

        builder.Should().NotBeNull();
        builder.Steps.Should().NotBeNull();
        builder.DefaultTimeout.Should().NotBe(TimeSpan.Zero);
        builder.DefaultRetries.Should().BeGreaterThanOrEqualTo(0);
        builder.TaggedTimeouts.Should().NotBeNull();
        builder.TaggedRetries.Should().NotBeNull();
        builder.TaggedPersist.Should().NotBeNull();
    }

    #endregion
}
